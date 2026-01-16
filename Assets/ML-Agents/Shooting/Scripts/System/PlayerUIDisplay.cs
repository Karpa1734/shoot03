using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUIDisplay : MonoBehaviour
{
    [Header("HP Components")]
    public Slider hpSlider;
    public Slider orangeSlider;
    [SerializeField] private float barSmoothSpeed = 8f;   // 補間速度をUI側で持つ
    [SerializeField] private float orangeSmoothSpeed = 1f;

    [Header("Text Components")]
    public TextMeshProUGUI nameText;

    [Header("Skill Components")]
    public SkillUIManager skillUIManager;

    private DodgerAgent targetAgent;

    public void Bind(DodgerAgent agent)
    {
        targetAgent = agent;
        if (agent == null) return;

        // ★ 修正：エージェントのインスペクター参照ではなく、Profileデータから直接取得する
        BulletSpawner spawner = agent.GetComponent<BulletSpawner>();
        if (spawner != null && spawner.characterProfile != null)
        {
            var profile = spawner.characterProfile;
            if (nameText != null)
            {
                nameText.text = profile.characterName;
                nameText.color = profile.personalColor;
            }

            // スキルアイコンの更新
            if (skillUIManager != null)
            {
                skillUIManager.SetupIcons(spawner.GetAttackPatterns());
            }
        }
    }

    void Update()
    {
        if (targetAgent == null) return;

        // ★ 修正：バインドされたエージェントの「HP割合」データを見て、自分のスライダーを動かす
        if (hpSlider != null)
        {
            hpSlider.value = Mathf.Lerp(hpSlider.value, targetAgent.targetHpRatio, Time.deltaTime * barSmoothSpeed);
        }

        if (orangeSlider != null)
        {
            // オレンジ色のゲージも同様に補間（必要に応じて待機タイマーロジックもこちらに移せます）
            orangeSlider.value = Mathf.Lerp(orangeSlider.value, targetAgent.targetHpRatio, Time.deltaTime * orangeSmoothSpeed);
        }
    }
}