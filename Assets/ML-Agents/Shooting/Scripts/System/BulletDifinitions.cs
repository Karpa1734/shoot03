using UnityEngine;

// --- 予兆エフェクトの色定義 ---
public enum DelayColor { RED, ORANGE, YELLOW, GREEN, AQUA, BLUE, PURPLE, WHITE }

[System.Serializable]
public class spanData
{
    public float default_;
    public float accuracy_;
    public float max_;
    public spanData() { }
    public spanData(float val, float acc, float max) { default_ = val; accuracy_ = acc; max_ = max; }
}

[System.Serializable]
public class NWayExpandSettings
{
    public int countAdd = 0;
    public float spreadAdd = 0f;
}

[System.Serializable]
public class ShotData
{
    public enum PatternType { Single, NWay, AllDirections, RandomGap, None } // ★ RandomGap を追加
    public enum AngleType { Fixed, AimAtPlayer, Random }

    [Header("Visual & Collision")]
    public BulletData bulletType;


    [Header("Pattern Settings")]
    public PatternType pattern = PatternType.Single;
    public AngleType angleType = AngleType.AimAtPlayer;
    public float fixedAngle = 270f;
    public int nWayCount = 3;
    public float nWaySpread = 30f;
    public int RoundCount = 36;
    public int speedCount = 1; // 弾数（速度のレイヤー数）
    public float speedMax = 8f; // 最大速度（一番外側の弾）

    [Header("NWay Expansion")]
    public NWayExpandSettings nWayExpand;

    [Header("Rotation Settings (連射時の回転)")]
    [Tooltip("1ステップ（連射）ごとに基本角度を何度回転させるか")]
    public float rotationPerStep = 0f;
    [Tooltip("全方位弾で偶数個の時に自動で角度をずらすか（自機外しを制御）")]
    public bool useEvenOffset = true;
    [Header("Random Gap Settings")]
    [Tooltip("プレイヤー周辺の弾を飛ばさない範囲（度）例：20度なら左右10度ずつ空く")]
    public float gapWidth = 20f;
    [Header("Movement Settings")]
    public spanData speedData = new spanData(3f, 0.5f, 8f);
    public spanData angleAccData = new spanData(0f, 0f, 0f);

    [Header("Delay Settings")]
    public int launchDelay = 0;

    [Header("Launch Randomness (初期値のゆらぎ)")]
    [Tooltip("発射時の速度に加えるランダム幅 (±n)")]
    public float launchSpeedJitter = 0f;
    [Tooltip("発射時の角度に加えるランダム幅 (±n度)")]
    public float launchAngleJitter = 0f;

    [Header("Spawn Position Settings")]
    public float spawnRadius = 0f;
}
// enumはクラスの外に出しておくと参照が楽になります
// SkillType 列挙型に Dodge を追加
public enum SkillType { Normal, MagicCircle, RemoteBarrage, Dodge, Assault }
[System.Serializable]
public class AttackPattern
{
    public ShotData[] multiShotData;
    public float recastTime;
    public float specialGaugeGain = 1.0f; // デフォルト値を設定
    public Sprite skillIcon;

    [Header("Behavior Settings")]
    public SkillType skillType = SkillType.Normal;
    // ★ 追加：移動しながら弾を放つモードを有効にするか
    public bool useMovingFire = false;
    [Header("Moving Barrage Settings (useMovingFire用)")]
    [Tooltip("魔方陣の移動速度。攻撃用は遅く（例: 2.0）、設置用は速く（例: 15.0）")]
    public float movingFireSpeed = 3.0f;

    [Tooltip("魔方陣が消滅するまでの時間（秒）")]
    public float movingFireDuration = 4.0f;

    [Tooltip("弾を出す間隔（フレーム数）。5〜15程度がおすすめ")]
    public int movingFireIntervalFrames = 10;
    [Header("Burst Settings")]
    public int burstCount = 1;
    public float burstInterval = 0.1f;
    // AttackPattern クラス内に追加
    [Header("Assault Settings")]
    [Tooltip("このスキルの基本ダメージ量（Assaultオブジェクトなどで使用）")]
    public int damage = 10; // ★追加：ここが不足していました
    public GameObject assaultPrefab; // 魔方陣とは別の見た目のプレハブ
    public float assaultSpeed = 20f; // かなり速めがおすすめ
    public float assaultDuration = 1.5f;

    [Header("Input & Speed Settings")]
    public FireType fireType = FireType.Instant;

    public bool targetBottomY = false; // ★ ONにすると敵の足元（最下段）を狙う

    [Header("Dodge Skill Settings")]
    public float dodgeDuration = 0.5f;        // 回避（無敵）時間
    public float dodgeSpeedMultiplier = 2.0f; // 回避中の移動速度倍率

    [Tooltip("trueなら押しっぱなしで連射、falseなら押し直しが必要")]
    public bool isAutoRepeat = true;

    [Range(0f, 1f)]
    [Tooltip("発射中の移動速度倍率 (1.0で等速、0.5で50%減速、0で停止)")]
    public float firingSpeedMultiplier = 1.0f;
}