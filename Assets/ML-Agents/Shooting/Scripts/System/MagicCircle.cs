using UnityEngine;
using System.Collections;

public class MagicCircle : MonoBehaviour
{
    private float expandSpeed = 8.0f;
    private float shrinkSpeed = 5.0f;
    private float rotationSpeed = 300f; // くるくる回転
    private Vector3 targetScale = Vector3.zero;
    private bool isExpiring = false;
    private int opponentBulletLayer;

    // 遠隔弾幕用の変数
    private Vector3? destination;
    private ShotData[] barrageData;
    private BulletSpawner spawnerRef;
    private BulletSpawner.TeamSide mySide;

    public void Initialize(Color color, BulletSpawner.TeamSide myTeam)
    {
        transform.localScale = Vector3.zero;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = color; // ここでキャラクターの色に変わります

        string opponentLayerName = (myTeam == BulletSpawner.TeamSide.Player1) ? "Player2_Bullet" : "Player1_Bullet";
        opponentBulletLayer = LayerMask.NameToLayer(opponentLayerName);
    }

    // ★ 遠隔モードのセットアップ
    public void LaunchToTarget(Vector3 dest, ShotData[] data, BulletSpawner spawner, BulletSpawner.TeamSide side)
    {
        destination = dest;
        barrageData = data;
        spawnerRef = spawner;
        mySide = side;
        SetTargetScale(0.5f); // 移動中は標準サイズ
    }

    public void SetTargetScale(float radius)
    {
        targetScale = new Vector3(radius, radius, 1);
    }

    public void Activate(float radius, float duration)
    {
        isExpiring = false;
        SetTargetScale(radius);
        StartCoroutine(ExpireRoutine(duration));
    }

    private void Update()
    {
        // 常に回転
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);

        // 目的地がある場合は移動
        if (destination.HasValue)
        {
            float dist = Vector3.Distance(transform.position, destination.Value);
            if (dist > 0.2f)
            {
                // 加速しながら向かう
                Vector3 dir = (destination.Value - transform.position).normalized;
                transform.position += dir * 15f * Time.deltaTime;
            }
            else
            {
                // 到着
                transform.position = destination.Value;
                destination = null;
                Activate(0.5f, 2.0f); // 展開
                StartCoroutine(ExecuteBarrageRoutine());
            }
        }

        float speed = isExpiring ? shrinkSpeed : expandSpeed;
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * speed);

        if (isExpiring && transform.localScale.x < 0.05f)
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator ExecuteBarrageRoutine()
    {
        // 展開が完了するまで待機（0.8秒など）
        yield return new WaitForSeconds(0.8f);

        if (spawnerRef != null && barrageData != null)
        {
            foreach (var data in barrageData)
            {
                // 魔方陣の位置から、従来通りのやり方で弾を発射
                spawnerRef.ExecuteShot(data, 0, 0, transform.position, mySide);
            }
        }

        // ★ 追加：撃ち終わったら即座に消滅プロセスへ移行
        isExpiring = true;
        targetScale = Vector3.zero; // サイズを0へ縮小開始
    }

    private IEnumerator ExpireRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        isExpiring = true;
        targetScale = Vector3.zero;
    }

    private void OnTriggerEnter2D(Collider2D therapeutic)
    {
        if (isExpiring) return;
        if (therapeutic.gameObject.layer == opponentBulletLayer)
        {
            EnemyBullet bullet = therapeutic.GetComponent<EnemyBullet>();
            if (bullet != null) bullet.Deactivate();
            else BulletPool.Instance.ReturnToPool(therapeutic.gameObject);
        }
    }
}