using UnityEngine;
using UnityEngine.UI;

public class SpecialGaugeManager : MonoBehaviour
{
    [Header("Gauge Settings")]
    [SerializeField] private Slider gaugeSlider;
    [SerializeField] private float maxGauge = 100f;
    private float currentGauge = 0f;
    public bool IsFull => currentGauge >= maxGauge; // 満タンか確認用
    public float CurrentGauge => currentGauge;

    void Start()
    {
        if (gaugeSlider != null)
        {
            gaugeSlider.maxValue = maxGauge;
            gaugeSlider.value = 0f;
        }
    }

    // ゲージを加算する共通メソッド
    public void IncreaseGauge(float amount)
    {
        currentGauge = Mathf.Min(currentGauge + amount, maxGauge);

        if (gaugeSlider != null)
        {
            gaugeSlider.value = currentGauge;
        }
    }
    public void ConsumeFullGauge()
    {
        currentGauge = 0f;
        if (gaugeSlider != null) gaugeSlider.value = 0f;
    }

    // ゲージをリセット（後ほど消費処理で利用）
    public void ResetGauge()
    {
        currentGauge = 0f;
        if (gaugeSlider != null) gaugeSlider.value = 0f;
    }
}