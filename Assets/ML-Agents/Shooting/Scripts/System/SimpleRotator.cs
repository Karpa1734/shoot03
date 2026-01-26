using UnityEngine;

public class SimpleRotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("1秒間に何度回転させるか（Z軸が基本）")]
    [SerializeField] private Vector3 rotationSpeed = new Vector3(0, 0, 360f);

    [Tooltip("Time.timeScaleの影響を受けるか（ポーズ中も回したいならfalse）")]
    [SerializeField] private bool useDeltaTime = true;

    private void Update()
    {
        // 回転量の計算
        float dt = useDeltaTime ? Time.deltaTime : Time.unscaledDeltaTime;

        // オブジェクトを回転させる
        transform.Rotate(rotationSpeed * dt);
    }
}