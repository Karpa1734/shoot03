using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUIDisplay : MonoBehaviour
{
    [Header("HP Components")]
    public Slider hpSlider;
    public Slider orangeSlider;
    public Image hpFillImage; // ★インスペクターでHPバーの「Fill」画像をセット
    [SerializeField] private float barSmoothSpeed = 8f;
    [SerializeField] private float orangeSmoothSpeed = 1.2f;
    [SerializeField] private float orangeDelayTime = 0.6f; // ダメージ後、オレンジが動き出すまでの時間

    [Header("Color Settings")]
    [SerializeField] private Color normalHpColor = new Color(0.2f, 1f, 0.2f); // 通常の緑
    [SerializeField] private Color dangerHpColor = new Color(1f, 0.2f, 0.2f); // ピンチの赤
    [SerializeField] private float dangerThreshold = 0.3f; // 30%以下でピンチ判定

    [Header("Text Components")]
    public TextMeshProUGUI nameText;

    [Header("Skill Components")]
    public SkillUIManager skillUIManager;

    private DodgerAgent targetAgent;
    private float orangeDelayTimer = 0f;
    private float lastHpRatio = 1f;

    public void Bind(DodgerAgent agent)
    {
        targetAgent = agent;
        if (agent == null) return;

        // 初期化時に現在のHPを即座に反映
        lastHpRatio = agent.targetHpRatio;
        if (hpSlider) hpSlider.value = lastHpRatio;
        if (orangeSlider) orangeSlider.value = lastHpRatio;

        BulletSpawner spawner = agent.GetComponent<BulletSpawner>();
        if (spawner != null && spawner.characterProfile != null)
        {
            if (skillUIManager != null)
            {
                AttackPattern[] patterns = spawner.GetAttackPatterns();
                skillUIManager.SetupIcons(patterns, spawner);
            }

            if (nameText != null)
            {
                nameText.text = spawner.characterProfile.characterName;
                nameText.color = spawner.characterProfile.personalColor;
            }
        }
    }

    void Update()
    {
        if (targetAgent == null) return;

        float currentTargetRatio = targetAgent.targetHpRatio;

        // 1. メインHPバーの更新
        if (hpSlider != null)
        {
            hpSlider.value = Mathf.Lerp(hpSlider.value, currentTargetRatio, Time.deltaTime * barSmoothSpeed);

            // ★ ピンチ判定：現在のゲージ量に合わせて色を変える
            if (hpFillImage != null)
            {
                hpFillImage.color = (hpSlider.value <= dangerThreshold) ? dangerHpColor : normalHpColor;
            }
        }

        // 2. オレンジゲージの制御（ダメージを受けた時だけタイマーをリセット）
        if (currentTargetRatio < lastHpRatio)
        {
            orangeDelayTimer = orangeDelayTime;
        }
        lastHpRatio = currentTargetRatio;

        if (orangeSlider != null)
        {
            if (orangeDelayTimer > 0)
            {
                orangeDelayTimer -= Time.deltaTime;
            }
            else
            {
                orangeSlider.value = Mathf.Lerp(orangeSlider.value, currentTargetRatio, Time.deltaTime * orangeSmoothSpeed);
            }
        }
    }
}