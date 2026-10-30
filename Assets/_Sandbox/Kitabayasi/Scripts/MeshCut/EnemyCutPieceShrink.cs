using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemyのメッシュ分割後に生成された全切断片を、
/// 指定時間待機した後、徐々に縮小する。
/// </summary>
/// <remarks>
/// Meshの分割、吹き飛ばし、死亡判定、State遷移、
/// Enemy本体の破棄は担当しない。
/// </remarks>
public sealed class EnemyCutPieceShrink : MonoBehaviour
{

    private readonly List<Transform> pieceTransforms =
        new List<Transform>();

    private readonly List<Vector3> pieceStartScales =
        new List<Vector3>();

    private float shrinkDelay;
    private float shrinkDuration;
    private float elapsedTime;

    /// <summary>
    /// 縮小開始待機を含めた、
    /// 縮小シーケンス全体が進行中かどうか。
    /// </summary>
    public bool IsShrinkActive { get; private set; }

    private void Awake()
    {
        // 縮小開始までは毎フレーム処理を行わない。
        enabled = false;
    }

    /// <summary>
    /// 指定された全切断片の縮小を開始する。
    /// </summary>
    /// <param name="pieces">
    /// EnemyMeshCutterが生成した現在有効な切断片。
    /// </param>
    /// <param name="delay">
    /// 縮小を開始するまでの待機時間。
    /// </param>
    /// <param name="duration">
    /// 縮小開始からScaleが0になるまでの時間。
    /// </param>
    public bool BeginShrink(
        IReadOnlyList<Transform> pieces,
        float delay,
        float duration)
    {
        if (pieces == null ||
            pieces.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceShrink)}: "
                + "縮小する切断片がありません。",
                this);

            return false;
        }

        StopShrink();

        for (int i = 0;
             i < pieces.Count;
             i++)
        {
            Transform piece =
                pieces[i];

            if (piece == null)
            {
                continue;
            }

            pieceTransforms.Add(
                piece);

            pieceStartScales.Add(
                piece.localScale);
        }

        if (pieceTransforms.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceShrink)}: "
                + "有効な切断片Transformがありません。",
                this);

            return false;
        }

        shrinkDelay =
            Mathf.Max(
                0f,
                delay);

        shrinkDuration =
            Mathf.Max(
                0f,
                duration);

        elapsedTime = 0f;
        IsShrinkActive = true;

        ApplyShrinkScale(0f);

        if (shrinkDelay <= 0f &&
            shrinkDuration <= 0f)
        {
            ApplyShrinkScale(1f);
            CompleteShrink();

            return true;
        }

        enabled = true;

        return true;
    
    }

    private void Update()
    {
        if (!IsShrinkActive)
        {
            return;
        }

        elapsedTime +=
            Time.deltaTime;

        if (elapsedTime <
            shrinkDelay)
        {
            return;
        }

        if (shrinkDuration <= 0f)
        {
            ApplyShrinkScale(1f);
            CompleteShrink();

            return;
        }

        float shrinkElapsedTime =
            elapsedTime -
            shrinkDelay;

        float progress =
            Mathf.Clamp01(
                shrinkElapsedTime /
                shrinkDuration);

        ApplyShrinkScale(
            progress);

        if (progress >= 1f)
        {
            CompleteShrink();
        }
    }

    /// <summary>
    /// 現在の縮小処理を停止する。
    /// </summary>
    public void StopShrink()
    {
        IsShrinkActive = false;

        pieceTransforms.Clear();
        pieceStartScales.Clear();

        shrinkDelay = 0f;
        shrinkDuration = 0f;
        elapsedTime = 0f;

        enabled = false;
    }

    /// <summary>
    /// 進行度に応じて、全切断片を初期Scaleから0まで縮小する。
    /// </summary>
    private void ApplyShrinkScale(
        float progress)
    {
        for (int i = 0;
             i < pieceTransforms.Count;
             i++)
        {
            Transform piece =
                pieceTransforms[i];

            if (piece == null)
            {
                continue;
            }

            piece.localScale =
                Vector3.Lerp(
                    pieceStartScales[i],
                    Vector3.zero,
                    progress);
        }
    }

    private void CompleteShrink()
    {
        IsShrinkActive = false;

        pieceTransforms.Clear();
        pieceStartScales.Clear();

        shrinkDelay = 0f;
        shrinkDuration = 0f;
        elapsedTime = 0f;

        enabled = false;
    }
}
