using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Danmaku/CharacterData")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public Sprite characterSprite;
    public Color personalColor;   // パーソナルカラー

    [Header("Movement Stats")]
    public float normalSpeed = 5f;
    public float slowSpeed = 2f; 
    
    [Header("Status")]
    public float maxHealth = 100f;

    [Header("Attack Patterns")]
    public AttackPattern shotZ;
    public AttackPattern shotX;
    public AttackPattern shotC;
    public AttackPattern shotV;
}

// ★ ここにあった [System.Serializable] public class AttackPattern ... は削除しました