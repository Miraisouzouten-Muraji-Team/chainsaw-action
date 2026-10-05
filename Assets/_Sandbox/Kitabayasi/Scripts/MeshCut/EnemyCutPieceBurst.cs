using UnityEngine;

/// <summary>
/// Enemyのメッシュ分割後に生成された2つの切断片を、
/// 互いに離れる方向へ吹き飛ばす。
/// </summary>
/// <remarks>
/// Meshの分割、死亡判定、State遷移、
/// Enemy本体の破棄は担当しない。
///
/// 現段階では分割状態を視認できることを目的として、
/// Rigidbodyを使用せずTransformを直接移動させる。
/// </remarks>
public sealed class EnemyCutPieceBurst : MonoBehaviour
{
    private const float DIRECTION_EPSILON_SQR =
        0.000001f;

    [Header("吹き飛ばし")]

    [Tooltip(
        "2つの切断片を合わせて離す距離。"
        + "各切断片はこの半分ずつ逆方向へ移動する。")]
    [SerializeField]
    [Min(0f)]
    private float burstDistance = 1.5f;

    [Tooltip(
        "切断片が指定距離まで移動する時間。"
        + "0の場合は即座に移動する。")]
    [SerializeField]
    [Min(0f)]
    private float burstDuration = 0.2f;

    private Transform firstPieceTransform;
    private Transform secondPieceTransform;

    private Vector3 firstPieceStartPosition;
    private Vector3 secondPieceStartPosition;

    private Vector3 burstDirection;

    private float elapsedTime;

    public bool IsBursting { get; private set; }

    private void Awake()
    {
        // 吹き飛ばし開始までは毎フレーム処理を行わない。
        enabled = false;
    }

    /// <summary>
    /// 指定された2つの切断片の吹き飛ばしを開始する。
    /// </summary>
    public bool BeginBurst(
        Transform firstPiece,
        Transform secondPiece)
    {
        if (firstPiece == null ||
            secondPiece == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: "
                + "吹き飛ばす切断片が設定されていません。",
                this);

            return false;
        }

        Renderer firstRenderer =
            firstPiece.GetComponent<Renderer>();

        Renderer secondRenderer =
            secondPiece.GetComponent<Renderer>();

        if (firstRenderer == null ||
            secondRenderer == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceBurst)}: "
                + "切断片にRendererがありません。"
                + "吹き飛ばし方向を決定できません。",
                this);

            return false;
        }

        StopBurst();

        Vector3 direction =
            firstRenderer.bounds.center -
            secondRenderer.bounds.center;

        // 現在のゲームプレイ平面はX/Yなので、
        // 確認用の吹き飛ばしでは奥行き方向へ移動させない。
        direction.z = 0f;

        if (direction.sqrMagnitude <=
            DIRECTION_EPSILON_SQR)
        {
            // X/Y平面上で中心位置がほぼ一致する場合でも、
            // 分割状態を確認できるよう上下方向へ離す。
            direction =
                Vector3.up;
        }
        else
        {
            direction.Normalize();
        }

        firstPieceTransform =
            firstPiece;

        secondPieceTransform =
            secondPiece;

        firstPieceStartPosition =
            firstPieceTransform.position;

        secondPieceStartPosition =
            secondPieceTransform.position;

        burstDirection =
            direction;

        elapsedTime = 0f;
        IsBursting = true;

        if (burstDistance <= 0f ||
            burstDuration <= 0f)
        {
            ApplyBurstPosition(1f);
            CompleteBurst();

            return true;
        }

        enabled = true;

        ApplyBurstPosition(0f);

        return true;
    }

    private void Update()
    {
        if (!IsBursting)
        {
            return;
        }

        elapsedTime +=
            Time.deltaTime;

        float progress =
            Mathf.Clamp01(
                elapsedTime /
                burstDuration);

        ApplyBurstPosition(
            progress);

        if (progress >= 1f)
        {
            CompleteBurst();
        }
    }

    /// <summary>
    /// 現在の吹き飛ばしを停止する。
    /// </summary>
    public void StopBurst()
    {
        IsBursting = false;

        firstPieceTransform = null;
        secondPieceTransform = null;

        burstDirection =
            Vector3.zero;

        elapsedTime = 0f;

        enabled = false;
    }

    /// <summary>
    /// 進行度に応じた切断片の位置を設定する。
    /// </summary>
    private void ApplyBurstPosition(
        float progress)
    {
        if (firstPieceTransform == null ||
            secondPieceTransform == null)
        {
            return;
        }

        float pieceDistance =
            burstDistance *
            0.5f;

        Vector3 offset =
            burstDirection *
            pieceDistance *
            progress;

        firstPieceTransform.position =
            firstPieceStartPosition +
            offset;

        secondPieceTransform.position =
            secondPieceStartPosition -
            offset;
    }

    private void CompleteBurst()
    {
        IsBursting = false;

        firstPieceTransform = null;
        secondPieceTransform = null;

        burstDirection =
            Vector3.zero;

        elapsedTime = 0f;

        enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        burstDistance =
            Mathf.Max(
                0f,
                burstDistance);

        burstDuration =
            Mathf.Max(
                0f,
                burstDuration);
    }
#endif
}
