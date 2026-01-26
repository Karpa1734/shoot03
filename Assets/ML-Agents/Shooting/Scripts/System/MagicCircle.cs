using UnityEngine;
using System.Collections;

public class MagicCircle : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private float expandSpeed = 8.0f;
    [SerializeField] private float shrinkSpeed = 5.0f;
    [SerializeField] private float rotationSpeed = 300f;
    
    // 状態管理
    private Vector3 targetScale = Vector3.zero;
    private bool isExpiring = false;
    private int opponentBulletLayer;
    
    // 移動・射撃パラメータ
    private Vector3? destination;
    private ShotData[] barrageData;
    private BulletSpawner spawnerRef;
    private BulletSpawner.TeamSide mySide;
    private int mySkillIndex;

    // モード切り替えフラグ
    private bool isPeriodicFiring = false; // 移動連射モードか
    private bool isManualMoving = false;    // 手動前進（シールド）モードか
    private bool canClearBullets = true;   // 弾消し能力の有無

    // タイマー・速度
    private float moveSpeed;
    private float lifeTimer;
    private float fireInterval;
    private float fireTimer;
    private float currentManualSpeed = 5f;
    private int moveDir = 1;
    private float totalDuration; // ★ 追加：開始時のトータル時間を保持
    // ★追加：半径（Radius）も取得できるようにしておくと、AIが範囲を認識しやすくなります
    private float currentRadius;
    public float Radius => currentRadius;
    // 内部保持用の変数
    private BulletSpawner.TeamSide myTeam;

    // ★追加：外部（DodgerAgent等）からチーム情報を取得するための公開プロパティ
    public BulletSpawner.TeamSide Team => myTeam;
    public void Initialize(Color color, BulletSpawner.TeamSide team)
    {
        this.myTeam = team; // ★送られてきたチーム情報を保存
        var sprite = GetComponent<SpriteRenderer>();
        if (sprite != null) sprite.color = color;
        transform.localScale = Vector3.zero;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = color;

        string opponentLayerName = (myTeam == BulletSpawner.TeamSide.Player1) ? "Player2_Bullet" : "Player1_Bullet";
        opponentBulletLayer = LayerMask.NameToLayer(opponentLayerName);
    }

    /// <summary>
    /// 移動連射モード：自機と敵を3:1に分ける地点を常に追従しながら連射する
    /// </summary>
    public void LaunchMovingBarrage(Vector3 dest, ShotData[] data, BulletSpawner spawner, BulletSpawner.TeamSide side, int skillIndex, float speed, float interval, float duration)
    {
        destination = dest;
        barrageData = data;
        spawnerRef = spawner;
        mySide = side;
        mySkillIndex = skillIndex;

        this.moveSpeed = speed;
        this.fireInterval = interval;
        this.lifeTimer = duration;
        this.fireTimer = 0f;
        this.totalDuration = duration; // ★ トータル時間を記憶
        this.isPeriodicFiring = true; 

        SetTargetScale(0.8f);
        SetBulletClearing(false); // 攻撃用は弾を消さない設定
    }

    public void LaunchToTarget(Vector3 dest, ShotData[] data, BulletSpawner spawner, BulletSpawner.TeamSide side)
    {
        destination = dest;
        barrageData = data;
        spawnerRef = spawner;
        mySide = side;
        isPeriodicFiring = false;
        
        SetTargetScale(0.5f);
        SetBulletClearing(false);
    }

    public void StartManualMove(int direction, float initialScale)
    {
        isManualMoving = true;
        moveDir = direction;
        currentManualSpeed = 5f;
        SetTargetScale(initialScale);
        SetBulletClearing(true);
    }

    public void SetBulletClearing(bool enable) => canClearBullets = enable;
    public void SetTargetScale(float radius) => targetScale = new Vector3(radius, radius, 1);

    private void Update()
    {
        // 常に自転
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);

        // --- 1. 移動・寿命・射撃ロジックの実行制御 ---
        // 各モードのメソッドを呼び出すことでUpdate内の重複を避ける
        if (isPeriodicFiring)
        {
            HandlePeriodicFiringMode();
        }
        else if (isManualMoving)
        {
            HandleManualMoveMode();
        }
        else if (destination.HasValue)
        {
            HandleNormalRemoteMode();
        }

        // --- 2. サイズ変更と消滅判定 ---
        float s = isExpiring ? shrinkSpeed : expandSpeed;
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * s);

        if (isExpiring && transform.localScale.x < 0.05f)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 移動連射モードの統合ロジック
    /// </summary>
    private void HandlePeriodicFiringMode()
    {
        // 1. 寿命管理
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0)
        {
            isPeriodicFiring = false;
            isExpiring = true;
            targetScale = Vector3.zero;
            destination = null;
            return;
        }

        // 2. 動的な速度調整とホーミング移動
        if (spawnerRef != null && spawnerRef.Target != null)
        {
            Vector3 playerPos = spawnerRef.transform.position;
            Vector3 enemyPos = spawnerRef.Target.position;

            // 目標地点（敵寄り75%）を計算
            Vector3 targetPoint = Vector3.Lerp(playerPos, enemyPos, 0.7f);

            // ★ 今回の重要修正：速度の動的計算
            // 現在地から目標地点までの距離を測る
            float distance = Vector3.Distance(transform.position, targetPoint);

            // 「距離 ÷ 残り時間」で、終了時にちょうど到着する速度を出す
            // lifeTimer が極端に小さい時のゼロ除算を防ぐため、Mathf.Max を使用
            float dynamicSpeed = distance / Mathf.Max(lifeTimer, 0.01f);

            // 計算した速度で移動
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPoint,
                dynamicSpeed * Time.deltaTime
            );
        }

        // 3. 射撃ロジック
        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0)
        {
            fireTimer = fireInterval;
            if (spawnerRef != null && barrageData != null)
            {
                foreach (var data in barrageData)
                {
                    spawnerRef.ExecuteShot(data, 0, mySkillIndex, transform.position, mySide);
                }
            }
        }
    }

    private void HandleManualMoveMode()
    {
        transform.position += new Vector3(moveDir * currentManualSpeed * Time.deltaTime, 0, 0);
    }

    private void HandleNormalRemoteMode()
    {
        float step = 18f * Time.deltaTime; 
        transform.position = Vector3.MoveTowards(transform.position, destination.Value, step);

        if (Vector3.Distance(transform.position, destination.Value) < 0.01f)
        {
            destination = null;
            Activate(0.5f, 2.0f);
            StartCoroutine(ExecuteBarrageRoutine());
        }
    }

    private IEnumerator ExecuteBarrageRoutine()
    {
        yield return new WaitForSeconds(0.6f);
        if (spawnerRef != null && barrageData != null)
        {
            foreach (var data in barrageData)
            {
                spawnerRef.ExecuteShot(data, 0, 0, transform.position, mySide);
            }
        }
        isExpiring = true;
        targetScale = Vector3.zero;
    }

    public void Activate(float radius, float duration)
    {
        this.currentRadius = radius; // 半径を記憶
        isExpiring = false;
        SetTargetScale(radius);
        StartCoroutine(ExpireRoutine(duration));
    }

    private IEnumerator ExpireRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        isExpiring = true;
        targetScale = Vector3.zero;
    }

    public void DeployAtCurrentPosition(float radius, float duration)
    {
        isManualMoving = false;
        Activate(radius, duration);
    }

    private void OnTriggerEnter2D(Collider2D therapeutic)
    {
        if (isExpiring || !canClearBullets) return;

        if (therapeutic.gameObject.layer == opponentBulletLayer)
        {
            EnemyBullet bullet = therapeutic.GetComponent<EnemyBullet>();
            if (bullet != null) bullet.Deactivate();
            else BulletPool.Instance.ReturnToPool(therapeutic.gameObject);
        }
    }
}