using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Enemyのメッシュ分割後に生成された全切断片へ、
/// 死亡させた攻撃のノックバック方向を使って物理的な初速と回転を与える。
/// </summary>
/// <remarks>
/// 全切断片は同じゲームプレイ平面上の方向へ飛ばす。
/// 飛散速度は全切断片で共通値を使用し、回転速度は死亡攻撃時のチェンソー回転率から共通値を決定する。
/// Mesh分割、Rigidbody生成、死亡判定、State遷移、縮小、Enemy本体の破棄は担当しない。
/// </remarks>
public sealed class EnemyCutPieceBurst : MonoBehaviour
{
    private const float DIRECTION_EPSILON_SQR =
        0.000001f;

    [Header("吹き飛ばし")]

    [Tooltip(
        "死亡させた攻撃のノックバック方向へ与える初速。" +
        "現在は全切断片で同じ値を使用する。")]
    [SerializeField]
    [Min(0f)]
    private float burstSpeed = 8f;

    [Header("回転")]

    [Tooltip(
        "チェンソーが最低回転のときに全切断片へ与えるZ軸回転速度（度/秒）。" +
        "低回転時でも着地までに姿勢が変化しやすいよう、初期値は180度/秒とする。")]
    [SerializeField]
    [Min(0f)]
    private float minimumRotationSpeedDegreesPerSecond = 180f;

    [Tooltip(
        "チェンソーが最大回転のときに全切断片へ与えるZ軸回転速度（度/秒）。")]
    [SerializeField]
    [Min(0f)]
    [FormerlySerializedAs("rotationSpeedDegreesPerSecond")]
    private float maximumRotationSpeedDegreesPerSecond = 360f;

    /// <summary>
    /// 指定された全切断片を、死亡攻撃の実ノックバック方向へ吹き飛ばす。
    /// </summary>
    /// <param name="cutPieces">
    /// EnemyMeshCutterが今回のMesh分割で生成した全切断片。
    /// </param>
    /// <param name="resolvedKnockbackVelocity">
    /// 攻撃元の左右判定まで反映済みのX/Yノックバック初速度。
    /// 強さは使用せず、吹き飛ばす方向だけに使用する。
    /// </param>
    /// <param name="chainsawRotationRatio">
    /// 死亡攻撃が命中した瞬間のチェンソー回転率。最低回転を0、最大回転を1とする。
    /// </param>
    public bool BeginBurst(
        IReadOnlyList<Transform> cutPieces,
        Vector2 resolvedKnockbackVelocity,
        float chainsawRotationRatio)
    {
        if (cutPieces == null ||
            cutPieces.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "吹き飛ばす切断片がありません。",
                this);

            return false;
        }

        Vector3 burstDirection =
            new Vector3(
                resolvedKnockbackVelocity.x,
                resolvedKnockbackVelocity.y,
                0f);

        if (burstDirection.sqrMagnitude <=
            DIRECTION_EPSILON_SQR)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "死亡攻撃のノックバックベクトルが0のため、" +
                "切断片の吹き飛ばし方向を決定できません。",
                this);

            return false;
        }

        burstDirection.Normalize();

        Vector3 velocityChange =
            burstDirection * burstSpeed;

        float minimumRotationSpeed =
            Mathf.Max(
                0f,
                minimumRotationSpeedDegreesPerSecond);

        float maximumRotationSpeed =
            Mathf.Max(
                minimumRotationSpeed,
                maximumRotationSpeedDegreesPerSecond);

        float resolvedRotationSpeedDegreesPerSecond =
            Mathf.Lerp(
                minimumRotationSpeed,
                maximumRotationSpeed,
                Mathf.Clamp01(
                    chainsawRotationRatio));

        // ForceMode.VelocityChangeのAddTorqueは角速度変化をrad/sで受け取る。
        // Inspectorでは演出調整しやすい度/秒で持ち、物理へ渡す直前に変換する。
        Vector3 angularVelocityChange =
            Vector3.forward *
            (resolvedRotationSpeedDegreesPerSecond *
             Mathf.Deg2Rad);

        int appliedPieceCount = 0;

        for (int i = 0;
             i < cutPieces.Count;
             i++)
        {
            Transform cutPiece =
                cutPieces[i];

            if (cutPiece == null)
            {
                continue;
            }

            Rigidbody pieceRigidbody =
                cutPiece.GetComponent<Rigidbody>();

            if (pieceRigidbody == null ||
                pieceRigidbody.isKinematic)
            {
                Debug.LogWarning(
                    $"{nameof(EnemyCutPieceBurst)}: " +
                    $"切断片 '{cutPiece.name}' に有効なRigidbodyがないため、" +
                    "吹き飛ばしを適用できません。",
                    cutPiece);

                continue;
            }

            // 生成直後の各切断片へ1回だけ速度変化を与え、
            // 以降の重力・衝突・減衰はUnity Physicsへ任せる。
            if (burstSpeed > 0f)
            {
                pieceRigidbody.AddForce(
                    velocityChange,
                    ForceMode.VelocityChange);
            }

            if (resolvedRotationSpeedDegreesPerSecond > 0f)
            {
                pieceRigidbody.AddTorque(
                    angularVelocityChange,
                    ForceMode.VelocityChange);
            }

            appliedPieceCount++;
        }

        if (appliedPieceCount == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: " +
                "吹き飛ばしを適用できる切断片がありませんでした。",
                this);

            return false;
        }

        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        burstSpeed =
            Mathf.Max(
                0f,
                burstSpeed);

        minimumRotationSpeedDegreesPerSecond =
            Mathf.Max(
                0f,
                minimumRotationSpeedDegreesPerSecond);

        maximumRotationSpeedDegreesPerSecond =
            Mathf.Max(
                minimumRotationSpeedDegreesPerSecond,
                maximumRotationSpeedDegreesPerSecond);
    }
#endif
}
