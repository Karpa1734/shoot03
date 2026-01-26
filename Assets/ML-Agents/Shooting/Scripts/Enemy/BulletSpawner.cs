using UnityEngine;
using Unity.MLAgents;
using System.Collections;
using System.Collections.Generic;

public class BulletSpawner : MonoBehaviour
{
    public enum TeamSide { Player1, Player2 }
    public TeamSide myTeam;
    public CharacterData characterProfile;

    [Header("Effect Settings")]
    public GameObject delayEffectPrefab;
    [Tooltip("RED, ORANGE, YELLOW, GREEN, AQUA, BLUE, PURPLE, WHITE の順で登録")]
    public Sprite[] delaySprites = new Sprite[8];

    [Header("Unique Skill Settings")]
    [SerializeField] private GameObject warningLinePrefab;
    [SerializeField] private float bottomY = -5.5f;

    [Header("Fan Charge Settings")]
    [SerializeField] private float shrinkSpeed = 200f;
    [SerializeField] private float expandSpeed = 400f;

    [SerializeField] private FanMeshVisualizer fanVisualizer;
    [SerializeField] private float previewRadius = 5f;

    [Header("AI Distance Settings")]
    public float minOptimalDistance = 3.0f; // これより近いと近すぎ
    public float maxOptimalDistance = 7.0f; // これより遠いと遠すぎ

    // 敵座標への正確な位置ではなく、ぶれ（ジッター）の範囲
    [SerializeField] private float targetJitter = 1.5f;
    private Vector3[] remoteTargets = new Vector3[4];
    public Transform Target => target; // ★ 外部からターゲットを参照可能にする
    [Header("Magic Circle Settings")]
    public GameObject magicCirclePrefab;
    public float longPressThreshold = 0.3f; // 長押しと判定する秒数
    private float[] holdTimers = new float[4]; // 各スキルの押し時間を計測

    [Header("Auto Fire Settings")]
    [SerializeField] private float maxChargeHoldTime = 2.0f; // 最大チャージ後に保持できる秒数
    private float[] chargeHoldTimers = new float[4];        // 各スキルの保持時間を計測

    private float currentDodgeSpeedMultiplier = 1f; // 現在の回避による速度倍率
    private float dodgeActiveTimer = 0f;             // 回避の残り時間

    private bool[] isLaunchedBurst = new bool[4]; // 現在のバーストが射出モードかどうかを保持

    [Header("Special Gauge Settings")]
    [SerializeField] private SpecialGaugeManager gaugeManager; 
    [Tooltip("このスキルを使用した時に増加するスペシャルゲージの量")]

    private Transform target;
    private int[] burstRemain = new int[4];
    private float[] burstIntervalTimers = new float[4];
    private bool[] isFiringBurst = new bool[4];

    private bool[] isCharging = new bool[4];
    private float[] currentFanAngles = new float[4];

    private static int largeCounter = 1000;
    private static int middleCounter = 6000;
    private static int smallCounter = 11000;
    // --- 変数宣言部に追加 ---

    private AttackPattern[] attackPatterns = new AttackPattern[5];
    private float[] recastTimers = new float[5];
    private bool[] isInputHeld = new bool[5];

    private float[] fixedChargeAngles = new float[4]; // チャージ開始時に固定した角度を保持
    private MagicCircle[] activeCircles = new MagicCircle[4];
    private float[] moveTimers = new float[4]; // ★ 移動開始からの経過時間を計測用
    private DodgerAgent myAgent;


    // BulletSpawner.cs 内に追加
    public AttackPattern[] GetAttackPatterns()
    {
        return attackPatterns;
    }

    private void Awake()
    {
    }
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    private void Start()
    {
        myAgent = GetComponent<DodgerAgent>(); // エージェントへの参照を取得
        if (characterProfile != null)
        {
            attackPatterns[0] = characterProfile.shotZ;
            attackPatterns[1] = characterProfile.shotX;
            attackPatterns[2] = characterProfile.shotC;
            attackPatterns[3] = characterProfile.shotV;
            attackPatterns[4] = characterProfile.shotZ;
        }

        for (int i = 0; i < 4; i++)
        {
            if (attackPatterns[i] != null && attackPatterns[i].multiShotData != null && attackPatterns[i].multiShotData.Length > 0)
                currentFanAngles[i] = attackPatterns[i].multiShotData[0].nWaySpread;
        }

        gameObject.layer = LayerMask.NameToLayer(myTeam.ToString());
        FindTarget();
    }

    private void Update()
    {
        if (myAgent != null && !myAgent.IsRoundActive)
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        bool anyCharging = false;
        int chargingIndex = -1;

        for (int i = 0; i < 4; i++)
        {
            if (attackPatterns[i] == null) continue;

            if (recastTimers[i] > 0) recastTimers[i] -= Time.deltaTime;

            UpdateFanAngleValues(i);

            // ★ チャージ中の処理を強化
            if (isCharging[i])
            {
                anyCharging = true;
                chargingIndex = i;

                // チャージ進捗が 1.0 (最大) に達しているか確認
                if (GetFanAngleProgress(i) >= 1.0f)
                {
                    // 最大チャージ状態の時間を加算
                    chargeHoldTimers[i] += Time.deltaTime;

                    // 設定時間を超えたら自動発射
                    if (chargeHoldTimers[i] >= maxChargeHoldTime)
                    {
                        isCharging[i] = false;
                        chargeHoldTimers[i] = 0f;
                        StartBurst(i);
                    }
                }
                else
                {
                    // チャージ途中の場合はタイマーをリセット
                    chargeHoldTimers[i] = 0f;
                }
            }
            else
            {
                // チャージしていないスロットのタイマーをリセット
                chargeHoldTimers[i] = 0f;
            }

            if (isFiringBurst[i])
            {
                if (attackPatterns[i].fireType == FireType.Instant && !isInputHeld[i])
                {
                    StopBurstAndStartRecast(i);
                    continue;
                }

                burstIntervalTimers[i] -= Time.deltaTime;
                if (burstIntervalTimers[i] <= 0) Fire(i);
            }
        }

        // --- 以下、FanVisualizer や 回避タイマーの処理はそのまま ---
        if (anyCharging && fanVisualizer != null)
        {
            if (attackPatterns[chargingIndex].multiShotData.Length > 0)
            {
                // ★修正：CalculateLocalAngle を呼ばず、固定済みの角度を使用する
                float angleToUse = fixedChargeAngles[chargingIndex];

                // ステップ回転（必要なら加算。通常は0でOK）
                angleToUse += attackPatterns[chargingIndex].multiShotData[0].rotationPerStep * 0;

                fanVisualizer.DrawFan(angleToUse, currentFanAngles[chargingIndex], previewRadius);
            }
        }
        else if (fanVisualizer != null)
        {
            fanVisualizer.Hide();
        }

        // 回避タイマーの更新
        if (dodgeActiveTimer > 0)
        {
            dodgeActiveTimer -= Time.deltaTime;
            if (dodgeActiveTimer <= 0)
            {
                currentDodgeSpeedMultiplier = 1f; // 速度を戻す

                // ★追加：エージェントに回避アクション終了を通知
                DodgerAgent agent = GetComponent<DodgerAgent>();
                if (agent != null)
                {
                    agent.EndDodgeAction();
                }
            }
        }
    }
    private float CalculateLocalAngle(ShotData data)
    {
        if (data.angleType == ShotData.AngleType.AimAtPlayer && target != null)
        {
            // 親（TrainingArea等）の中での相対座標でベクトルを出す
            Vector2 relativeDir = target.localPosition - transform.localPosition;
            return Mathf.Atan2(relativeDir.y, relativeDir.x) * Mathf.Rad2Deg;
        }
        return (data.angleType == ShotData.AngleType.Random) ? Random.Range(0f, 360f) : data.fixedAngle;
    }
    // 123行目付近
    private void UpdateFanAngleValues(int i)
    {
        // multiShotData[0] を基準にする
        if (attackPatterns[i] == null || attackPatterns[i].multiShotData == null || attackPatterns[i].multiShotData.Length == 0) return;

        float maxAngle = attackPatterns[i].multiShotData[0].nWaySpread;
        float minAngle = 10f;

        if (isCharging[i])
        {
            currentFanAngles[i] = Mathf.MoveTowards(currentFanAngles[i], minAngle, shrinkSpeed * Time.deltaTime);
        }
        else if (!isFiringBurst[i])
        {
            currentFanAngles[i] = Mathf.MoveTowards(currentFanAngles[i], maxAngle, expandSpeed * Time.deltaTime);
        }
    }
    // BulletSpawner.cs 内に追加
    // 139行目付近
    public float GetFanAngleProgress(int index)
    {
        if (index < 0 || index >= 4 || attackPatterns[index] == null || attackPatterns[index].multiShotData.Length == 0) return 0f;

        float max = attackPatterns[index].multiShotData[0].nWaySpread;
        float min = 10f;

        return Mathf.Clamp01((max - currentFanAngles[index]) / (max - min));
    }
    public bool IsCharging(int index) => isCharging[index];
    public void UpdateInputState(int attackAction)
    {
        // アクション5（Dキー）が入力された際の処理
        if (attackAction == 5 && !isInputHeld[4])
        {
            ExecuteSpecialMove();
        }

        for (int i = 0; i < 4; i++)
        {
            if (attackPatterns[i] == null) continue;

            bool currentlyPressed = (attackAction == i + 1);
            bool wasPressed = isInputHeld[i];

            // --- A. 魔方陣（自機/移動）スキルの場合 ---
            if (attackPatterns[i].skillType == SkillType.MagicCircle)
            {
                // 1. 押した瞬間：半分展開(1.5f)で生成
                if (currentlyPressed && !wasPressed && recastTimers[i] <= 0)
                {
                    GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
                    activeCircles[i] = circleObj.GetComponent<MagicCircle>();// ★追加：データ側からゲージを増やす
                    if (gaugeManager != null)
                    {
                        gaugeManager.IncreaseGauge(attackPatterns[i].specialGaugeGain);
                    }
                    if (activeCircles[i] != null)
                    {
                        Color pColor = (characterProfile != null) ? characterProfile.personalColor : Color.white;
                        activeCircles[i].Initialize(pColor, myTeam);

                        // 敵の方向に合わせて向きを決定
                        int dir = (target != null && target.position.x > transform.position.x) ? 1 : -1;
                        activeCircles[i].StartManualMove(dir, 1.5f);
                    }
                }

                // 2. 離した瞬間：その場で完全展開(2.5f)
                if (!currentlyPressed && wasPressed && activeCircles[i] != null)
                {
                    activeCircles[i].DeployAtCurrentPosition(2.5f, 3.0f);
                    recastTimers[i] = attackPatterns[i].recastTime; // 離したときにリキャスト開始
                    activeCircles[i] = null;
                }
            }
            // --- B. 遠隔弾幕スキルの場合 ---
            else if (attackPatterns[i].skillType == SkillType.RemoteBarrage)
            {
                if (currentlyPressed && !wasPressed && recastTimers[i] <= 0 && !isFiringBurst[i])
                {
                    // 単押しで即バースト開始
                    StartBurst(i);
                }
            }
            else if (attackPatterns[i].skillType == SkillType.Dodge)
            {
                if (currentlyPressed && !wasPressed && recastTimers[i] <= 0)
                {
                    ExecuteDodge(i);
                }
            }
            // --- B. 通常スキルの場合（Zキーなどの修正版） ---
            else
            {
                if (currentlyPressed)
                {
                    bool triggerInput = !wasPressed || (attackPatterns[i].isAutoRepeat && recastTimers[i] <= 0 && !isFiringBurst[i]);

                    if (triggerInput && recastTimers[i] <= 0 && !isFiringBurst[i] && !isCharging[i])
                    {
                        if (attackPatterns[i].fireType == FireType.Instant) StartBurst(i);
                        else if (attackPatterns[i].fireType == FireType.OnRelease)
                        {
                            isCharging[i] = true;
                            // ★追加：チャージ開始時の相対角度を固定する
                            if (attackPatterns[i].multiShotData.Length > 0)
                            {
                                fixedChargeAngles[i] = CalculateLocalAngle(attackPatterns[i].multiShotData[0]);
                            }
                        }
                    }
                }

                if (!currentlyPressed && wasPressed)
                {
                    if (attackPatterns[i].fireType == FireType.OnRelease && isCharging[i])
                    {
                        isCharging[i] = false;
                        StartBurst(i);
                    }
                    isCharging[i] = false;
                }
            }
            isInputHeld[i] = currentlyPressed;
        }
        // 全アクションの入力保持状態を更新
        for (int i = 0; i < 5; i++)
        {
            isInputHeld[i] = (attackAction == i + 1);
        }
    }
    private void ExecuteSpecialMove()
    {
        // ゲージが100%（満タン）のときだけ発動可能
        if (gaugeManager != null && gaugeManager.IsFull)
        {
            gaugeManager.ConsumeFullGauge();

            // 5つ目のスキル（超必殺技）のデータを使用
            AttackPattern p = attackPatterns[4];
            if (p == null) return;

            Debug.Log("<color=red>Ultimate Skill: Knife Illusion!</color>");

            // 全方位にナイフをバラまく例
            foreach (ShotData data in p.multiShotData)
            {
                // インデックス4はDボタン用のパラメータ
                ExecuteShot(data, 0, 4);
            }
        }
    }
    // ★追加：すべてのリキャストを即座に完了させるメソッド
    public void ResetAllRecasts()
    {
        for (int i = 0; i < 4; i++)
        {
            recastTimers[i] = 0f; // リキャストをゼロにする
        }
    }
    private void ExecuteDodge(int i)
    {
        var pattern = attackPatterns[i];
        dodgeActiveTimer = pattern.dodgeDuration;
        currentDodgeSpeedMultiplier = pattern.dodgeSpeedMultiplier;

        DodgerAgent agent = GetComponent<DodgerAgent>();
        if (agent != null)
        {
            // ★変更：無敵設定だけでなく、回避アクション全体を開始させる
            agent.StartDodgeAction(pattern.dodgeDuration);
        }
        if (gaugeManager != null)
        {
            gaugeManager.IncreaseGauge(pattern.specialGaugeGain);
        }
        recastTimers[i] = pattern.recastTime;
    }
    public float GetCurrentSpeedMultiplier()
    {
        float multiplier = 1f;
        // 回避による倍率を乗算する
        multiplier *= currentDodgeSpeedMultiplier;

        for (int i = 0; i < 4; i++)
        {
            if ((isFiringBurst[i] || isCharging[i]) && attackPatterns[i] != null)
                multiplier = Mathf.Min(multiplier, attackPatterns[i].firingSpeedMultiplier);
        }
        return multiplier;
    }
    private void StartBurst(int index)
    {
        isFiringBurst[index] = true;
        burstRemain[index] = attackPatterns[index].burstCount;
        Fire(index);
    }

    // 195行目付近
    private void Fire(int index)
    {
        AttackPattern p = attackPatterns[index]; // 変数 p を定義
        if (p == null) return;
        // ★追加：スキル使用時にゲージを増やす
        // ★修正：スロットに応じた増加量を加算
        if (gaugeManager != null)
        {
            gaugeManager.IncreaseGauge(p.specialGaugeGain);
        }
        if (p.skillType == SkillType.Normal)
        {
            int currentStep = p.burstCount - burstRemain[index];
            foreach (ShotData data in p.multiShotData)
            {
                if (data != null)
                {
                    // ★修正：チャージショットの場合は固定角度を渡す
                    float? angleOverride = (p.fireType == FireType.OnRelease) ? fixedChargeAngles[index] : (float?)null;
                    ExecuteShot(data, currentStep, index, null, null, angleOverride);
                }
            }
        }
        else if (p.skillType == SkillType.MagicCircle)
        {
            // ★ 修正：ここが抜けていました
            SpawnMagicCircleBurst(index, isLaunchedBurst[index]);
        }
        else if (p.skillType == SkillType.RemoteBarrage)
        {
            if (p.useMovingFire) SpawnMovingBarrageBurst(index);
            else SpawnRemoteBarrageBurst(index);
        }
        else if (p.skillType == SkillType.Assault) // 突進オブジェクトの処理
        {
            if (p.assaultPrefab != null)
            {
                GameObject assaultObj = Instantiate(p.assaultPrefab, transform.position, Quaternion.identity);
                AssaultObject script = assaultObj.GetComponent<AssaultObject>();
                if (script != null) script.Initialize(this, myTeam, p.assaultSpeed, p.assaultDuration, p.damage);
            }
        }

        burstRemain[index]--;
        if (burstRemain[index] > 0) burstIntervalTimers[index] = p.burstInterval;
        else StopBurstAndStartRecast(index);
    
    }
    public void CancelAllSkills()
    {
        for (int i = 0; i < 4; i++)
        {
            // 各種フラグのリセット
            isCharging[i] = false;       // チャージ中断
            isFiringBurst[i] = false;    // 連射中断
            isInputHeld[i] = false;      // 入力保持解除
            burstRemain[i] = 0;          // 残り弾数クリア

            // 保持している魔方陣（シールド移動中など）があれば消去
            if (activeCircles[i] != null)
            {
                Destroy(activeCircles[i].gameObject);
                activeCircles[i] = null;
            }
        }
    }
    private void SpawnMovingBarrageBurst(int i)
    {
        // 現在のスキルデータを取得
        AttackPattern p = attackPatterns[i];

        // 目的地設定
        Vector3 targetPos = (target != null) ? target.position : transform.position + transform.right * 10f;
        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
        circleObj.tag = "Enemy_Bullet"; // ★タグを追加
        MagicCircle script = circleObj.GetComponent<MagicCircle>();

        if (script != null)
        {
            Color pColor = (characterProfile != null) ? characterProfile.personalColor : Color.white;
            script.Initialize(pColor, myTeam);

            // ★修正ポイント：AttackPattern の設定値を秒数に変換して渡す
            float interval = p.movingFireIntervalFrames / 60f;

            // LaunchMovingBarrage に設定値を流し込む
            script.LaunchMovingBarrage(
                targetPos,
                p.multiShotData,
                this,
                myTeam,
                i,
                p.movingFireSpeed,    // ★ インスペクターの速度を適用
                interval,             // ★ インスペクターの間隔を適用
                p.movingFireDuration  // ★ インスペクターの寿命を適用
            );
        }
    }
    private void SpawnMagicCircleBurst(int i, bool isLaunched)
    {
        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
        // ★追加：AIが認識できるようにタグを付ける
        circleObj.tag = "Enemy_Bullet";

        MagicCircle script = circleObj.GetComponent<MagicCircle>();
        if (script == null) return;
        Color pColor = (characterProfile != null) ? characterProfile.personalColor : Color.white;
        script.Initialize(pColor, myTeam);

        if (isLaunched)
        {
            // 移動・射出モード（水平加速）
            float directionX = (target != null && target.position.x > transform.position.x) ? 1f : ((transform.right.x >= 0) ? 1f : -1f);
            Rigidbody2D rb = circleObj.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = new Vector2(directionX * 10f, 0f);
            script.Activate(2.0f, 4.0f);
        }
        else
        {
            // 自機周囲モード
            circleObj.transform.SetParent(transform);
            script.Activate(2.5f, 4.0f);
        }
    }

    // BulletSpawner.cs 

    private void SpawnRemoteBarrageBurst(int i)
    {
        Vector3 targetPos;

        if (target != null)
        {
            // ★ 追加：フラグがONなら Xは敵、Yは最下段にする
            if (attackPatterns[i].targetBottomY)
            {
                // bottomY はすでにクラス内で -5.5f と定義されています
                targetPos = new Vector3(target.position.x, bottomY, 0);
            }
            else
            {
                // 従来の「敵付近に飛ばす」処理
                float jitter = 1.5f;
                Vector3 randomJitter = new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter), 0);
                targetPos = target.position + randomJitter;
            }
        }
        else
        {
            // ターゲットがいない場合のデフォルト位置
            targetPos = transform.position + transform.right * 7f;
        }

        // 魔方陣の生成と飛ばす処理はそのまま維持
        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
        circleObj.tag = "Enemy_Bullet"; // ★タグを追加
        MagicCircle script = circleObj.GetComponent<MagicCircle>();
        if (script != null)
        {
            Color pColor = (characterProfile != null) ? characterProfile.personalColor : Color.white;
            script.Initialize(pColor, myTeam);

            // ★追加：射出型（攻撃用）は弾消しをOFFにする
            script.SetBulletClearing(false);

            script.LaunchToTarget(targetPos, attackPatterns[i].multiShotData, this, myTeam);
        }
    }

    private void StopBurstAndStartRecast(int index)
    {
        isFiringBurst[index] = false;
        isCharging[index] = false;
        burstRemain[index] = 0;
        recastTimers[index] = attackPatterns[index].recastTime;
    }

    public void ExecuteShot(ShotData data, int step, int skillIndex, Vector3? overrideOrigin = null, TeamSide? overrideTeam = null, float? overrideBaseAngle = null)
    {
        if (data == null || data.bulletType == null) return;

        Vector3 origin = overrideOrigin ?? transform.position;
        TeamSide team = overrideTeam ?? myTeam;
        switch (data.pattern)
        {
            case ShotData.PatternType.Single:
                ShootSingle(data, origin, team);
                break;
            case ShotData.PatternType.NWay:
                ShootExpandingNWay(data, step, currentFanAngles[skillIndex], origin, team, overrideBaseAngle);
                break;
            case ShotData.PatternType.AllDirections:
                ShootAllDirections(data, origin, team, step); // stepを追加
                break;
            case ShotData.PatternType.RandomGap:
                ShootRandomGapShot(data, origin, team);
                break;
        }
    }

    // 1. NWay弾の修正
    private void ShootExpandingNWay(ShotData data, int step, float overridenSpread, Vector3 origin, TeamSide team, float? overrideBaseAngle)
    {
        // 1. 回転オフセットの計算
        float rotationOffset = data.rotationPerStep * step;
        float baseAngle = (overrideBaseAngle ?? CalculateAngle(data, origin)) + rotationOffset;

        // 2. NWay拡張設定（連射ステップごとの弾数と範囲の増加）の計算
        int actualCount = data.nWayCount + (data.nWayExpand != null ? data.nWayExpand.countAdd * step : 0);
        float actualSpread = overridenSpread + (data.nWayExpand != null ? data.nWayExpand.spreadAdd * step : 0);

        // 3. 配置基準（開始角度と弾ごとの角度ステップ）の計算
        float startAngle = baseAngle - (actualSpread / 2f);
        float angleStep = (actualCount > 1) ? actualSpread / (actualCount - 1) : 0;

        // 4. 速度レイヤーごとのループ
        for (int s = 0; s < data.speedCount; s++)
        {
            // レイヤーごとの基本速度を線形補間で算出
            float baseSpeed = (data.speedCount > 1)
                ? Mathf.Lerp(data.speedData.default_, data.speedMax, (float)s / (data.speedCount - 1))
                : data.speedData.default_;

            // 5. 個々の弾を生成するループ
            for (int i = 0; i < actualCount; i++)
            {
                // ★ 発射時のゆらぎ（Jitter）を適用
                float angleJitter = Random.Range(-data.launchAngleJitter, data.launchAngleJitter);
                float speedJitter = Random.Range(-data.launchSpeedJitter, data.launchSpeedJitter);

                // 最終的な角度と速度の決定
                float currentAngle = startAngle + (angleStep * i) + angleJitter;
                float finalSpeed = baseSpeed + speedJitter;

                // 生成座標の計算
                Vector3 spawnPos = GetOffsetPosition(currentAngle, data.spawnRadius, origin);

                // プールから弾を発射
                SpawnFromPool(
                    data.bulletType,
                    spawnPos,
                    currentAngle,
                    data.speedData,    // 加速度に使用
                    data.angleAccData, // 角速度に使用
                    data.launchDelay,
                    finalSpeed,
                    team
                );
            }
        }
    }

    // 2. Single弾の修正
    private void ShootSingle(ShotData data, Vector3 origin, TeamSide team)
    {
        float baseAngle = CalculateAngle(data, origin);

        for (int s = 0; s < data.speedCount; s++)
        {
            float baseSpeed = (data.speedCount > 1)
                ? Mathf.Lerp(data.speedData.default_, data.speedMax, (float)s / (data.speedCount - 1))
                : data.speedData.default_;

            // ★ 新設した Jitter 変数を使用
            float finalAngle = baseAngle + Random.Range(-data.launchAngleJitter, data.launchAngleJitter);
            float finalSpeed = baseSpeed + Random.Range(-data.launchSpeedJitter, data.launchSpeedJitter);

            Vector3 spawnPos = GetOffsetPosition(finalAngle, data.spawnRadius, origin);
            SpawnFromPool(data.bulletType, spawnPos, finalAngle, data.speedData, data.angleAccData, data.launchDelay, finalSpeed, team);
        }
    }

    private void ShootAllDirections(ShotData data, Vector3 origin, TeamSide team, int step)
    {
        // 1. 基本角度の計算（ターゲットへの角度 + 連射ステップに応じた回転）
        float rotationOffset = data.rotationPerStep * step;
        float baseAngle = CalculateAngle(data, origin) + rotationOffset; //

        // 2. 弾ごとの間隔を計算
        float stepAngle = 360f / data.RoundCount; //

        // 3. 偶数個のオフセット処理（data.useEvenOffset で切り替え可能にする）
        float evenOffset = (data.useEvenOffset && data.RoundCount % 2 == 0) ? stepAngle / 2f : 0f; //

        for (int s = 0; s < data.speedCount; s++)
        {
            float baseSpeed = (data.speedCount > 1)
                ? Mathf.Lerp(data.speedData.default_, data.speedMax, (float)s / (data.speedCount - 1))
                : data.speedData.default_; //

            for (int i = 0; i < data.RoundCount; i++)
            {
                // 回転とオフセットを加味した最終角度
                float finalAngle = baseAngle + (stepAngle * i) + evenOffset + Random.Range(-data.launchAngleJitter, data.launchAngleJitter);
                float finalSpeed = baseSpeed + Random.Range(-data.launchSpeedJitter, data.launchSpeedJitter);

                Vector3 spawnPos = GetOffsetPosition(finalAngle, data.spawnRadius, origin); //
                SpawnFromPool(data.bulletType, spawnPos, finalAngle, data.speedData, data.angleAccData, data.launchDelay, finalSpeed, team); //
            }
        }
    }
    private void ShootRandomGapShot(ShotData data, Vector3 origin, TeamSide team)
    {
        // 1. プレイヤー（ターゲット）への正確な角度を取得
        float targetAngle = CalculateAngle(data, origin);

        // 2. 指定された弾数（RoundCount）分、ランダムな角度を生成して撃つ
        for (int s = 0; s < data.speedCount; s++)
        {
            float baseSpeed = (data.speedCount > 1)
                ? Mathf.Lerp(data.speedData.default_, data.speedMax, (float)s / (data.speedCount - 1))
                : data.speedData.default_;

            for (int i = 0; i < data.RoundCount; i++)
            {
                // ★ 0〜360度の間でランダムな角度を決定
                float randomAngle = Random.Range(0f, 360f);

                // ★ 自機外しの判定：ターゲット角度 ± (gapWidth/2) の範囲なら発射をスキップ
                // Mathf.DeltaAngle を使うと、360度を跨ぐ計算（350度と10度の差など）も正しく行えます
                if (Mathf.Abs(Mathf.DeltaAngle(randomAngle, targetAngle)) < data.gapWidth / 2f)
                {
                    // 安全圏に入った場合は、その弾をキャンセルするか、
                    // あるいは i-- して再試行（弾数を維持したい場合）
                    continue;
                }

                // 個別のゆらぎ（Jitter）も適用可能
                float finalAngle = randomAngle + Random.Range(-data.launchAngleJitter, data.launchAngleJitter);
                float finalSpeed = baseSpeed + Random.Range(-data.launchSpeedJitter, data.launchSpeedJitter);

                Vector3 spawnPos = GetOffsetPosition(finalAngle, data.spawnRadius, origin);
                SpawnFromPool(data.bulletType, spawnPos, finalAngle, data.speedData, data.angleAccData, data.launchDelay, finalSpeed, team);
            }
        }
    }

    private void SpawnFromPool(BulletData bData, Vector3 spawnPos, float angle, spanData sData, spanData aData, int delayFrames, float overrideSpeed, TeamSide team)
    {
        if (BulletPool.Instance == null) return;

        // フレーム数を秒数に変換（60fps想定）
        float delayTime = delayFrames / 60f;

        if (delayFrames <= 0)
        {
            ExecuteActualSpawn(bData, spawnPos, angle, sData, aData, overrideSpeed, team);
        }
        else
        {
            // ★ 修正：データ側で「予兆を使う」設定になっており、プレハブがある場合のみ実行
            if (bData.useLaunchDelayEffect && delayEffectPrefab != null)
            {
                GameObject effectObj = Instantiate(delayEffectPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
                LaunchDelayEffect effect = effectObj.GetComponent<LaunchDelayEffect>();

                if (effect != null)
                {
                    // ★ 修正：bData.delayColor をインデックスとして使用し、適切な色のスプライトを渡す
                    int colorIndex = (int)bData.delayColor;
                    Sprite delaySprite = (colorIndex < delaySprites.Length) ? delaySprites[colorIndex] : bData.bulletSprite;

                    // 槍のスケールに合わせてエフェクトの初期サイズも調整（bData.localScaleを利用）
                    float effectScale = bData.localScale.x * 2.0f;
                    effect.Setup(delaySprite, delayFrames, 1000, effectScale);
                }
            }

            StartCoroutine(DelayedSpawnRoutine(bData, spawnPos, angle, sData, aData, delayTime, overrideSpeed, team));
        }
    }

    private IEnumerator DelayedSpawnRoutine(BulletData bData, Vector3 spawnPos, float angle, spanData sData, spanData aData, float delayTime, float overrideSpeed, TeamSide team)
    {
        // ★ yield return null のループをやめ、WaitForSeconds を使う
        // これにより Time.timeScale (倍速設定) が正しく反映されます
        yield return new WaitForSeconds(delayTime);

        ExecuteActualSpawn(bData, spawnPos, angle, sData, aData, overrideSpeed, team);
    }
    private void ExecuteActualSpawn(BulletData bData, Vector3 spawnPos, float angle, spanData sData, spanData aData, float overrideSpeed, TeamSide team)
    {
        GameObject b = BulletPool.Instance.Get();
        b.transform.position = spawnPos;

        // ★重要: 生成された弾にタグを強制的に設定する
        // これがないと GameObject.FindGameObjectsWithTag("Enemy_Bullet") で見つけられません
        b.tag = "Enemy_Bullet";

        b.layer = LayerMask.NameToLayer(team.ToString() + "_Bullet");

        var bullet = b.GetComponent<EnemyBullet>();
        if (bullet != null)
        {
            int order = GetNextOrder(bData.sizeCategory);
            float speedLimit = Mathf.Max(sData.max_, overrideSpeed);
            bullet.Setup(bData, spawnPos, overrideSpeed, sData.accuracy_, speedLimit, angle, aData.accuracy_, aData.max_, order, team, this);
        }
    }

    private int GetNextOrder(BulletData.SizeCategory category)
    {
        switch (category)
        {
            case BulletData.SizeCategory.Large:
                largeCounter = (largeCounter >= 5999) ? 1000 : largeCounter + 1;
                return largeCounter;
            case BulletData.SizeCategory.Middle:
                middleCounter = (middleCounter >= 10999) ? 6000 : middleCounter + 1;
                return middleCounter;
            case BulletData.SizeCategory.Small:
                smallCounter = (smallCounter >= 15999) ? 11000 : smallCounter + 1;
                return smallCounter;
            default: return 0;
        }
    }
    // 角度計算：起点(origin)からプレイヤーへの向きを出す
    private float CalculateAngle(ShotData data, Vector3 origin)
    {
        switch (data.angleType)
        {
            case ShotData.AngleType.AimAtPlayer:
                if (target != null)
                {
                    // ★ transform.position ではなく、引数の origin を使う
                    Vector2 dir = target.position - origin;
                    return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                }
                return data.fixedAngle;
            case ShotData.AngleType.Random:
                return Random.Range(1f, 360f);
            default:
                return data.fixedAngle;
        }
    }

    // 座標計算：起点(origin)から指定の半径分オフセットさせる
    private Vector3 GetOffsetPosition(float angle, float radius, Vector3 origin)
    {
        float rad = angle * Mathf.Deg2Rad;
        // ★ ここも transform.position ではなく origin を使う
        return origin + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * radius;
    }
    // --- ★ 復元した補助メソッド ---


    public float GetRecastProgress(int index)
    {
        if (index < 0 || index >= 4 || attackPatterns[index] == null || attackPatterns[index].recastTime <= 0) return 0f;
        return Mathf.Clamp01(recastTimers[index] / attackPatterns[index].recastTime);
    }

    public float GetRemainingRecastTime(int index)
    {
        if (index < 0 || index >= 4) return 0;
        return Mathf.Max(0, recastTimers[index]);
    }

    private void FindTarget()
    {
        string enemyTag = (myTeam == TeamSide.Player1) ? "Player2" : "Player1";
        GameObject targetObj = GameObject.FindGameObjectWithTag(enemyTag);
        if (targetObj != null) target = targetObj.transform;
    }
}