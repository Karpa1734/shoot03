using UnityEngine;

public class SkillUIManager : MonoBehaviour
{
    [SerializeField] private SkillIcon[] skillIcons = new SkillIcon[4];

    public void SetupIcons(AttackPattern[] patterns, BulletSpawner spawner)
    {
        for (int i = 0; i < 4; i++)
        {
            if (skillIcons[i] == null) continue;

            if (patterns[i] != null && patterns[i].skillIcon != null)
            {
                skillIcons[i].gameObject.SetActive(true);
                // ★ 修正：画像、番号、そして「データ元のスパナー」を渡す
                skillIcons[i].Setup(patterns[i].skillIcon, i, spawner);
            }
            else
            {
                skillIcons[i].gameObject.SetActive(false);
            }
        }
    }

    // ★ 追加：リキャストの参照先だけをサッと切り替えるためのメソッド
    public void SetTargetSpawner(BulletSpawner spawner)
    {
        foreach (var icon in skillIcons)
        {
            if (icon != null) icon.SetTargetSpawner(spawner);
        }
    }
}