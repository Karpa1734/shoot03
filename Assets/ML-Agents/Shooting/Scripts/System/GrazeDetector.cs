using UnityEngine;
using System.Collections.Generic;

public class GrazeDetector : MonoBehaviour
{
    [SerializeField] private SpecialGaugeManager gaugeManager;
    [SerializeField] private float gaugeGainPerGraze = 1.0f;

    // 同じ弾で連続してゲージが増えないようにリストで管理
    private HashSet<int> grazedBulletIds = new HashSet<int>();

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Enemy_Bullet"))
        {
            var eb = col.GetComponent<EnemyBullet>();
            var myAgent = GetComponentInParent<DodgerAgent>();

            // ★ myAgent.MyTeam を参照してチームを比較
            if (eb != null && myAgent != null && eb.Team != myAgent.MyTeam)
            {
                int bulletId = col.gameObject.GetInstanceID();
                if (!grazedBulletIds.Contains(bulletId))
                {
                    grazedBulletIds.Add(bulletId);

                    if (gaugeManager != null)
                    {
                        gaugeManager.IncreaseGauge(gaugeGainPerGraze);
                    }
                }
            }
        }
    }
    // 弾が画面外に出たり消えたりした際、リストが肥大化しないよう定期的に掃除
    // もしくは、ラウンド開始時にリセットする
    public void ResetGrazeList()
    {
        grazedBulletIds.Clear();
    }
}