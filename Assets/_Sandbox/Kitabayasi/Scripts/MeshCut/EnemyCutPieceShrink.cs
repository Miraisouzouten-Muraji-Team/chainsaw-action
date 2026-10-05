using UnityEngine;

/// <summary>
/// Enemyのメッシュ分割後に生成された2つの切断片を、
/// 指定時間待機した後、徐々に縮小する。
/// </summary>
/// <remarks>
/// Meshの分割、吹き飛ばし、死亡判定、State遷移、
/// Enemy本体の破棄は担当しない。
///
/// 現在のEnemyMeshCutterが生成する
/// 2つの切断片すべてを同時に縮小する。
/// </remarks>
public sealed class EnemyCutPieceShrink : MonoBehaviour
{
    private Transform firstPieceTransform;
    private Transform secondPieceTransform;

    private Vector3 firstPieceStartScale;
    private Vector3 secondPieceStartScale;

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
    /// 指定された2つの切断片の縮小を開始する。
    /// </summary>
    /// <param name="firstPiece">
    /// 1つ目の切断片。
    /// </param>
    /// <param name="secondPiece">
    /// 2つ目の切断片。
    /// </param>
    /// <param name="delay">
    /// 縮小を開始するまでの待機時間。
    /// </param>
    /// <param name="duration">
    /// 縮小開始からScaleが0になるまでの時間。
    /// </param>
    public bool BeginShrink(
        Transform firstPiece,
        Transform secondPiece,
        float delay,
        float duration)
    {
        if (firstPiece == null ||
            secondPiece == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyCutPieceShrink)}: "
                + "縮小する切断片が設定されていません。",
                this);

            return false;
        }

        StopShrink();

        firstPieceTransform =
            firstPiece;

        secondPieceTransform =
            secondPiece;

        firstPieceStartScale =
            firstPieceTransform.localScale;

        secondPieceStartScale =
            secondPieceTransform.localScale;

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

        firstPieceTransform = null;
        secondPieceTransform = null;

        firstPieceStartScale =
            Vector3.zero;

        secondPieceStartScale =
            Vector3.zero;

        shrinkDelay = 0f;
        shrinkDuration = 0f;
        elapsedTime = 0f;

        enabled = false;
    }

    /// <summary>
    /// 進行度に応じて、
    /// 両方の切断片を初期Scaleから0まで縮小する。
    /// </summary>
    private void ApplyShrinkScale(
        float progress)
    {
        if (firstPieceTransform == null ||
            secondPieceTransform == null)
        {
            return;
        }

        firstPieceTransform.localScale =
            Vector3.Lerp(
                firstPieceStartScale,
                Vector3.zero,
                progress);

        secondPieceTransform.localScale =
            Vector3.Lerp(
                secondPieceStartScale,
                Vector3.zero,
                progress);
    }

    private void CompleteShrink()
    {
        IsShrinkActive = false;

        firstPieceTransform = null;
        secondPieceTransform = null;

        firstPieceStartScale =
            Vector3.zero;

        secondPieceStartScale =
            Vector3.zero;

        shrinkDelay = 0f;
        shrinkDuration = 0f;
        elapsedTime = 0f;

        enabled = false;
    }
}
