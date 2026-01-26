using UnityEngine;

public class FadeOutSprite : MonoBehaviour
{
    private SpriteRenderer sr;
    [SerializeField] private float fadeSpeed = 3f; // 消える速さ

    // 生成時にエージェントから現在の見た目情報を受け取る
    public void Setup(Sprite sprite, Color color, int sortingOrder, Vector3 scale, Quaternion rotation, bool flipX, bool flipY)
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = sprite;
        // 元の色をベースに、少し半透明で開始する（0.5fはお好みで調整）
        sr.color = new Color(color.r, color.g, color.b, 0.5f);
        // 本体より少し後ろに表示
        sr.sortingOrder = sortingOrder - 1;
        // ★ 向きを合わせる
        sr.flipX = flipX;
        sr.flipY = flipY;
        transform.localScale = scale;
        transform.rotation = rotation;
    }

    void Update()
    {
        if (sr == null) return;
        // 徐々に透明にする
        Color c = sr.color;
        c.a -= fadeSpeed * Time.deltaTime;
        sr.color = c;

        // 完全に透明になったら削除
        if (c.a <= 0) Destroy(gameObject);
    }
}