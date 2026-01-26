using UnityEngine;
using System.Collections.Generic;

// 弾の変化ステップを定義
[System.Serializable]
public class BulletChangeStep
{
    [Tooltip("発射からの経過フレーム数でトリガー（1 = 次のフレーム）")]
    public int triggerFrame;

    [Header("Visual & Collision")]
    public Sprite newSprite;
    public float newColliderRadius = -1f; // -1なら変更なし
    public Vector3 newScale = Vector3.zero; // Zeroなら変更なし

    [Header("Trajectory (Absolute)")]
    public bool changeTrajectory = false;
    public float newSpeed;
    public float newSpeedAcc;
    public float newAngleOffset; // 現在の角度に対するオフセット、または絶対角
    public bool isAbsoluteAngle = false;

    [Header("Ultimate Knife Settings")]
    public bool aimAtTarget;            // ONにすると変化した瞬間に敵の方向を向く
    public BulletRotationMode newRotationMode; // 変化後の回転モード (Fixed, ConstantSpin等)
    public float newSpinSpeed;          // 回転モードが ConstantSpin の時の速さ
}
public enum BulletStartupType { None, RotateX, RotateY, RotateZ, MoveX, MoveY, Scale }

// 出現演出のパラメータ
[System.Serializable]
public class BulletStartupEffect
{
    public BulletStartupType type;
    public float startValue;
    public float endValue;
    public int durationFrames; // 何フレームかけて変化させるか
}
// 出現時の演出タイプ
public enum ColliderShape { Circle, Capsule, Cross } // ★追加 } // 形状の定義
public enum FireType { Instant, OnRelease } // 発射タイプの定義
// 既存の enum 等と同じ場所に定義
public enum BulletRotationMode { FaceMovement, Fixed, ConstantSpin }
[CreateAssetMenu(fileName = "NewBulletData", menuName = "Danmaku/BulletData")]
public class BulletData : ScriptableObject
{
    public enum SizeCategory { Large, Middle, Small }
    [Header("Fire Logic Settings")]
    public FireType fireType = FireType.Instant; // デフォルトは即時発射
    [Header("Basic Info")]
    public string bulletName;
    public Sprite bulletSprite;
    public GameObject deathEffectPrefab;
    public SizeCategory sizeCategory;

    [Header("Combat Settings")]
    public int damage = 10;

    [Header("Effect Settings")]
    public DelayColor delayColor;
    public bool isAdditive;// ★ 追加：予兆エフェクトを表示するかどうかのフラグ
    public bool useLaunchDelayEffect = true;
    [Header("Rotation Settings")]
    public BulletRotationMode rotationMode = BulletRotationMode.FaceMovement;
    public float spinSpeed = 360f; // ConstantSpin の時の回転速度（度/秒）

    [Header("Collision Settings")] 
    public ColliderShape colliderShape = ColliderShape.Circle; // デフォルトは円
    public float colliderRadius = 0.1f;
    public float capsuleHeight = 1.0f;  // ★追加：カプセルの長さ（槍の長さに合わせる）
    public CapsuleDirection2D capsuleDirection = CapsuleDirection2D.Vertical;
    public Vector2 colliderOffset = Vector2.zero;

    [Header("Visual Settings")]
    public Vector3 localScale = Vector3.one;

    [Header("Startup Effect (Pre-fire Animation)")]
    // ★ ここに配置することで、各弾のデータとして保存・設定できるようになります
    public BulletStartupEffect startupEffect = new BulletStartupEffect();

    [Header("Sub-Shot Settings (Spawning from Bullet)")]
    public bool spawnSubBullets = false;        // この弾から子弾を出すか
    public ShotData subShotData;                // 出現させる弾幕のデータ
    public int spawnIntervalFrames = 10;        // 何フレームごとに出現させるか
    public float bulletLifespan = -1f;          // 弾の寿命（-1なら無制限）

    [Header("Special Attributes")]
    [Tooltip("ONの場合、相手がスタン状態(stunTimer > 0)でも被弾判定が発生します")]
    public bool isContinuousHit = false;

    [Header("Phase Changes")]
    [Tooltip("時間経過で変化するステップのリスト（実行順に並べること）")]
    public List<BulletChangeStep> changeSteps = new List<BulletChangeStep>();


}