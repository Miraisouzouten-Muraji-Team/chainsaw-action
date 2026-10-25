using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// くい込み攻撃死亡で生成されたPieceを、腰付近の爆発中心から放射状に飛散させる。
/// </summary>
/// <remarks>
/// 上側Pieceには放射方向のImpulseと回転Torqueを与え、
/// 下側Pieceには地面方向を強めたImpulseを与えて初期回転を残さない。
/// Mesh分割、Piece生成、死亡原因判定、AttackVector、チェンソー回転数、
/// 通常攻撃死亡の飛散は担当しない。
/// </remarks>
public sealed class EnemyDiggingDeathBurst : MonoBehaviour
{
    private const float DIRECTION_EPSILON_SQR = 0.000001f;
    private const float MAX_DIRECTION_SPREAD = 90f;

    [Header("爆発中心")]

    [Tooltip(
        "くい込み死亡Pieceを放射状に飛ばす中心。" +
        "Enemyの腰付近に配置したTransformを設定する。")]
    [SerializeField]
    private Transform burstOrigin;

    [Header("上側Piece")]

    [Tooltip("爆発中心より上側のPieceへ与える放射状Impulse強度。")]
    [SerializeField]
    [Min(0f)]
    private float upperBurstForce = 8f;

    [Tooltip(
        "上側Pieceへ与える回転Impulse強度。" +
        "現在のPiece物理設定で許可されているゲームプレイ平面の法線軸まわりへ適用する。")]
    [SerializeField]
    [Min(0f)]
    private float upperTorqueStrength = 0.3f;

    [Header("下側Piece")]

    [Tooltip("爆発中心より下側のPieceへ与えるImpulse強度。")]
    [SerializeField]
    [Min(0f)]
    private float lowerBurstForce = 10f;

    [Tooltip(
        "下側Pieceの放射方向へ追加する下向き成分。" +
        "0なら純粋な放射方向、値を大きくするほど地面方向が強くなる。")]
    [SerializeField]
    [Min(0f)]
    private float lowerDownwardBias = 1f;

    [Header("方向ばらつき")]

    [Tooltip(
        "Pieceごとの放射方向へゲームプレイ平面内で加える最大角度のばらつき。単位は度。")]
    [SerializeField]
    [Range(0f, MAX_DIRECTION_SPREAD)]
    private float directionSpread = 10f;

    /// <summary>
    /// くい込み死亡で生成されたPieceへ、爆発中心基準の飛散Impulseを適用する。
    /// </summary>
    /// <param name="pieces">
    /// EnemyDiggingMeshDeathEffectが今回の死亡演出で生成したPiece。
    /// </param>
    public bool BeginBurst(
        IReadOnlyList<Transform> pieces)
    {
        if (burstOrigin == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingDeathBurst)}: " +
                "Burst Originが設定されていないため、" +
                "くい込み死亡Pieceを飛散させられません。",
                this);

            return false;
        }

        if (pieces == null ||
            pieces.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingDeathBurst)}: " +
                "飛散対象Pieceがありません。",
                this);

            return false;
        }

        Vector3 gameplayPlaneNormal =
            Vector3.forward;

        Vector3 gameUpDirection =
            Vector3.ProjectOnPlane(
                transform.up,
                gameplayPlaneNormal);

        if (gameUpDirection.sqrMagnitude <=
            DIRECTION_EPSILON_SQR)
        {
            gameUpDirection =
                Vector3.up;
        }
        else
        {
            gameUpDirection.Normalize();
        }

        Vector3 originPosition =
            burstOrigin.position;

        int appliedPieceCount = 0;

        for (int pieceIndex = 0;
             pieceIndex < pieces.Count;
             pieceIndex++)
        {
            Transform piece =
                pieces[pieceIndex];

            if (piece == null)
            {
                continue;
            }

            Rigidbody pieceRigidbody =
                piece.GetComponent<Rigidbody>();

            if (pieceRigidbody == null ||
                pieceRigidbody.isKinematic)
            {
                Debug.LogWarning(
                    $"{nameof(EnemyDiggingDeathBurst)}: " +
                    $"Piece '{piece.name}' に有効なRigidbodyがないため、" +
                    "飛散を適用できません。",
                    piece);

                continue;
            }

            if (!TryGetPieceWorldCenter(
                    piece,
                    out Vector3 pieceCenter))
            {
                Debug.LogWarning(
                    $"{nameof(EnemyDiggingDeathBurst)}: " +
                    $"Piece '{piece.name}' の中心位置を取得できないため、" +
                    "飛散を適用できません。",
                    piece);

                continue;
            }

            Vector3 originToPiece =
                Vector3.ProjectOnPlane(
                    pieceCenter - originPosition,
                    gameplayPlaneNormal);

            float verticalRelation =
                Vector3.Dot(
                    originToPiece,
                    gameUpDirection);

            bool isUpperPiece =
                verticalRelation >= 0f;

            Vector3 radialDirection =
                ResolveRadialDirection(
                    originToPiece,
                    isUpperPiece,
                    gameUpDirection);

            Vector3 spreadDirection =
                ApplyDirectionSpread(
                    radialDirection,
                    gameplayPlaneNormal);

            if (isUpperPiece)
            {
                ApplyUpperBurst(
                    pieceRigidbody,
                    spreadDirection,
                    gameUpDirection,
                    gameplayPlaneNormal);
            }
            else
            {
                ApplyLowerBurst(
                    pieceRigidbody,
                    spreadDirection,
                    gameUpDirection);
            }

            appliedPieceCount++;
        }

        if (appliedPieceCount > 0)
        {
            return true;
        }

        Debug.LogWarning(
            $"{nameof(EnemyDiggingDeathBurst)}: " +
            "飛散を適用できるPieceがありませんでした。",
            this);

        return false;
    }

    private void ApplyUpperBurst(
        Rigidbody pieceRigidbody,
        Vector3 burstDirection,
        Vector3 gameUpDirection,
        Vector3 gameplayPlaneNormal)
    {
        float upwardComponent =
            Vector3.Dot(
                burstDirection,
                gameUpDirection);

        // 上側PieceがDirection Spreadによって下向きへ反転しないよう、
        // 下向き成分だけを除去して「上・外側」の飛散意図を維持する。
        if (upwardComponent < 0f)
        {
            burstDirection -=
                gameUpDirection *
                upwardComponent;

            burstDirection =
                ResolveFallbackDirection(
                    burstDirection,
                    gameUpDirection);
        }

        float resolvedBurstForce =
            Mathf.Max(
                0f,
                upperBurstForce);

        if (resolvedBurstForce > 0f)
        {
            pieceRigidbody.AddForce(
                burstDirection * resolvedBurstForce,
                ForceMode.Impulse);
        }

        float resolvedTorqueStrength =
            Mathf.Max(
                0f,
                upperTorqueStrength);

        if (resolvedTorqueStrength <= 0f)
        {
            return;
        }

        float torqueDirectionSign =
            Random.value < 0.5f
                ? -1f
                : 1f;

        pieceRigidbody.AddTorque(
            gameplayPlaneNormal *
            (resolvedTorqueStrength * torqueDirectionSign),
            ForceMode.Impulse);
    }

    private void ApplyLowerBurst(
        Rigidbody pieceRigidbody,
        Vector3 burstDirection,
        Vector3 gameUpDirection)
    {
        // 新規生成Rigidbodyでも、今後Piece生成方法が変わった場合に初期回転が混入しないよう
        // 下側Pieceは飛散開始時点で明示的に角速度を消す。
        pieceRigidbody.angularVelocity =
            Vector3.zero;

        Vector3 downwardDirection =
            -gameUpDirection;

        Vector3 biasedDirection =
            burstDirection +
            downwardDirection *
            Mathf.Max(
                0f,
                lowerDownwardBias);

        biasedDirection =
            ResolveFallbackDirection(
                biasedDirection,
                downwardDirection);

        float downwardComponent =
            Vector3.Dot(
                biasedDirection,
                gameUpDirection);

        if (downwardComponent > 0f)
        {
            biasedDirection -=
                gameUpDirection *
                downwardComponent;

            biasedDirection =
                ResolveFallbackDirection(
                    biasedDirection,
                    downwardDirection);
        }

        float resolvedBurstForce =
            Mathf.Max(
                0f,
                lowerBurstForce);

        if (resolvedBurstForce <= 0f)
        {
            return;
        }

        pieceRigidbody.AddForce(
            biasedDirection * resolvedBurstForce,
            ForceMode.Impulse);
    }

    private Vector3 ApplyDirectionSpread(
        Vector3 direction,
        Vector3 gameplayPlaneNormal)
    {
        float resolvedSpread =
            Mathf.Clamp(
                directionSpread,
                0f,
                MAX_DIRECTION_SPREAD);

        if (resolvedSpread <= 0f)
        {
            return direction;
        }

        float spreadAngle =
            Random.Range(
                -resolvedSpread,
                resolvedSpread);

        return
            Quaternion.AngleAxis(
                spreadAngle,
                gameplayPlaneNormal) *
            direction;
    }

    private static Vector3 ResolveRadialDirection(
        Vector3 originToPiece,
        bool isUpperPiece,
        Vector3 gameUpDirection)
    {
        if (originToPiece.sqrMagnitude >
            DIRECTION_EPSILON_SQR)
        {
            return originToPiece.normalized;
        }

        return isUpperPiece
            ? gameUpDirection
            : -gameUpDirection;
    }

    private static Vector3 ResolveFallbackDirection(
        Vector3 direction,
        Vector3 fallbackDirection)
    {
        if (direction.sqrMagnitude >
            DIRECTION_EPSILON_SQR)
        {
            return direction.normalized;
        }

        return fallbackDirection.normalized;
    }

    private static bool TryGetPieceWorldCenter(
        Transform piece,
        out Vector3 pieceCenter)
    {
        Renderer pieceRenderer =
            piece.GetComponent<Renderer>();

        if (pieceRenderer != null)
        {
            pieceCenter =
                pieceRenderer.bounds.center;

            return true;
        }

        Collider pieceCollider =
            piece.GetComponent<Collider>();

        if (pieceCollider != null)
        {
            pieceCenter =
                pieceCollider.bounds.center;

            return true;
        }

        pieceCenter = default;
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        upperBurstForce =
            Mathf.Max(
                0f,
                upperBurstForce);

        upperTorqueStrength =
            Mathf.Max(
                0f,
                upperTorqueStrength);

        lowerBurstForce =
            Mathf.Max(
                0f,
                lowerBurstForce);

        lowerDownwardBias =
            Mathf.Max(
                0f,
                lowerDownwardBias);

        directionSpread =
            Mathf.Clamp(
                directionSpread,
                0f,
                MAX_DIRECTION_SPREAD);
    }
#endif
}
