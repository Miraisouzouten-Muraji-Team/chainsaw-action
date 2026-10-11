using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Enemyがダメージを受けた際の白黒点滅演出を管理する。
/// </summary>
/// <remarks>
/// 指定されたRendererのMaterialを一時的に白→黒へ切り替え、
/// 点滅終了後に元のMaterialへ戻す。
///
/// 点滅時間はEnemyDataReferenceからEnemyDataを取得し、
/// EnemyDataに設定されたDamageFlashDurationを使用する。
///
/// このクラス自身はダメージ判定やHP管理を行わない。
/// ダメージが成立したタイミングで、外部からPlayDamageFlashを呼び出して使用する。
///
/// 点滅中に再度PlayDamageFlashが呼ばれた場合は、
/// 現在の点滅を停止し、白から点滅を再開始する。
/// </remarks>
[RequireComponent(typeof(EnemyDataReference))]
public class EnemyDamageFlash : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("ダメージ点滅の対象となるRenderer。")]
    [SerializeField]
    private Renderer[] targetRenderers;

    [Tooltip("白点滅時に使用するMaterial。")]
    [SerializeField]
    private Material whiteFlashMaterial;

    [Tooltip("黒点滅時に使用するMaterial。")]
    [SerializeField]
    private Material blackFlashMaterial;

    private EnemyDataReference enemyDataReference;

    private Material[][] originalMaterials;
    private Material[][] whiteFlashMaterials;
    private Material[][] blackFlashMaterials;

    // 実行中の点滅処理をキャンセルするために使用する。
    private CancellationTokenSource flashCts;

    /// <summary>
    /// 元のMaterialを退避済みかを示す。
    /// 点滅中に再度呼ばれた際、白・黒Materialを元Materialとして保存しないために使用する。
    /// </summary>
    private bool hasCapturedOriginalMaterials;

    private void Awake()
    {
        enemyDataReference = GetComponent<EnemyDataReference>();

        InitializeMaterialArrays();
    }

    private void OnDisable()
    {
        StopActiveFlash();
        RestoreOriginalMaterials();
    }

    /// <summary>
    /// ダメージ点滅を開始する。
    /// 点滅中に再度呼ばれた場合は、現在の点滅を停止して白から再開始する。
    /// </summary>
    public void PlayDamageFlash()
    {
        if (!CanPlayFlash())
        {
            return;
        }

        float flashDuration =
            enemyDataReference.Data.DamageFlashDuration;

        // 0秒の場合は点滅演出を行わない。
        if (flashDuration <= 0f)
        {
            return;
        }

        if (!hasCapturedOriginalMaterials)
        {
            CaptureOriginalMaterials();
        }

        // 既に点滅中の場合は古い処理をキャンセルし、
        // 新しい点滅処理へ切り替える。
        StopActiveFlash();

        flashCts = new CancellationTokenSource();

        PlayDamageFlashAsync(
            flashCts,
            flashDuration).Forget();
    }

    /// <summary>
    /// 白→黒→元のMaterialへ戻す点滅処理を時間経過で実行する。
    /// </summary>
    /// <param name="cts">
    /// この点滅処理をキャンセルするためのCancellationTokenSource。
    /// </param>
    /// <param name="flashDuration">
    /// EnemyDataから取得した点滅全体の時間。
    /// </param>
    private async UniTask PlayDamageFlashAsync(
        CancellationTokenSource cts,
        float flashDuration)
    {
        // 全体時間を白表示と黒表示の2区間に分ける。
        float halfDuration = flashDuration * 0.5f;

        ApplyFlashMaterials(whiteFlashMaterials);

        // 元のWaitForSecondsと同様にTime.timeScaleの影響を受ける時間で待機する。
        bool isCanceled = await UniTask.Delay(
                TimeSpan.FromSeconds(halfDuration),
                ignoreTimeScale: false,
                cancellationToken: cts.Token)
            .SuppressCancellationThrow();

        // キャンセルされた処理、または既に新しい点滅処理へ
        // 切り替わっている場合は以降のMaterial変更を行わない。
        if (isCanceled ||
            !ReferenceEquals(flashCts, cts))
        {
            return;
        }

        ApplyFlashMaterials(blackFlashMaterials);

        isCanceled = await UniTask.Delay(
                TimeSpan.FromSeconds(halfDuration),
                ignoreTimeScale: false,
                cancellationToken: cts.Token)
            .SuppressCancellationThrow();

        // キャンセルされた処理、または既に新しい点滅処理へ
        // 切り替わっている場合は元Materialへの復元を行わない。
        if (isCanceled ||
            !ReferenceEquals(flashCts, cts))
        {
            return;
        }

        RestoreOriginalMaterials();

        flashCts = null;
        cts.Dispose();
    }

    /// <summary>
    /// Renderer数と各RendererのMaterial数に合わせて、
    /// Material保存・点滅用の配列を初期化する。
    /// </summary>
    private void InitializeMaterialArrays()
    {
        int rendererCount = targetRenderers != null
            ? targetRenderers.Length
            : 0;

        originalMaterials = new Material[rendererCount][];
        whiteFlashMaterials = new Material[rendererCount][];
        blackFlashMaterials = new Material[rendererCount][];

        for (int i = 0; i < rendererCount; i++)
        {
            Renderer targetRenderer = targetRenderers[i];

            if (targetRenderer == null)
            {
                continue;
            }

            int materialCount =
                targetRenderer.sharedMaterials.Length;

            whiteFlashMaterials[i] =
                CreateFlashMaterialArray(
                    materialCount,
                    whiteFlashMaterial);

            blackFlashMaterials[i] =
                CreateFlashMaterialArray(
                    materialCount,
                    blackFlashMaterial);
        }
    }

    /// <summary>
    /// 点滅開始前のMaterial構成を保存する。
    /// 点滅終了後に元の見た目へ戻すために使用する。
    /// </summary>
    private void CaptureOriginalMaterials()
    {
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];

            if (targetRenderer == null)
            {
                continue;
            }

            Material[] currentMaterials =
                targetRenderer.sharedMaterials;

            originalMaterials[i] = currentMaterials;

            if (whiteFlashMaterials[i] == null ||
                whiteFlashMaterials[i].Length != currentMaterials.Length)
            {
                whiteFlashMaterials[i] =
                    CreateFlashMaterialArray(
                        currentMaterials.Length,
                        whiteFlashMaterial);

                blackFlashMaterials[i] =
                    CreateFlashMaterialArray(
                        currentMaterials.Length,
                        blackFlashMaterial);
            }
        }

        hasCapturedOriginalMaterials = true;
    }

    /// <summary>
    /// 指定された点滅用Material配列を対象Rendererへ適用する。
    /// </summary>
    /// <param name="flashMaterials">
    /// 各Rendererへ適用するMaterial配列。
    /// </param>
    private void ApplyFlashMaterials(
        Material[][] flashMaterials)
    {
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];

            if (targetRenderer == null ||
                flashMaterials[i] == null)
            {
                continue;
            }

            targetRenderer.sharedMaterials =
                flashMaterials[i];
        }
    }

    /// <summary>
    /// 点滅前に保存したMaterial構成を各Rendererへ戻す。
    /// </summary>
    private void RestoreOriginalMaterials()
    {
        if (!hasCapturedOriginalMaterials)
        {
            return;
        }

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];

            if (targetRenderer == null ||
                originalMaterials[i] == null)
            {
                continue;
            }

            targetRenderer.sharedMaterials =
                originalMaterials[i];

            originalMaterials[i] = null;
        }

        hasCapturedOriginalMaterials = false;
    }

    /// <summary>
    /// 実行中の点滅処理があればキャンセルする。
    /// </summary>
    private void StopActiveFlash()
    {
        if (flashCts == null)
        {
            return;
        }

        flashCts.Cancel();
        flashCts.Dispose();
        flashCts = null;
    }

    /// <summary>
    /// 点滅処理を実行するために必要な設定が揃っているか確認する。
    /// </summary>
    /// <returns>
    /// 点滅可能ならtrue、それ以外はfalse。
    /// </returns>
    private bool CanPlayFlash()
    {
        if (enemyDataReference == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDamageFlash)}: " +
                $"{nameof(EnemyDataReference)} が見つかりません。",
                this);

            return false;
        }

        if (enemyDataReference.Data == null)
        {
            Debug.LogError(
                $"{nameof(EnemyDamageFlash)}: " +
                $"{nameof(EnemyDataReference)} に " +
                $"{nameof(EnemyData)} が設定されていません。",
                this);

            return false;
        }

        if (targetRenderers == null ||
            targetRenderers.Length == 0)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDamageFlash)}: " +
                "点滅対象のRendererが設定されていません。",
                this);

            return false;
        }

        if (whiteFlashMaterial == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDamageFlash)}: " +
                "白点滅用Materialが設定されていません。",
                this);

            return false;
        }

        if (blackFlashMaterial == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDamageFlash)}: " +
                "黒点滅用Materialが設定されていません。",
                this);

            return false;
        }

        return true;
    }

    /// <summary>
    /// 指定されたMaterialを必要なスロット数だけ格納した配列を生成する。
    /// </summary>
    /// <param name="materialCount">
    /// Rendererが使用しているMaterialスロット数。
    /// </param>
    /// <param name="flashMaterial">
    /// 各スロットへ設定する点滅用Material。
    /// </param>
    /// <returns>
    /// 指定Materialで埋めたMaterial配列。
    /// </returns>
    private Material[] CreateFlashMaterialArray(
        int materialCount,
        Material flashMaterial)
    {
        Material[] materials =
            new Material[materialCount];

        for (int i = 0; i < materialCount; i++)
        {
            materials[i] = flashMaterial;
        }

        return materials;
    }
}
