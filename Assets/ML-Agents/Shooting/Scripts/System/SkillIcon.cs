using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillIcon : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image cooldownOverlay;
    [SerializeField] private TextMeshProUGUI timerText; // ★ 追加：リキャスト秒数用

    private int myIndex;
    private BulletSpawner targetSpawner;

    public void Setup(Sprite sprite, int index, BulletSpawner spawner)
    {
        if (iconImage != null) iconImage.sprite = sprite;
        myIndex = index;
        targetSpawner = spawner;
    }

    public void SetTargetSpawner(BulletSpawner spawner)
    {
        targetSpawner = spawner;
    }

    // SkillIcon.cs の修正

    void Update()
    {
        // ★重要：targetSpawner が正しく入れ替わっていれば、ここでの参照先も自動で切り替わります
        if (targetSpawner == null) return;

        // 1. ゲージ（暗転）の更新
        float progress = targetSpawner.GetRecastProgress(myIndex);
        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = progress;
        }

        // 2. 秒数テキストの更新
        if (timerText != null)
        {
            float remaining = targetSpawner.GetRemainingRecastTime(myIndex);
            if (remaining > 0.01f)
            {
                if (!timerText.gameObject.activeSelf) timerText.gameObject.SetActive(true);
                timerText.text = remaining.ToString("00.00") + "s";
            }
            else
            {
                if (timerText.gameObject.activeSelf) timerText.gameObject.SetActive(false);
            }
        }
    }
}