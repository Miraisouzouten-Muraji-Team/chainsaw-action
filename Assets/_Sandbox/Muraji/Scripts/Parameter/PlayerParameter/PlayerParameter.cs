using UnityEngine;

[CreateAssetMenu(menuName = "Player/PlayerParameter")]
public class PlayerParameter : ScriptableObject
{
    [Header("プレイヤーの基本パラメータ")]
    // 基本は移動速度/ジャンプ力/重力加速度/落下速度上限
    [Tooltip("移動速度")]
    [SerializeField]
    public float moveSpeed = 5f;
    [Tooltip("加速度")]
    [SerializeField]
    public float acceleration = 10f;
    [Tooltip("減速度")]
    [SerializeField]
    public float deceleration = 10f;
    [Tooltip("ジャンプ力")]
    [SerializeField]
    public float jumpForce = 10f;
    [Tooltip("二段ジャンプ力")]
    [SerializeField]
    public float airJumpForce = 10f;
    [Tooltip("コヨーテタイム")]
    [SerializeField,Min(0f)]
    public float coyoteTime = 0.1f;
    [Tooltip("重力の大きさ")]
    [SerializeField]
    public float gravityScale = 1f;
    [Tooltip("ジャンプ直後の重力倍率")]
    [SerializeField, Range(0.1f, 1f)]
    public float jumpStartGravityMultiplier = 0.5f;
    [Tooltip("落下中の最大重力倍率")]
    [SerializeField, Min(1f)]
    public float maxFallGravityMultiplier = 2f;
    [Tooltip("落下開始からの最大重力までの時間")]
    [SerializeField, Min(0.01f)]
    public float fallGravityIncreaseTime = 0.5f;
    [Tooltip("落下速度上限")]
    [SerializeField, Min(0.1f)]
    public float maxFallSpeed = 20f;

    [Header("壁衝突時のパラメータ")]
    // 壁衝突時のパラメータ
    [Tooltip("ノックバックスピード")]
    [SerializeField, Min(0f)]
    public float wallKnockbackSpeed = 5f;
    [Tooltip("ノックバックでの減速")]
    [SerializeField,Min(0.01f)]
    public float wallKnockbackDeceleration = 10f;
    [Tooltip("ノックバックでの上昇数")]
    [SerializeField, Min(0f)]
    public float wallKnockbackUpwardForce = 5f;
    [Tooltip("強衝突が発生したときの硬直時間")]
    [SerializeField, Min(0.01f)]
    public float diggingImpactStunDuration = 0.2f;
    [Tooltip("強衝突時に発生するカメラシェイクの時間")]
    [SerializeField, Min(0f)]
    public float impactShakeDuration = 0.15f;
    [Tooltip("強衝突時のカメラシェイクの強さ")]
    [SerializeField, Min(0f)]
    public float impactShakeMagnitude = 0.1f;

    [Header("食い込みパラメータ")]
    // 食い込み速度/硬直とカメラシェイク
    [Tooltip("食い込み速度")]
    [SerializeField, Min(0f)]
    public float surfaceStickSpeed = 5f;
    [Tooltip("壁に食い込んでいるときの上昇速度")]
    [SerializeField, Min(0f)]
    public float wallClimbSpeed = 5f;
    [Tooltip("食い込み壁ジャンプ力")]
    [SerializeField, Min(0f)]
    public float surfaceStickJumpForce = 10f;
    [Tooltip("壁ジャンプの角度")]
    [SerializeField, Range(0f, 90f)]
    public float surfaceStickJumpAngle = 45f;
    [Tooltip("壁ジャンプ時の硬直時間")]
    [SerializeField, Min(0f)]
    public float surfaceStickJumpStunTime = 0.1f;
    [Tooltip("壁ジャンプ時のカメラシェイク")]
    [SerializeField, Min(0f)]
    public float surfaceStickJumpCameraShake = 0.1f;
    [Tooltip("壁ジャンプ後の速度上書きしない時間")]
    [SerializeField, Min(0f)]
    public float surfaceStickJumpNoOverrideTime = 0.1f;
    [Tooltip("衝突速度")]
    [SerializeField, Min(0f)]
    public float surfaceStickCollisionSpeed = 5f;
    [Header("角のパラメータ")]
    // 天井の角の処理値
    [Tooltip("角の許容範囲")]
    [SerializeField, Min(0.01f)]
    public float cornerTolerance = 0.1f;
    [Tooltip("角の押し出し速度")]
    [SerializeField, Min(0.1f)]
    public float cornerPushSpeed = 5f;
    [Tooltip("角でのジャンプ力")]
    [SerializeField, Min(0f)]
    public float cornerJumpForce = 10f;
    [Tooltip("天井や壁を判定する接触面の法線の閾値")]
    [SerializeField, Range(0.1f, 0.95f)]
    public float impactSurfaceNormalThreshold = 0.7f;
    [Header("コンボ設定")]
    [Tooltip("アニメーション間隔変更")]
    [SerializeField,Range(0.1f,1f)]
    public float comboAdvanceTime = 1;
}
