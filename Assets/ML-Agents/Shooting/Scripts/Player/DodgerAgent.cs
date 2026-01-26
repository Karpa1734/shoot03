using System.Collections;
using System.Linq;
using TMPro;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.UI;

public class DodgerAgent : Agent
{
    [Header("Target Settings")]
    public Transform enemyTransform;
    private DodgerAgent opponentAgent;


    [Header("UI References")]
    public TextMeshProUGUI nameText;
    [SerializeField] private float barSmoothSpeed = 8f;   // メインHPの速度（少し速め）
    [SerializeField] private float orangeSmoothSpeed = 1f; // ★追加：オレンジゲージの速度（ゆっくり）
    [SerializeField] private float orangeDelayTime = 0.6f;  // ★追加：減少が始まるまでの待機時間

    [Header("UI Binding Settings")]
    [SerializeField] private PlayerUIDisplay leftUI;  // インスペクターで左固定のUIを指定
    [SerializeField] private PlayerUIDisplay rightUI; // インスペクターで右固定のUIを指定

    [Header("Visual Settings")]
    public SpriteRenderer visualSprite;
    private Animator animator; // ★ 追加：アニメーターの参照を保持する変数
    [Header("Dodge Visual Settings")]
    [SerializeField] private GameObject ghostPrefab;       // 作成した残像プレハブをセット
    [SerializeField] private float ghostSpawnInterval = 0.05f; // 残像を出す間隔

    [Header("Game Flow Settings")]
    [SerializeField] private TextMeshProUGUI centerNotificationText; // 画面中央の大きなテキスト
    private bool isRoundActive = false; // カウントダウン中かどうかを管理

    [Header("Movement Settings")]
    public float normalSpeed = 5f;
    public float slowSpeed = 2f;
    private Rigidbody2D rb;

    [Header("Observation Settings")]
    public int observeBulletCount = 10;
    public float maxDetectionRadius = 15f;

    [Header("Status Settings")]
    public float stunDuration = 2.0f;
    public float invincibilityDuration = 5.0f;
    private float stunTimer = 0f;
    private float invincibilityTimer = 0f;

    [Header("Life Settings")]
    public float maxHealth = 100f;
    private float currentHealth;
    // ★ targetHpRatio も public にするか、プロパティを作成して外部から読み取れるようにする
    public float targetHpRatio { get; private set; } = 1f;

    [Header("Reward Weights")]
    public float survivalReward = 0.002f;
    public float hitPenalty = -0.5f;
    public float hitOpponentReward = 0.8f;
    public float gameOverPenalty = -1.0f;
    public float clearBonus = 2.0f;

    [Header("Stage Bounds")]
    public float minX = -9f;
    public float maxX = 9f;
    public float minY = -5f;
    public float maxY = 5f;

    [Header("Random Fire Settings")]
    [SerializeField] private bool useRandomFireInTraining = false; // 学習中にランダム射撃を行うか
    private float randomFireTimer = 0f;

    private static bool isSideSwapped = false;
    private static int lastUpdateFrame = -1; // 同じフレーム内での重複判定を防止

    [Header("Team Appearance")]
    [SerializeField] private Color p1Color = new Color(0.2f, 0.5f, 1f); // 青系
    [SerializeField] private Color p2Color = new Color(1f, 0.3f, 0.3f); // 赤系
    private Color normalColor; // 自分のチームの平常色

    [Header("Timer References")]
    [SerializeField] private GameTimerManager timerManager; // 追加：マネージャーの参照

    private BulletSpawner spawner; 
    private float orangeDelayTimer = 0f; // ★追加：現在の待機カウント
    private bool isDodging = false; // 現在回避アクション中かのフラグ
                                    // 無敵中かどうかを外部から判定するためのプロパティ
    public bool IsInvincible => invincibilityTimer > 0;
    // ★ 追加：外部から状態を取得するためのプロパティ
    public bool IsRoundActive => isRoundActive;
    public BulletSpawner.TeamSide MyTeam => (spawner != null) ? spawner.myTeam : BulletSpawner.TeamSide.Player1;
    // 1. 外部から情報を取るためのプロパティを追加
    public float CurrentHealth => currentHealth;
    public string CharacterName => (spawner != null && spawner.characterProfile != null)
        ? spawner.characterProfile.characterName : gameObject.name;


    public override void Initialize()
    {
        rb = GetComponent<Rigidbody2D>();
        spawner = GetComponent<BulletSpawner>();

        // アニメーターの取得
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (spawner != null && spawner.characterProfile != null)
        {
            CharacterData profile = spawner.characterProfile;
            normalColor = profile.personalColor;
            normalSpeed = profile.normalSpeed;
            slowSpeed = profile.slowSpeed;
            maxHealth = profile.maxHealth;
        }

        if (visualSprite == null) visualSprite = GetComponentInChildren<SpriteRenderer>();

        // ★追加：対戦相手を自動的に見つける
        FindOpponent();
        if (opponentAgent != null && spawner != null)
        {
            // Spawnerが持つターゲットを、ここで見つけた相手に上書きする
            spawner.SetTarget(opponentAgent.transform);
        }
    }

    // 相手を見つけるための補助メソッド
    private void FindOpponent()
    {
        // スパナーの設定を利用して、自分とは逆のチームタグを持つオブジェクトを探す
        string enemyTag = (spawner.myTeam == BulletSpawner.TeamSide.Player1) ? "Player2" : "Player1";
        GameObject targetObj = GameObject.FindGameObjectWithTag(enemyTag);
        if (targetObj != null)
        {
            opponentAgent = targetObj.GetComponent<DodgerAgent>();
        }
    }

    public override void OnEpisodeBegin()
    {
        if (rb != null) rb.linearVelocity = Vector2.zero;
        if (BulletPool.Instance != null) BulletPool.Instance.ReturnAllBullets();

        // ★ 修正：ラウンド開始時にタイマーを初期値に戻す
        if (timerManager != null)
        {
            timerManager.ResetTimer();
        }

        // カウントダウン演出開始
        StopAllCoroutines();
        StartCoroutine(RoundStartRoutine());

        currentHealth = maxHealth;
        UpdateHPUI(true);

        if (spawner == null) spawner = GetComponent<BulletSpawner>();
        bool isP1 = (spawner != null && spawner.myTeam == BulletSpawner.TeamSide.Player1);

        // ★ 同期処理：同じフレーム内（エピソード開始時）に一度だけランダム判定を行う
        if (Time.frameCount != lastUpdateFrame)
        {
            isSideSwapped = (Random.value > 0.5f);
            lastUpdateFrame = Time.frameCount;
            if (BulletPool.Instance != null) BulletPool.Instance.ReturnAllBullets();
        }

        // ★ 自機の座標計算
        float offset = 6.0f;
        float startX;
        bool isActuallyOnLeft;

        if (isP1)
        {
            startX = isSideSwapped ? offset : -offset;
            isActuallyOnLeft = !isSideSwapped; // Swappedでなければ左
        }
        else
        {
            startX = isSideSwapped ? -offset : offset;
            isActuallyOnLeft = isSideSwapped; // Swappedなら左
        }

        transform.localPosition = new Vector3(startX, 0f, 0f);
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // ★ 参照先の切り替え実行
        // 自分が左にいるなら左UIに、右にいるなら右UIに自分を紐付ける
        if (isActuallyOnLeft)
        {
            if (leftUI != null) leftUI.Bind(this);
        }
        else
        {
            if (rightUI != null) rightUI.Bind(this);
        }

        if (visualSprite != null) visualSprite.flipY = !isP1; //

        stunTimer = 0f;
        invincibilityTimer = 0f;
    }

    void Update()
    {
        if (visualSprite != null && enemyTransform != null)
        {
            visualSprite.flipY = (enemyTransform.position.x < transform.position.x);
        }

        if (invincibilityTimer > 0)
        {
            // 回避中（無敵中）は半透明にするなどの演出
            if (visualSprite != null)
            {
                Color c = visualSprite.color;
                c.a = 0.5f;
                visualSprite.color = c;
            }
        }
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition.x / maxX);
        sensor.AddObservation(transform.localPosition.y / maxY);
        sensor.AddObservation(stunTimer > 0);
        sensor.AddObservation(invincibilityTimer > 0);
        sensor.AddObservation(currentHealth / maxHealth);
        for (int i = 0; i < 4; i++)
        {
            // 既存のリキャスト観測
            sensor.AddObservation(spawner.GetRecastProgress(i));

            // ★追加：各スロットのスキルタイプを数値として教える
            // Normal=0, MagicCircle=1, Remote=2, Dodge=3, Assault=4 などの数値を正規化して渡す
            if (spawner.GetAttackPatterns()[i] != null)
            {
                sensor.AddObservation((float)spawner.GetAttackPatterns()[i].skillType / 5f);
            }
            else
            {
                sensor.AddObservation(-1f);
            }
        }

        if (enemyTransform != null)
        {
            sensor.AddObservation(enemyTransform.localPosition.x / maxX);
            sensor.AddObservation((Vector2)(enemyTransform.position - transform.position) / maxDetectionRadius);
        }
        else { sensor.AddObservation(0f); sensor.AddObservation(Vector2.zero); }

        string targetLayer = (spawner.myTeam == BulletSpawner.TeamSide.Player1) ? "Player2_Bullet" : "Player1_Bullet";
        var bullets = GameObject.FindGameObjectsWithTag("Enemy_Bullet")
            .Where(b => b.layer == LayerMask.NameToLayer(targetLayer))
            .OrderBy(b => Vector2.Distance(b.transform.position, transform.position))
            .Take(observeBulletCount).ToList();

        foreach (var b in bullets)
        {
            sensor.AddObservation((Vector2)(b.transform.position - transform.position) / maxDetectionRadius);
            var eb = b.GetComponent<EnemyBullet>();
            sensor.AddObservation(eb != null ? (Vector2)eb.Velocity / 10f : Vector2.zero);
            sensor.AddObservation(eb != null ? (Vector2)eb.Acceleration / 5f : Vector2.zero);
        }

        for (int i = 0; i < observeBulletCount - bullets.Count; i++)
        {
            sensor.AddObservation(Vector2.zero);
            sensor.AddObservation(Vector2.zero);
            sensor.AddObservation(Vector2.zero);
        }

        // 各スキルのチャージ状況（引き絞り具合）を観測に追加 (+4個)
        for (int i = 0; i < 4; i++) sensor.AddObservation(spawner.GetFanAngleProgress(i));

        // ★追加：近くの敵の魔方陣を観測する
        // 「Enemy_Bullet」タグが付いているオブジェクトの中から、MagicCircleコンポーネントを持つものを探す
        var magicCircles = GameObject.FindGameObjectsWithTag("Enemy_Bullet")
            .Select(go => go.GetComponent<MagicCircle>())
            .Where(mc => mc != null && mc.Team != this.MyTeam) // 相手チームの魔方陣
            .OrderBy(mc => Vector2.Distance(mc.transform.position, transform.position))
            .Take(2) // 近いもの2つを観測
            .ToList();

        foreach (var mc in magicCircles)
        {
            // 1. 相対位置を観測
            sensor.AddObservation((Vector2)(mc.transform.position - transform.position) / maxDetectionRadius);
            // 2. ★追加：魔方陣の半径を観測（正規化のため10f程度で割る）
            sensor.AddObservation(mc.Radius / 10f);
        }

        // パディング処理
        for (int i = 0; i < 2 - magicCircles.Count; i++)
        {
            sensor.AddObservation(Vector2.zero);
            sensor.AddObservation(0f); // 半径のダミー
        }

    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 学習中ではない 且つ ラウンドがアクティブでないなら入力を受け付けない
        if (!isRoundActive)
        {
            if (rb != null) rb.linearVelocity = Vector2.zero;
            return;
        }

        // 1. エピソード終了判定（タイムアップ / MaxStep到達）
        if (StepCount >= MaxStep - 1 && MaxStep > 0)
        {
            SetReward(clearBonus);
            EndEpisode();
            return;
        }

        // --- 前準備：学習中かどうかのフラグ ---
        bool isTraining = Academy.Instance.IsCommunicatorOn;

        // 2. 状態異常（スタン・無敵）の処理
        bool canMove = true;
        if (stunTimer > 0)
        {
            stunTimer -= Time.deltaTime;
            canMove = false;
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (visualSprite != null) visualSprite.color = Color.red;
            if (stunTimer <= 0) invincibilityTimer = invincibilityDuration;
        }
        else if (invincibilityTimer > 0)
        {
            invincibilityTimer -= Time.deltaTime;
            if (visualSprite != null) { Color c = Color.cyan; c.a = 0.5f; visualSprite.color = c; }
        }
        else
        {
            if (visualSprite != null) visualSprite.color = normalColor;
        }

        // 3. メインアクション処理
        if (canMove)
        {
            ApplyMagicCirclePenalty();
            // --- A. 移動ロジック（Continuous Actions） ---
            var CA = actions.ContinuousActions;
            float moveX = Mathf.Clamp(CA[0], -1f, 1f);
            float moveY = Mathf.Clamp(CA[1], -1f, 1f);
            bool isSlowInput = CA.Length >= 3 && CA[2] > 0.5f;

            float firingMultiplier = (spawner != null) ? spawner.GetCurrentSpeedMultiplier() : 1f;
            float speed = (isSlowInput ? slowSpeed : normalSpeed) * firingMultiplier;

            Vector2 moveInput = new Vector2(moveX, moveY);
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
            if (rb != null) rb.linearVelocity = moveInput * speed;

            transform.localPosition = new Vector3(
                Mathf.Clamp(transform.localPosition.x, minX, maxX),
                Mathf.Clamp(transform.localPosition.y, minY, maxY), 0);

            // --- B. 射撃アクションの決定（Discrete Actions） ---
            int actionToExecute = actions.DiscreteActions[0];

            // 学習中のみの特殊報酬・挙動
            if (isTraining)
            {
                // ① 積極的射撃思考の「強制」を廃止
                if (actionToExecute > 0)
                {
                    int skillIdx = actionToExecute - 1;
                    if (spawner != null && spawner.GetRemainingRecastTime(skillIdx) <= 0)
                    {
                        // 撃ったことへの直接加点は「ごく微量」に抑える（ボタンを押すきっかけ作り程度）
                        AddReward(0.001f);

                        // ★戦略的インセンティブ：敵との距離に応じたスキルの使い分けを促す
                        float dist = Vector2.Distance(transform.position, enemyTransform.position);

                        // 例：遠距離なら特定のスキル(Zなど)に微加点、近距離なら別のスキル(Xなど)に微加点
                        // これにより、AIは「この距離ならこのボタン」という傾向を学習し始めます
                        if (skillIdx == 0 && dist > 7f) AddReward(0.002f); // 遠距離での牽制
                        if (skillIdx == 1 && dist < 4f) AddReward(0.002f); // 近距離での迎撃
                    }
                    else
                    {
                        // リキャスト中の無駄撃ちへの厳罰化（これを強くすると「考えて撃つ」ようになります）
                        AddReward(-0.01f);
                    }
                }
                // ★重要：「撃てるのに撃たなかった（サボり）減点」を削除します。
                // これを消すことで、AIは「今は撃たずに温存する」という選択肢を選べるようになります。
            }

            // --- C. 命令の実行（★ UpdateInputState はここ1回だけ！） ---
            if (spawner != null)
            {
                spawner.UpdateInputState(actionToExecute);

                // デバッグログ：0以外（スキル選択中）のときだけ表示
                if (actionToExecute != 0)
                {
                    // 推論時でも学習時でも、実際に命令が通っているか確認できる
                    // Debug.Log($"{gameObject.name} Action: {actionToExecute}");
                }
            }

            // --- D. 引き絞り（チャージ）＆照準報酬 ---
            if (spawner != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (spawner.IsCharging(i))
                    {
                        float progress = spawner.GetFanAngleProgress(i);
                        float exponentialBonus = Mathf.Pow(progress, 2);

                        if (enemyTransform != null)
                        {
                            Vector2 toEnemy = (enemyTransform.position - transform.position).normalized;
                            float angleToEnemy = Mathf.Atan2(toEnemy.y, toEnemy.x) * Mathf.Rad2Deg;
                            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.z, angleToEnemy));

                            // 敵を射程（30度以内）に捉えつつチャージしていれば加点
                            if (angleDiff < 30f)
                            {
                                AddReward(0.002f * exponentialBonus);
                            }
                        }

                        // 最大チャージ維持報酬
                        if (progress >= 1.0f) AddReward(0.01f);
                    }
                }
            }
        }

        // 4. 共通報酬・ペナルティの適用
        ApplyPositionRewards();
        ApplyWallPenalty();
        AddReward(survivalReward); // 生存すること自体の微小報酬
    }
    void ApplyWallPenalty()
    {
        // 1. 壁からの距離のしきい値（例：1ユニット以内なら「壁際」とみなす）
        float wallThreshold = 1.0f;

        // 2. 現在の座標が各壁のしきい値内に入っているか判定
        bool nearLeft = transform.localPosition.x < minX + wallThreshold;
        bool nearRight = transform.localPosition.x > maxX - wallThreshold;
        bool nearBottom = transform.localPosition.y < minY + wallThreshold;
        bool nearTop = transform.localPosition.y > maxY - wallThreshold;

        // 3. いずれかの壁に近い場合に微小なペナルティを課す
        if (nearLeft || nearRight || nearBottom || nearTop)
        {
            // 生存報酬 (0.002f) よりも小さく設定するのがコツです
            AddReward(-0.001f);
        }
    }
    void ApplyPositionRewards()
    {
        if (enemyTransform == null || spawner == null) return;

        // 1. 敵との距離を計算
        float distance = Vector2.Distance(transform.position, enemyTransform.position);

        // キャラクターごとの理想的な範囲を取得
        float minDist = spawner.minOptimalDistance;
        float maxDist = spawner.maxOptimalDistance;

        // 近すぎることへの強い警告
        if (distance < minDist)
        {
            // 生存報酬 (0.002f) を打ち消す程度のペナルティを与える
            AddReward(-0.01f);
        }

        if (distance >= minDist && distance <= maxDist)
        {
            // 理想的な間合いにいる間、少しずつ加点
            AddReward(0.001f);
        }
        else
        {
            // 範囲外（近すぎ・遠すぎ）の場合は微小な減点
            AddReward(-0.0005f);
        }

        // 2. 画面端に寄っているかチェック（壁際ハメ防止）
        float edgeThreshold = 1.0f; // 壁から1ユニット以内を「端」とみなす
        bool isAtEdge =
            transform.localPosition.x < minX + edgeThreshold ||
            transform.localPosition.x > maxX - edgeThreshold ||
            transform.localPosition.y < minY + edgeThreshold ||
            transform.localPosition.y > maxY - edgeThreshold;

        if (isAtEdge)
        {
            // 画面端に滞在することへのペナルティ
            AddReward(-0.001f);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var CA = actionsOut.ContinuousActions;
        var DA = actionsOut.DiscreteActions;
        DA[0] = 0;

        if (spawner.myTeam == BulletSpawner.TeamSide.Player1)
        {
            float h = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            CA[0] = h; CA[1] = v;
            CA[2] = Input.GetKey(KeyCode.LeftShift) ? 1f : 0f;

            if (Input.GetKey(KeyCode.Z)) DA[0] = 1;
            else if (Input.GetKey(KeyCode.X)) DA[0] = 2;
            else if (Input.GetKey(KeyCode.C)) DA[0] = 3;
            else if (Input.GetKey(KeyCode.V)) DA[0] = 4; 
            else if (Input.GetKey(KeyCode.D)) DA[0] = 5; // ★追加
        }
        else
        {
            float h = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            CA[0] = h; CA[1] = v;
            CA[2] = Input.GetKey(KeyCode.RightShift) ? 1f : 0f;

            if (Input.GetKey(KeyCode.I)) DA[0] = 1;
            else if (Input.GetKey(KeyCode.O)) DA[0] = 2;
            else if (Input.GetKey(KeyCode.P)) DA[0] = 3;
            else if (Input.GetKey(KeyCode.L)) DA[0] = 4;
            else if (Input.GetKey(KeyCode.K)) DA[0] = 5; // ★追加
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        // 無敵中は全ての判定を無視（最優先）
        // ラウンド中でない場合も即座に終了
        if (!isRoundActive || invincibilityTimer > 0) return;

        if (col.CompareTag("Enemy_Bullet"))
        {
            // 1. 普通の弾（EnemyBullet）の場合の処理
            EnemyBullet eb = col.GetComponent<EnemyBullet>();
            if (eb != null)
            {
                // 敵のチームの弾であることを確認
                if (eb.Team != this.MyTeam)
                {
                    // ★ 修正の核：連続ヒット判定
                    // スタン中であっても、弾が連続ヒット属性を持っていれば判定を続行する
                    bool isStunned = stunTimer > 0;
                    bool canContinuousHit = eb.IsContinuousHit;

                    // 「スタン中」かつ「連続ヒット属性がない」弾なら、ここで処理を中断（当たらない）
                    if (isStunned && !canContinuousHit) return;

                    // 被弾処理を実行：ここで stunTimer がリセット（延長）される
                    TakeDamage(eb.DamageValue, eb.Team);
                    eb.Deactivate();
                }
                return;
            }

            // 2. アサルトオブジェクト等の場合
            AssaultObject ao = col.GetComponent<AssaultObject>();
            if (ao != null)
            {
                // AssaultObject側でチームチェック・無敵チェックを行って
                // ダメージが必要な場合は TakeDamage を直接呼ぶ設計のため、
                // ここでは条件判定のみ通して処理を完了させます。
            }
        }
    }
    private IEnumerator RoundStartRoutine()
    {
        isRoundActive = false;
        if (visualSprite != null) visualSprite.color = normalColor;

        // 学習中でない場合のみ、カウントダウン演出を実行
        if (!Academy.Instance.IsCommunicatorOn)
        {
            centerNotificationText.gameObject.SetActive(true);

            centerNotificationText.text = "3";
            yield return new WaitForSeconds(1f);

            centerNotificationText.text = "2";
            yield return new WaitForSeconds(1f);

            centerNotificationText.text = "1";
            yield return new WaitForSeconds(1f);

            centerNotificationText.text = "GO Shoot!!";
            yield return new WaitForSeconds(0.5f);

            centerNotificationText.gameObject.SetActive(false);
        }

        // ゲーム（アクション受け付け）開始
        isRoundActive = true;
    }
    private void Respawn()
    {
        Debug.Log($"{gameObject.name} が倒されました。リスポーンします。");
        AddReward(-0.5f);

        currentHealth = maxHealth;
        UpdateHPUI(true); // ★リスポーン時は即座に満タン

        float offset = 6.0f;
        bool isP1 = (spawner != null && spawner.myTeam == BulletSpawner.TeamSide.Player1);
        float startX = isP1 ? -offset : offset;
        transform.localPosition = new Vector3(startX, 0f, 0f);
        if (rb != null) rb.linearVelocity = Vector2.zero;

        stunTimer = 0f;
        invincibilityTimer = invincibilityDuration;

        if (visualSprite != null)
        {
            Color c = Color.cyan;
            c.a = 0.5f;
            visualSprite.color = c;
        }
    }
    public void Heal(float amount)
    {
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        UpdateHPUI(false);
        Debug.Log($"{gameObject.name} が回復！ 現在のHP: {currentHealth}");
    }

    // ★新規追加：外部からダメージを与えるための窓口
    public void TakeDamage(float amount, BulletSpawner.TeamSide attackerTeam)
    {
        // 無敵中なら何もしない
        if (invincibilityTimer > 0) return;

        // A. ステータス更新
        currentHealth -= amount;
        UpdateHPUI(false);

        // B. ★ 攻撃の強制中断（長押し解除）
        // 被弾したら弾幕を撃つのをやめるリクエストに対応
        if (spawner != null)
        {
            spawner.CancelAllSkills();
        }

        // C. 報酬・ペナルティ計算
        AddReward(hitPenalty);

        if (opponentAgent != null)
        {
            // 弾の時と同様、距離による報酬倍率を計算（任意で追加可能）
            float dist = Vector2.Distance(transform.position, opponentAgent.transform.position);
            float multiplier = (dist < opponentAgent.spawner.minOptimalDistance) ?
                Mathf.Lerp(0.1f, 1.0f, dist / opponentAgent.spawner.minOptimalDistance) : 1.0f;

            opponentAgent.AddReward(hitOpponentReward * multiplier);
        }

        // D. 死亡判定またはスタン発生
        if (currentHealth <= 0)
        {
            HandleMatchOver();
        }
        else
        {
            // 被弾リアクション
            stunTimer = stunDuration; // 弾道と同じスタン時間を適用
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (visualSprite != null) visualSprite.color = Color.red;
        }
    }
    // UI更新メソッドを修正
    private void UpdateHPUI(bool immediate = false)
    {
        float oldRatio = targetHpRatio;
        targetHpRatio = currentHealth / maxHealth;

       
    }

    public void TriggerInvincibility(float duration)
    {
        invincibilityTimer = duration; // 既存のタイマーを上書き
    }

    // ★回避アクション開始（BulletSpawnerから呼ばれる）
    public void StartDodgeAction(float duration)
    {
        if (isDodging) return;
        isDodging = true;

        // 1. 既存の無敵タイマーを設定（前回の実装を利用）
        TriggerInvincibility(duration);

        // 2. アニメーションを回避状態へ切り替え
        // ※ Animator側で "IsDodging" というBool型パラメータが必要です
        //if (animator != null) animator.SetBool("IsDodging", true);

        // 3. 残像生成コルーチンを開始
        StartCoroutine(SpawnGhostRoutine(duration));
    }

    // ★回避アクション終了（BulletSpawnerから呼ばれる）
    public void EndDodgeAction()
    {
        if (!isDodging) return;
        isDodging = false;

        // アニメーションを通常状態へ戻す
        if (animator != null) animator.SetBool("IsDodging", false);

        // ※無敵タイマーは時間経過で自然に切れるので、ここではリセットしません。
        // もし回避モーション終了と同時に無敵も切りたい場合は invincibilityTimer = 0; を追加してください。
    }

    // 残像を一定間隔で生成するコルーチン
    // DodgerAgent.cs の SpawnGhostRoutine 内

    private IEnumerator SpawnGhostRoutine(float duration)
    {
        float timer = 0f;
        while (timer < duration && isDodging)
        {
            if (ghostPrefab != null && visualSprite != null)
            {
                GameObject ghost = Instantiate(ghostPrefab, transform.position, transform.rotation);
                FadeOutSprite ghostScript = ghost.GetComponent<FadeOutSprite>();

                if (ghostScript != null)
                {
                    // ★ 今の自機の情報を丸ごとコピーして渡す
                    ghostScript.Setup(
                        visualSprite.sprite,
                        visualSprite.color,
                        visualSprite.sortingOrder,
                        visualSprite.transform.localScale,
                        visualSprite.transform.rotation,
                        visualSprite.flipX, //
                        visualSprite.flipY  //
                    );
                }
            }
            yield return new WaitForSeconds(ghostSpawnInterval);
            timer += ghostSpawnInterval;
        }
    }
    private void HandleMatchOver()
    {
        isRoundActive = false;

        // ★追加：タイマーマネージャー経由で弾を掃除する
        if (timerManager != null)
        {
            timerManager.ClearAllBullets();
        }

        // その後の勝利メッセージ表示などの処理...
        string winnerName = (opponentAgent != null) ? opponentAgent.CharacterName : "Enemy";
        ShowMatchResult($"{winnerName} Wins!");
    }
    // --- DodgerAgent.cs ---

    public void ShowMatchResult(string message)
    {
        isRoundActive = false;

        // 物理的な動きを完全に止める
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // スキル関連の停止とリキャスト全回復
        if (spawner != null)
        {
            spawner.CancelAllSkills();
            spawner.ResetAllRecasts();   // ★これでリキャストが0（即使用可能状態）になります
        }

        // 相手も止める（対戦相手がいる場合）
        if (opponentAgent != null && opponentAgent.IsRoundActive)
        {
            opponentAgent.StopAgentAction();
        }

        if (!Academy.Instance.IsCommunicatorOn && centerNotificationText != null)
        {
            centerNotificationText.gameObject.SetActive(true);
            centerNotificationText.text = message;
            Invoke(nameof(EndMatchEpisode), 3f);
        }
        else
        {
            EndMatchEpisode();
        }
    }
    public void StopAgentAction()
    {
        isRoundActive = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        if (spawner != null)
        {
            spawner.CancelAllSkills();
            spawner.ResetAllRecasts();
        }
    }
    private void EndMatchEpisode()
    {
        if (centerNotificationText != null) centerNotificationText.gameObject.SetActive(false);

        // 既存の報酬処理とEndEpisodeを実行
        AddReward(gameOverPenalty);
        if (opponentAgent != null) opponentAgent.EndEpisode();
        EndEpisode();
    }
    private void ApplyMagicCirclePenalty()
    {
        // 画面内の全魔方陣をチェック
        var magicCircles = GameObject.FindGameObjectsWithTag("Enemy_Bullet")
            .Select(go => go.GetComponent<MagicCircle>())
            .Where(mc => mc != null && mc.Team != this.MyTeam); // ★ mc.Team が使えるようになります

        foreach (var mc in magicCircles)
        {
            float dist = Vector2.Distance(transform.position, mc.transform.position);

            // 魔方陣の半径（mc.Radius）に基づいて回避判定
            if (dist < mc.Radius + 0.5f)
            {
                AddReward(-0.005f);
            }
        }
    }
}