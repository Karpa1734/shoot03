using UnityEngine;
using Unity.MLAgents;

public class GameTimerManager : MonoBehaviour
{
    [Header("Timer Settings")]
    [SerializeField] private float initialTime = 30f;
    private float currentTime;
    private bool isTimerStopped = false; // タイマー停止フラグ

    [Header("References")]
    [SerializeField] private TopUITimerDisplay timerUI;
    [SerializeField] private DodgerAgent player1;
    [SerializeField] private DodgerAgent player2;

    void Start()
    {
        ResetTimer();
    }

    public void ResetTimer()
    {
        currentTime = initialTime;
        isTimerStopped = false; // フラグリセット

        if (timerUI != null)
        {
            timerUI.UpdateTimer(currentTime);
        }
    }

    void Update()
    {
        if (isTimerStopped) return; // 決着がついたら更新しない

        bool isTraining = Academy.Instance.IsCommunicatorOn;

        if (isTraining)
        {
            if (player1 != null && player1.MaxStep > 0)
            {
                float ratio = 1f - ((float)player1.StepCount / player1.MaxStep);
                currentTime = ratio * initialTime;
                if (timerUI != null) timerUI.UpdateTimer(currentTime);
            }
        }
        else
        {
            // ラウンド開始前は待機
            if (player1 != null && !player1.IsRoundActive)
            {
                if (timerUI != null) timerUI.UpdateTimer(currentTime);
                return;
            }

            if (currentTime > 0)
            {
                currentTime -= Time.deltaTime;
                if (timerUI != null) timerUI.UpdateTimer(currentTime);

                if (currentTime <= 0)
                {
                    TimeUp();
                }
            }
        }
    }

    private void TimeUp()
    {
        currentTime = 0;
        isTimerStopped = true; // タイマー更新を止める
        if (timerUI != null) timerUI.UpdateTimer(0f);

        // 1. 弾幕をすべて消去
        ClearAllBullets();

        // 2. 勝敗判定（HP比較）
        string resultMessage = "";
        float hp1 = player1.CurrentHealth;
        float hp2 = player2.CurrentHealth;

        if (hp1 > hp2)
        {
            resultMessage = $"{player1.CharacterName} Wins!";
        }
        else if (hp2 > hp1)
        {
            resultMessage = $"{player2.CharacterName} Wins!";
        }
        else
        {
            resultMessage = "Draw";
        }

        Debug.Log($"タイムアップ！結果: {resultMessage}");

        // 3. 操作不能にして結果表示
        // DodgerAgent側の終了処理メソッドを呼び出す
        player1.ShowMatchResult(resultMessage);
        player2.ShowMatchResult(resultMessage);
    }

    public void ClearAllBullets()
    {
        // 1. プール内の弾をすべて回収
        if (BulletPool.Instance != null)
        {
            BulletPool.Instance.ReturnAllBullets();
        }

        // 2. タグによる削除（確実にタグが付いているもの）
        GameObject[] bullets = GameObject.FindGameObjectsWithTag("Enemy_Bullet");
        foreach (var b in bullets)
        {
            var eb = b.GetComponent<EnemyBullet>();
            if (eb != null) eb.Deactivate(); // プールへ戻す
            else Destroy(b); // スクリプトがない場合は直接削除
        }

        // 3. ★レイヤーによる念押し削除
        // Player1_Bullet と Player2_Bullet レイヤーに属するものをすべて消す
        int p1Layer = LayerMask.NameToLayer("Player1_Bullet");
        int p2Layer = LayerMask.NameToLayer("Player2_Bullet");

        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == p1Layer || obj.layer == p2Layer)
            {
                var eb = obj.GetComponent<EnemyBullet>();
                if (eb != null) eb.Deactivate();
                else Destroy(obj);
            }
        }
    }
}