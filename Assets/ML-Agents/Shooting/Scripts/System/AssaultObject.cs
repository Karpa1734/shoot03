using UnityEngine;

public class AssaultObject : MonoBehaviour
{
    private float speed;
    private float lifeTimer;
    private int damage;
    private int opponentBulletLayer;
    private int opponentLayer;
    private int direction; // 1: 右, -1: 左
    private bool hasHit = false; // 重複ヒット防止

    // ★ 追加：クラス全体で spawner を参照するための変数
    private BulletSpawner spawnerRef;

    // 初期化（BulletSpawnerから呼ばれる）
    public void Initialize(BulletSpawner spawner, BulletSpawner.TeamSide myTeam, float moveSpeed, float duration, int damageValue)
    {
        // ★ 追加：受け取った spawner を変数に保存する
        this.spawnerRef = spawner;

        this.speed = moveSpeed;
        this.lifeTimer = duration;
        this.damage = damageValue;

        // 敵の方向を向く（Y軸固定、X軸のみ判定）
        if (spawner.Target != null)
        {
            direction = (spawner.Target.position.x > transform.position.x) ? 1 : -1;
        }
        else
        {
            direction = (transform.right.x >= 0) ? 1 : -1;
        }
        // もしスクリプトから半径を微調整したい場合
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
        {
            // 必要に応じて半径をセット（例：データの値を反映など）
            // circle.radius = 0.5f; 
        }
        // 画像の向きを進行方向に合わせる
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * direction;
        transform.localScale = scale;

        // 敵の弾レイヤーと、敵本体のレイヤーを特定
        string oppBullet = (myTeam == BulletSpawner.TeamSide.Player1) ? "Player2_Bullet" : "Player1_Bullet";
        string oppAgent = (myTeam == BulletSpawner.TeamSide.Player1) ? "Player2" : "Player1";

        opponentBulletLayer = LayerMask.NameToLayer(oppBullet);
        opponentLayer = LayerMask.NameToLayer(oppAgent);
    }

    private void Update()
    {
        // 直進移動
        transform.position += new Vector3(direction * speed * Time.deltaTime, 0, 0);

        // 寿命
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0) Destroy(gameObject);
    }

    // AssaultObject.cs の OnTriggerEnter2D メソッドを修正

    private void OnTriggerEnter2D(Collider2D col)
    {
        // 1. 弾消し処理
        if (col.gameObject.layer == opponentBulletLayer)
        {
            EnemyBullet bullet = col.GetComponent<EnemyBullet>();
            if (bullet != null) bullet.Deactivate();
            else BulletPool.Instance.ReturnToPool(col.gameObject);
        }

        // 2. 敵本体へのダメージ処理
        // レイヤー判定に加えて、チームチェックを念入りに行う
        if (!hasHit)
        {
            DodgerAgent hitAgent = col.GetComponentInParent<DodgerAgent>();

            if (hitAgent != null)
            {
                // ★追加：当たった相手が自分と同じチームなら無視する
                if (hitAgent.MyTeam == spawnerRef.myTeam) return;

                // 相手が無敵中なら通り抜ける
                if (hitAgent.IsInvincible) return;

                hasHit = true; // ヒット確定
                hitAgent.TakeDamage(damage, spawnerRef.myTeam);
                Debug.Log($"{col.gameObject.name} にアサルトがヒット！");
            }
        }
    }
}