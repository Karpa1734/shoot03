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

    [Header("Magic Circle Settings")]
    public GameObject magicCirclePrefab;
    public float longPressThreshold = 0.3f; // 長押しと判定する秒数
    private float[] holdTimers = new float[4]; // 各スキルの押し時間を計測

    private bool[] isLaunchedBurst = new bool[4]; // 現在のバーストが射出モードかどうかを保持

    private AttackPattern[] attackPatterns = new AttackPattern[4];
    private float[] recastTimers = new float[4];
    private Transform target;
    private int[] burstRemain = new int[4];
    private float[] burstIntervalTimers = new float[4];
    private bool[] isFiringBurst = new bool[4];
    private bool[] isInputHeld = new bool[4];

    private bool[] isCharging = new bool[4];
    private float[] currentFanAngles = new float[4];

    private static int largeCounter = 1000;
    private static int middleCounter = 6000;
    private static int smallCounter = 11000;

    private MagicCircle[] activeCircles = new MagicCircle[4];
    private float[] moveTimers = new float[4]; // ★ 移動開始からの経過時間を計測用
    // BulletSpawner.cs 内に追加
    [SerializeField] private SkillUIManager uiManager;
    public static BulletSpawner Instance { get; private set; }
    public AttackPattern[] GetAttackPatterns()
    {
        return attackPatterns;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (characterProfile != null)
        {
            attackPatterns[0] = characterProfile.shotZ;
            attackPatterns[1] = characterProfile.shotX;
            attackPatterns[2] = characterProfile.shotC;
            attackPatterns[3] = characterProfile.shotV;
        }

        // 64行目付近
        for (int i = 0; i < 4; i++)
        {
            // multiShotData[0] を参照するように変更
            if (attackPatterns[i] != null && attackPatterns[i].multiShotData != null && attackPatterns[i].multiShotData.Length > 0)
                currentFanAngles[i] = attackPatterns[i].multiShotData[0].nWaySpread;
        }
        if (uiManager != null)
        {
            uiManager.SetupIcons(attackPatterns); // スキル配列を渡してアイコンをセット
        }
        gameObject.layer = LayerMask.NameToLayer(myTeam.ToString());
        FindTarget();
    }

    private void Update()
    {
        bool anyCharging = false;
        int chargingIndex = -1;

        for (int i = 0; i < 4; i++)
        {
            if (attackPatterns[i] == null) continue;

            if (recastTimers[i] > 0) recastTimers[i] -= Time.deltaTime;

            UpdateFanAngleValues(i);

            if (isCharging[i])
            {
                anyCharging = true;
                chargingIndex = i;
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

        if (anyCharging && fanVisualizer != null)
        {
            // 配列の 0 番目のデータが存在するかチェックして実行
            if (attackPatterns[chargingIndex].multiShotData != null && attackPatterns[chargingIndex].multiShotData.Length > 0)
            {
                // shotData を multiShotData[0] に変更
                float baseAngle = CalculateAngle(attackPatterns[chargingIndex].multiShotData[0], transform.position);

                // 描画処理を実行
                fanVisualizer.DrawFan(baseAngle, currentFanAngles[chargingIndex], previewRadius);
            }
        }
        else if (fanVisualizer != null)
        {
            fanVisualizer.Hide(); //
        }
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
        for (int i = 0; i < 4; i++)
        {
            if (attackPatterns[i] == null) continue;

            bool currentlyPressed = (attackAction == i + 1);
            bool wasPressed = isInputHeld[i];

            // --- A. 魔方陣（自機/移動）スキルの場合 ---
            if (attackPatterns[i].skillType == SkillType.MagicCircle)
            {
                if (currentlyPressed) holdTimers[i] += Time.deltaTime;

                if (!currentlyPressed && wasPressed && recastTimers[i] <= 0 && !isFiringBurst[i])
                {
                    // 離した瞬間にモードを決定し、バースト開始
                    isLaunchedBurst[i] = (holdTimers[i] >= longPressThreshold);
                    StartBurst(i);
                    holdTimers[i] = 0;
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
            // --- B. 通常スキルの場合（Zキーなどの修正版） ---
            else
            {
                if (currentlyPressed)
                {
                    // ★ 修正：発射中 (!isFiringBurst) でないことも条件に加え、リキャスト消失を防止
                    bool triggerInput = !wasPressed || (attackPatterns[i].isAutoRepeat && recastTimers[i] <= 0 && !isFiringBurst[i]);

                    if (triggerInput && recastTimers[i] <= 0 && !isFiringBurst[i] && !isCharging[i])
                    {
                        if (attackPatterns[i].fireType == FireType.Instant) StartBurst(i);
                        else if (attackPatterns[i].fireType == FireType.OnRelease) isCharging[i] = true;
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
    }
    // 旧 DeployMagicCircle と DeployAfterDelay は削除し、以下のロジックを UpdateInputState で呼び出します

    // 1. 生成処理（ボタンを押した瞬間）
    private void CreateMagicCircle(int i, Color pColor)
    {
        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
        activeCircles[i] = circleObj.GetComponent<MagicCircle>();
        if (activeCircles[i] != null)
        {
            // ★ チーム情報（myTeam）を渡して初期化
            activeCircles[i].Initialize(pColor, myTeam);
        }
    }

    // 2. 更新処理（ボタンを長押ししている間）
    // BulletSpawner.cs の UpdateMagicCircle メソッドを以下に差し替え
    private void UpdateMagicCircle(int i)
    {
        if (activeCircles[i] == null) return;

        // ボタンを押している間は、まず「半分」を目指して展開
        activeCircles[i].SetTargetScale(1.0f);

        if (holdTimers[i] > longPressThreshold)
        {
            isCharging[i] = true;

            // ★ 追加：スケールが 0.6 を超えたら（半分展開がほぼ完了したら）動き出す
            if (activeCircles[i].transform.localScale.x > 0.6f)
            {
                moveTimers[i] += Time.deltaTime; // 移動時間をカウントアップ

                Rigidbody2D rb = activeCircles[i].GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    // 向きの決定（X軸のみ）
                    float directionX = (target != null && target.position.x > transform.position.x) ? 1f : ((transform.right.x >= 0) ? 1f : -1f);

                    // ★ 加速ロジック
                    float startSpeed = 1.0f;     // 初速
                    float acceleration = 25.0f;  // 加速度（毎秒25増加）
                    float maxSpeed = 15.0f;      // 最高速度
                    float currentSpeed = Mathf.Min(startSpeed + (acceleration * moveTimers[i]), maxSpeed);

                    rb.linearVelocity = new Vector2(directionX * currentSpeed, 0f); // Y軸は 0 固定
                }
            }
        }
    }

    // 3. 確定処理（ボタンを離した瞬間）
    private void FinalizeMagicCircle(int i)
    {
        if (activeCircles[i] == null) return;

        Rigidbody2D rb = activeCircles[i].GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero; // 停止

        if (holdTimers[i] < longPressThreshold)
        {
            // 【単押し】自機周りに引き戻して展開
            activeCircles[i].transform.SetParent(null); // 親子付け解除
            activeCircles[i].Activate(2.5f, 4.0f); // 半径3.5、2秒持続
        }
        else
        {
            // 【長押し】離した場所で最大展開
            activeCircles[i].transform.SetParent(null); // 親子付け解除
            activeCircles[i].Activate(2.0f, 4.0f); // 半径4.5、3秒持続
        }
        moveTimers[i] = 0; // ★ リセット
        recastTimers[i] = attackPatterns[i].recastTime; // リキャスト開始
        activeCircles[i] = null; // 参照をクリア
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
        if (attackPatterns[index] == null) return;

        // スキルタイプによる分岐
        if (attackPatterns[index].skillType == SkillType.Normal)
        {
            // 従来の弾幕生成
            int currentStep = attackPatterns[index].burstCount - burstRemain[index];
            foreach (ShotData data in attackPatterns[index].multiShotData)
            {
                if (data != null) ExecuteShot(data, currentStep, index);
            }
        }
        else if (attackPatterns[index].skillType == SkillType.MagicCircle)
        {
            // 魔方陣（自機/移動）のバースト生成
            SpawnMagicCircleBurst(index, isLaunchedBurst[index]);
        }
        else if (attackPatterns[index].skillType == SkillType.RemoteBarrage)
        {
            // 遠隔設置魔方陣のバースト生成
            SpawnRemoteBarrageBurst(index);
        }

        burstRemain[index]--;
        if (burstRemain[index] > 0) burstIntervalTimers[index] = attackPatterns[index].burstInterval;
        else StopBurstAndStartRecast(index);
    }

    private void SpawnMagicCircleBurst(int i, bool isLaunched)
    {
        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
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

    private void SpawnRemoteBarrageBurst(int i)
    {
        float jitter = 1.5f;
        Vector3 randomJitter = new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter), 0);
        Vector3 targetPos = (target != null) ? target.position + randomJitter : transform.position + transform.right * 7f;

        GameObject circleObj = Instantiate(magicCirclePrefab, transform.position, Quaternion.identity);
        MagicCircle script = circleObj.GetComponent<MagicCircle>();
        if (script != null)
        {
            Color pColor = (characterProfile != null) ? characterProfile.personalColor : Color.white;
            script.Initialize(pColor, myTeam);
            // 敵機方向へ飛ばして弾幕展開
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

    public void ExecuteShot(ShotData data, int step, int skillIndex, Vector3? overrideOrigin = null, TeamSide? overrideTeam = null)
    {
        if (data == null || data.bulletType == null) return;

        Vector3 origin = overrideOrigin ?? transform.position;
        TeamSide team = overrideTeam ?? myTeam;
        switch (data.pattern)
        {
            case ShotData.PatternType.Single:
                ShootSingle(data, origin, team); // stepを追加
                break;
            case ShotData.PatternType.NWay:
                ShootExpandingNWay(data, step, currentFanAngles[skillIndex], origin, team);
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
    private void ShootExpandingNWay(ShotData data, int step, float overridenSpread, Vector3 origin, TeamSide team)
    {
        // 1. 回転オフセットの計算
        float rotationOffset = data.rotationPerStep * step;
        float baseAngle = CalculateAngle(data, origin) + rotationOffset;

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
    private void ExecuteActualSpawn(BulletData bData, Vector3 spawnPos, float angle, spanData sData, spanData aData, float overrideSpeed, TeamSide team) // 引数に team を追加
    {
        GameObject b = BulletPool.Instance.Get();
        b.transform.position = spawnPos;

        // ★ 渡されたチーム情報に基づいてレイヤーを設定する
        b.layer = LayerMask.NameToLayer(team.ToString() + "_Bullet");

        var bullet = b.GetComponent<EnemyBullet>();
        if (bullet != null)
        {
            int order = GetNextOrder(bData.sizeCategory);
            float speedLimit = Mathf.Max(sData.max_, overrideSpeed);
            // Setup時にチーム情報を渡す
            bullet.Setup(bData, spawnPos, overrideSpeed, sData.accuracy_, speedLimit, angle, aData.accuracy_, aData.max_, order, team,this);
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

    public float GetCurrentSpeedMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < 4; i++)
        {
            // 発射中またはチャージ中のスキルがあれば、その倍率を適用（最も低いものを優先）
            if ((isFiringBurst[i] || isCharging[i]) && attackPatterns[i] != null)
                multiplier = Mathf.Min(multiplier, attackPatterns[i].firingSpeedMultiplier);
        }
        return multiplier;
    }

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