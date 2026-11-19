using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 食い込み攻撃で死亡したEnemyの分割Pieceを、ゲームプレイ平面内のランダムな方向へ飛ばす。
/// </summary>
/// <remarks>
/// 分割・Piece生成・死亡判定は担当しない。
/// ステージへ向けた初速で再び地面に潜ることを避けるため、初期飛散方向は水平から上向きの範囲に限定する。
/// </remarks>
public sealed class EnemyDiggingDeathBurst : MonoBehaviour
{
    [Header("吹き飛ばし")]
    [Tooltip("全Pieceへ与える飛散初速度の大きさ（m/s）。左右・上方向へランダムに飛ばす。")]
    [SerializeField]
    [Min(0f)]
    private float burstSpeed = 8f;

    /// <summary>
    /// 今回の食い込み死亡で生成した各Pieceへ、それぞれ異なるランダム方向の初速度を与える。
    /// </summary>
    public bool BeginBurst(IReadOnlyList<Transform> pieces)
    {
        if (pieces == null || pieces.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingDeathBurst)}: 飛散対象Pieceがありません。",
                this);
            return false;
        }

        float resolvedBurstSpeed = Mathf.Max(0f, burstSpeed);
        int appliedPieceCount = 0;

        for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
        {
            Transform piece = pieces[pieceIndex];
            if (piece == null)
            {
                continue;
            }

            Rigidbody pieceRigidbody = piece.GetComponent<Rigidbody>();
            if (pieceRigidbody == null || pieceRigidbody.isKinematic)
            {
                Debug.LogWarning(
                    $"{nameof(EnemyDiggingDeathBurst)}: " +
                    $"Piece '{piece.name}' に有効なRigidbodyがないため飛散できません。",
                    piece);
                continue;
            }

            // 現在の分割PieceはXY平面を移動し、Z方向を固定する。
            // 下向きの初速を避けながら、左右どちらへ飛ぶかはPieceごとにランダムに決める。
            float randomAngle = Random.Range(0f, Mathf.PI);
            Vector3 burstDirection = new Vector3(
                Mathf.Cos(randomAngle),
                Mathf.Sin(randomAngle),
                0f);

            // 質量差で飛散量が大きく変わらないよう、Impulseではなく速度変化を与える。
            if (resolvedBurstSpeed > 0f)
            {
                pieceRigidbody.AddForce(
                    burstDirection * resolvedBurstSpeed,
                    ForceMode.VelocityChange);
            }

            appliedPieceCount++;
        }

        if (appliedPieceCount > 0)
        {
            return true;
        }

        Debug.LogWarning(
            $"{nameof(EnemyDiggingDeathBurst)}: 飛散できるPieceがありませんでした。",
            this);
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        burstSpeed = Mathf.Max(0f, burstSpeed);
    }
#endif
}
