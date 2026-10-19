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

    [Header("移動スティックの遊び")]
    [Tooltip("横方向の移動入力を無視する範囲です。0.2なら左右とも入力の大きさが0.2以下では移動・向き変更しません。範囲を超えた入力は0～1へつなぎ直します。0なら遊びを追加しません。")]
    [SerializeField, Range(0f, 0.95f)]
    public float moveDeadZone = 0.2f;

    [Header("ジャンプ・重力")]
    [Tooltip("ジャンプ力")]
    [SerializeField]
    public float jumpForce = 10f;
    [Tooltip("二段ジャンプ力")]
    [SerializeField]
    public float airJumpForce = 10f;
    [Tooltip("コヨーテタイム")]
    [SerializeField, Min(0f)]
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

    [Header("ジャンプ：3本Ray接地判定")]
    [Tooltip("接地を判定する地形レイヤー。Nothingなら食い込み衝突判定の地形レイヤーを使用します。")]
    public LayerMask groundRayLayers;
    [Tooltip("足元中央のTransformから、前と後ろのRayを横方向にずらす距離です。")]
    [Min(0f)] public float groundRaySideOffset = 0.35f;
    [Tooltip("Ground Check Transformの位置からRayの開始位置を上へずらす距離です。")]
    [Min(0.01f)] public float groundRayStartHeight = 0.1f;
    [Tooltip("足元から下向きに地面を探すRayの長さ。開始位置からの距離です。")]
    [Min(0.01f)] public float groundRayDistance = 0.25f;
    [Tooltip("床として認める面の法線Yの下限です。0.7で約45.6度、0.5で60度まで。Controller・壁停止・食い込みで共通使用します。")]
    [Range(0.1f, 1f)] public float groundRayNormalThreshold = 0.7f;
    [Tooltip("Rayが床を検出した際、足元から床までの距離がこの値以下なら接地確定します。Rayの探索距離とは別の値です。")]
    [Min(0f)] public float groundRayContactTolerance = 0.03f;

    [Header("斜面への追従")]
    [Tooltip("接地直後の短い下り追従距離です。本体の形状で下方向を調べます。大きすぎると小さな段差を降りても接地を維持します。")]
    [Min(0f)] public float slopeGroundSnapDistance = 0.1f;
    [Tooltip("通常歩行中に斜面へ押し付ける速度です。食い込み中の押し付けとは別です。")]
    [Min(0f)] public float slopeGroundStickSpeed = 1f;
    [Tooltip("本体形状で前方の坂を先読みする最大距離です。実際には1物理更新分の移動距離と小さな余白までに制限します。")]
    [Min(0f)] public float slopeGroundProbeDistance = 0.2f;

    [Header("壁衝突時のパラメータ")]
    // 壁衝突時のパラメータ
    [Tooltip("ノックバックスピード")]
    [SerializeField, Min(0f)]
    public float wallKnockbackSpeed = 5f;
    [Tooltip("ノックバックでの減速")]
    [SerializeField, Min(0.01f)]
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
    [Tooltip("壁へ直接食い込んだとき、回転速度をこの値で割って壁登りの基本速度へ加えます。小さいほど加速が大きくなります。床から壁へ移るときの速度引き継ぎには使用しません。")]
    [SerializeField, Min(0.01f)]
    public float directWallSpeedDivisor = 100f;
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
    [SerializeField, Range(0.1f, 1f)]
    public float comboAdvanceTime = 1;
}
