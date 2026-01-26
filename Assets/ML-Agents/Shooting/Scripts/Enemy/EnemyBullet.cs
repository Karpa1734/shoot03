using Unity.AppUI.UI;
using UnityEngine;

public class EnemyBullet : MonoBehaviour
{
    [Header("Screen Bounds")]
    public float minX = -10f;
    public float maxX = 10f;
    public float minY = -6f;
    public float maxY = 6f;
    // --- スクリプト上部に追加 ---
    private GameObject deathEffectPrefab; // インスペクターでプレハブを指定

    [Header("Materials")]
    [SerializeField] private Material additiveMaterial; // 加算用マテリアル
    private Material defaultMaterial;

    private BulletRotationMode rotationMode;
    private float spinSpeed;

    // Parameters set by spawner
    private float currentSpeed;
    private float speedAcc;
    private float maxSpeed;
    private float currentAngle;
    private float angleAcc;
    private float maxAngle;
    private bool hasAngleLimit = false;
    private bool shouldRotate = true; // 画像を進行方向に向けるか
    private int frameCounter = 0;
    private BulletData currentData;
    private float lifeTimer = 0f;
    private int subSpawnFrameCounter = 0;

    private SpriteRenderer spriteRenderer;

    [Header("Collision Components")]
    [SerializeField] private CircleCollider2D circleCollider;
    [SerializeField] private CapsuleCollider2D capsuleCollider; // ★追加
    [SerializeField] private PolygonCollider2D polygonCollider; // ★追加：多角形コライダー
    // internal
    private Vector3 velocity = Vector3.zero;
    private Vector3 lastVelocity = Vector3.zero;
    private Vector3 _calculatedAcceleration;
    private BulletData originData;
    private int nextStepIndex = 0;
    private int framesSinceSpawn = 0; // float timeSinceSpawn から変更
    private int currentSortingOrder; // ★追加：現在の描画順を保持
    private bool isPreparing = false;
    private bool isClosing = false;    // 消滅演出中か
                                       // --- 変数定義の変更 ---
    private float prepTimer = 0f;   // int prepFrameCount から変更
    private float closeTimer = 0f;  // int closeFrameCount から変更
    private Vector3 originalLocalPos;
    private BulletSpawner mySpawner;
    // メンバ変数に追加
    private BulletSpawner.TeamSide myTeam;

    // ★追加：外部からチーム情報を取得するための公開プロパティ
    public BulletSpawner.TeamSide Team => myTeam;
    // Expose for Agent observation
    public Vector3 Velocity => velocity;
    public Vector3 Acceleration => _calculatedAcceleration;
    // 外部（PlayerHealth）からダメージ量を読み取るためのプロパティ
    public int DamageValue => (originData != null) ? originData.damage : 1;// EnemyBullet.cs 内の推奨修正
                                                                           // ★追加：連続ヒット属性を持っているか外部から参照するためのプロパティ
    public bool IsContinuousHit => (originData != null) ? originData.isContinuousHit : false;

    private float subSpawnTimer = 0f;
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (circleCollider == null) circleCollider = GetComponent<CircleCollider2D>();
        if (capsuleCollider == null) capsuleCollider = GetComponent<CapsuleCollider2D>();
        if (polygonCollider == null) polygonCollider = GetComponent<PolygonCollider2D>(); // ★追加
        if (spriteRenderer != null) defaultMaterial = spriteRenderer.material;
    }

    public void Setup(BulletData data, Vector3 pos, float initSpeed, float spAcc, float spMax, float initAngle, float anAcc, float anMax, int orderInLayer, BulletSpawner.TeamSide team, BulletSpawner spawner)
    {
        this.mySpawner = spawner;
        this.myTeam = team; // 自分のチームを記憶
        originData = data;
        nextStepIndex = 0;
        framesSinceSpawn = 0; // Delayを含まないカウント開始
        currentSortingOrder = orderInLayer; // 描画順を保存
        UpdateVelocityAndRotation();
        if (data != null && spriteRenderer != null)
        {
            deathEffectPrefab = data.deathEffectPrefab;
            spriteRenderer.sprite = data.bulletSprite;
            spriteRenderer.sortingLayerName = "Middle";
            spriteRenderer.sortingOrder = currentSortingOrder;
            transform.localScale = data.localScale;
            // ★回転モードとスピードをデータから受け取る
            this.rotationMode = data.rotationMode;
            this.spinSpeed = data.spinSpeed;
            // 加算合成の適用
            spriteRenderer.material = (data.isAdditive && additiveMaterial != null) ? additiveMaterial : defaultMaterial;

            // --- ここから当たり判定の個別設定 ---
            if (data.colliderShape == ColliderShape.Circle)
            {
                if (circleCollider != null)
                {
                    circleCollider.enabled = true;
                    // ★重要：データの半径を反映（これがないと全部同じ大きさになる）
                    circleCollider.radius = data.colliderRadius;
                }
                if (capsuleCollider != null) capsuleCollider.enabled = false;
                if (polygonCollider != null) polygonCollider.enabled = false;
            }
            else if (data.colliderShape == ColliderShape.Capsule)
            {
                if (capsuleCollider != null)
                {
                    capsuleCollider.enabled = true;

                    // ★ カプセルのサイズを計算
                    // 半径(radius)を幅として扱い、capsuleHeight を長さとして適用します
                    float width = data.colliderRadius * 2f;
                    float height = data.capsuleHeight;

                    // ★ 方向とサイズをセット
                    capsuleCollider.direction = data.capsuleDirection;
                    capsuleCollider.size = new Vector2(width, height);

                    // オフセットも反映
                    capsuleCollider.offset = data.colliderOffset;
                }
                if (circleCollider != null) circleCollider.enabled = false;
                if (polygonCollider != null) polygonCollider.enabled = false;
            }
            else if (data.colliderShape == ColliderShape.Cross)
            {
                if (polygonCollider != null)
                {
                    polygonCollider.enabled = true;
                    // PolygonCollider2D は形状が複雑なため、
                    // 基本的にはプレハブ側で設定した形状がそのまま使われます
                }
                if (circleCollider != null) circleCollider.enabled = false;
                if (capsuleCollider != null) capsuleCollider.enabled = false;
            }
        }

        // 出現演出（Startup Effect）の初期化
        if (data != null && data.startupEffect.durationFrames > 0)
        {
            isPreparing = true; 
            prepTimer = 0f;
            closeTimer = 0f;
            ApplyStartupEffect(0f); // 初期状態を適用
        }
        else
        {
            isPreparing = false;
        }


        // 初期角度に基づいて一度向きをリセット
        if (rotationMode == BulletRotationMode.FaceMovement)
        {
            transform.rotation = Quaternion.Euler(0, 0, currentAngle - 90f);
        }
        else if (rotationMode == BulletRotationMode.Fixed)
        {
            // 固定の場合は、データの localScale 等と一緒に設定された初期角度を守る
            // 必要に応じて transform.rotation = Quaternion.Euler(0, 0, data.fixedVisualAngle); 等
        }

        // 移動パラメータ初期化
        transform.position = pos;
        currentSpeed = initSpeed;
        speedAcc = spAcc;
        maxSpeed = spMax;
        currentAngle = initAngle;
        angleAcc = anAcc;
        maxAngle = anMax;
        hasAngleLimit = (anMax != 0);
        subSpawnFrameCounter = 0; // カウンターをリセット
        lifeTimer = 0f;           // 寿命タイマーをリセット

        //Debug.Log($"[Bullet Setup] Shape: {data.colliderShape}, Speed: {currentSpeed}, Order: {currentSortingOrder}");

        UpdateVelocityAndRotation();
    }
    void Update()
    {


        framesSinceSpawn++;

        // --- A. 出現演出中 ---
        if (isPreparing)
        {
            UpdatePreparation();
            UpdateDelayVisuals();
            return;
        }

        // ★追加：B. 消滅演出中（逆再生）
        if (isClosing)
        {
            UpdateClosing();
            return;
        }
        // ★追加：多段変化（ステップ変化）の実行判定
        if (originData != null && nextStepIndex < originData.changeSteps.Count)
        {
            // 次のステップのデータを取得
            var nextStep = originData.changeSteps[nextStepIndex];

            // 設定されたフレーム数に到達したか判定
            // (BulletChangeStep クラスに time または durationFrames という変数がある想定です)
            if (framesSinceSpawn >= nextStep.time)
            {
                ApplyNextStep(nextStep);
                nextStepIndex++;
            }
        }
        // --- C. 通常の移動・寿命処理 ---
        if (originData != null && originData.bulletLifespan > 0)
        {
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= originData.bulletLifespan)
            {
                Deactivate();
                return;
            }
        }

        // --- 1. 寿命（Lifespan）の処理 ---
        if (originData != null && originData.bulletLifespan > 0)
        {
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= originData.bulletLifespan)
            {
                Deactivate(); // 8秒などで消滅
                return;
            }
        }

        // 既存の多段変化チェック...
        if (originData != null && nextStepIndex < originData.changeSteps.Count) { /* ... */ }

        // 移動処理
        currentSpeed += speedAcc * Time.deltaTime;
        if (maxSpeed > 0f && currentSpeed > maxSpeed) currentSpeed = maxSpeed;
        currentAngle += angleAcc * Time.deltaTime;

        UpdateVelocityAndRotation();
        transform.position += velocity * Time.deltaTime;

        // --- 2. 子弾（Sub-Bullet）の生成処理 ---
        if (originData != null && originData.spawnSubBullets && originData.subShotData != null)
        {
            // フレームカウントではなく、経過時間で判定する
            subSpawnTimer += Time.deltaTime;

            float intervalSeconds = originData.spawnIntervalFrames / 60f;

            if (subSpawnTimer >= intervalSeconds)
            {
                subSpawnTimer = 0f;
                SpawnSubBullet();
            }
        }

        CheckOutOfBounds();
    }


    private void SpawnSubBullet()
    {
        if (mySpawner == null) return;

        mySpawner.ExecuteShot(
            originData.subShotData,
            0,
            0,
            this.transform.position,
            this.myTeam
        );
    }

    // 従来の溜め演出（色変化など）を維持するためのメソッド（例）
    private void UpdateDelayVisuals()
    {
        // スパナーやデータで定義された「溜め色」への変化などを継続
        if (originData != null && spriteRenderer != null)
        {
            // 準備（アニメーション）中も、設定されたDelayColorを反映させる
            // (既存のロジックに合わせて調整してください)
        }
    }
    private void ApplyNextStep(BulletChangeStep step)
    {
        // 多段変化の適用
        if (step.newSprite != null) spriteRenderer.sprite = step.newSprite;
        if (step.newColliderRadius > 0) circleCollider.radius = step.newColliderRadius;
        if (step.newScale != Vector3.zero) transform.localScale = step.newScale;

        if (step.changeTrajectory)
        {
            currentSpeed = step.newSpeed;
            speedAcc = step.newSpeedAcc;
            if (step.isAbsoluteAngle) currentAngle = step.newAngleOffset;
            else currentAngle += step.newAngleOffset;
        }

        // ★追加：回転モードとスピン速度の更新
        this.rotationMode = step.newRotationMode;
        this.spinSpeed = step.newSpinSpeed;

        if (step.changeTrajectory)
        {
            currentSpeed = step.newSpeed;
            speedAcc = step.newSpeedAcc;

            // ★追加：ターゲットへの再照準
            if (step.aimAtTarget && mySpawner != null && mySpawner.Target != null)
            {
                Vector2 dir = mySpawner.Target.position - transform.position;
                currentAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            }
            else
            {
                if (step.isAbsoluteAngle) currentAngle = step.newAngleOffset;
                else currentAngle += step.newAngleOffset;
            }
        }
        UpdateVelocityAndRotation();

    }

    private void CheckOutOfBounds()
    {
        Vector3 p = transform.position;
        if (p.x < minX || p.x > maxX || p.y < minY || p.y > maxY)
        {
            // × BulletPool.Instance.ReturnAllBullets(); // これだと全部消える
            BulletPool.Instance.ReturnToPool(gameObject); // ◎ 自分だけを戻す
        }
    }

    // --- 既存の Deactivate メソッドを修正 ---
    // ★修正：Deactivate を演出開始のトリガーにする
    public void Deactivate()
    {
        if (isClosing) return;

        if (originData != null && originData.startupEffect.durationFrames > 0)
        {
            isClosing = true;
            closeTimer = 0;

            // 演出中はすべてのコライダーを消す
            if (circleCollider != null) circleCollider.enabled = false;
            if (capsuleCollider != null) capsuleCollider.enabled = false;
            if (polygonCollider != null) polygonCollider.enabled = false; // ★追加
        }
        else
        {
            ActualDeactivate();
        }
    }

    // ★追加：消滅演出（逆再生）の更新
    private void UpdateClosing()
    {
        closeTimer += Time.deltaTime; //

        float durationSeconds = originData.startupEffect.durationFrames / 60f;
        float progress = 1.0f - Mathf.Clamp01(closeTimer / durationSeconds);

        ApplyStartupEffect(progress);

        if (closeTimer >= durationSeconds)
        {
            ActualDeactivate();
        }
    }

    // ★追加：最終的なプール返却処理
    // EnemyBullet.cs

    // ★修正：消滅演出を経ていない場合のみエフェクトを出すように変更
    private void ActualDeactivate()
    {
        // isClosing が false の場合（演出を介さず即座に消えた場合）のみエフェクトを発生
        if (!isClosing)
        {
            SpawnDeathEffect(); //
        }

        isClosing = false; // フラグをリセット
        BulletPool.Instance.ReturnToPool(gameObject); // プールに返却
    }

    private void UpdateVelocityAndRotation()
    {
        float rad = currentAngle * Mathf.Deg2Rad;
        velocity = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * currentSpeed;

        // 演出中（Startup/Closing）は独自の回転ロジック（ApplyStartupEffect）があるため、通常回転はスキップ
        if (isPreparing || isClosing) return;

        // ★リクエスト通り、場合分け（switch）で処理を切り分ける
        switch (rotationMode)
        {
            case BulletRotationMode.FaceMovement:
                // 進行方向を向く（従来の挙動）
                transform.rotation = Quaternion.Euler(0, 0, currentAngle - 90f);
                break;

            case BulletRotationMode.ConstantSpin:
                // 常に自転する（コインやノコギリ状の弾など）
                transform.Rotate(0, 0, spinSpeed * Time.deltaTime);
                break;

            case BulletRotationMode.Fixed:
                // 移動方向にかかわらず向きを固定（岩やブロックなどの弾）
                // 何もしない（初期設定の角度を維持）
                break;
        }
    }
    private void UpdatePreparation()
    {
        if (originData == null) return;

        prepTimer += Time.deltaTime; // フレーム加算から秒数加算へ

        // 秒数換算での進行度計算 (60fps想定のデータなら 60 で割る)
        float durationSeconds = originData.startupEffect.durationFrames / 60f;
        float progress = Mathf.Clamp01(prepTimer / durationSeconds);

        ApplyStartupEffect(progress);

        if (prepTimer >= durationSeconds)
        {
            isPreparing = false;
            prepTimer = 0f;
        }
    }

    private void ApplyStartupEffect(float t)
    {
        var effect = originData.startupEffect;
        float value = Mathf.Lerp(effect.startValue, effect.endValue, t);

        switch (effect.type)
        {
            case BulletStartupType.RotateX:
                transform.localRotation = Quaternion.Euler(value, 0, currentAngle - 90f);
                break;
            case BulletStartupType.RotateY:
                transform.localRotation = Quaternion.Euler(0, value, currentAngle - 90f);
                break;
            case BulletStartupType.RotateZ:
                transform.localRotation = Quaternion.Euler(0, 0, value);
                break;
            case BulletStartupType.MoveX:
                // 発射位置からの相対距離で動かす例
                transform.position += transform.right * (value * Time.deltaTime);
                break;
            case BulletStartupType.Scale:
                transform.localScale = originData.localScale * value;
                break;
        }
    }
    // --- エフェクト生成メソッドを修正 ---
    private void SpawnDeathEffect()
    {
        if (deathEffectPrefab != null && originData != null)
        {
            GameObject effect = Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);

            // --- 大きさの同期 ---
            effect.transform.localScale = originData.localScale * 2.5f;

            // --- レイヤーと描画順の同期 ★追加 ---
            // パーティクルシステムやスプライトなど、すべての Renderer を対象にする
            Renderer[] renderers = effect.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                r.sortingLayerName = "Middle"; // 弾と同じレイヤー
                r.sortingOrder = currentSortingOrder; // 弾と同じ順序
            }
        }
    }

}