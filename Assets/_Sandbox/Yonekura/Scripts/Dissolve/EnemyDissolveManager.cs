using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 敵のディゾルブ出現演出を一括管理するクラス。
/// </summary>
public class EnemyDissolveManager : MonoBehaviour
{
    // Shader GraphのReferenceと同じ名前を指定する。
    private static readonly int DissolveAmountId =
        Shader.PropertyToID("_DissolveAmount");

    private static readonly int NoiseOffsetId =
        Shader.PropertyToID("_NoiseOffset");

    // シーン内のEnemyDissolveManagerを1つだけ管理する。
    private static EnemyDissolveManager instance;

    public static EnemyDissolveManager Instance => instance;

    // Managerの生成時に重複を確認し、インスタンスを登録する。
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError(
                "EnemyDissolveManagerが複数存在しています。",
                this);

            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    // Managerの破棄時に登録したインスタンスを解除する。
    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// 敵のディゾルブ出現演出を再生する。
    /// </summary>
    public async UniTask PlayAppearAsync(
        GameObject enemy,
        Material dissolveMaterial,
        float duration)
    {
        // 敵が存在しない場合は処理しない。
        if (enemy == null)
        {
            return;
        }

        // 演出用マテリアルが未設定の場合は処理しない。
        if (dissolveMaterial == null)
        {
            Debug.LogError("ディゾルブマテリアルが未設定です。");
            return;
        }

        // 出現中はAIを停止する。
        EnemyStateMachine stateMachine =
            enemy.GetComponent<EnemyStateMachine>();

        if (stateMachine != null)
        {
            stateMachine.enabled = false;
        }

        // 敵本体と子オブジェクトのRendererを取得する。
        Renderer[] renderers =
            enemy.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            Debug.LogWarning(
                $"{enemy.name}: Rendererが見つかりません。",
                enemy);

            if (stateMachine != null)
            {
                stateMachine.enabled = true;
            }

            return;
        }

        // 元のマテリアルをRendererごとに保存する。
        Material[][] originalMaterials =
            new Material[renderers.Length][];

        // 元のMaterialPropertyBlockを保存する。
        MaterialPropertyBlock[] originalPropertyBlocks =
            new MaterialPropertyBlock[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            // マテリアルのスロット構成も含めて保存する。
            originalMaterials[i] =
                renderers[i].sharedMaterials;

            // 既存の個別設定を保存する。
            originalPropertyBlocks[i] =
                new MaterialPropertyBlock();

            renderers[i].GetPropertyBlock(
                originalPropertyBlocks[i]);
        }

        try
        {
            // ディゾルブ用マテリアルに置き換える。
            SetDissolveMaterial(
                renderers,
                dissolveMaterial);

            MaterialPropertyBlock propertyBlock =
                new MaterialPropertyBlock();

            // 敵ごとに異なるノイズ座標を設定する。
            Vector3 noiseOffset = new Vector3(
                Random.Range(0f, 100f),
                Random.Range(0f, 100f),
                Random.Range(0f, 100f));

            duration = Mathf.Max(0f, duration);

            float elapsed = 0f;

            // 演出開始時は敵を完全に非表示にする。
            SetDissolveAmount(
                renderers,
                propertyBlock,
                0f,
                noiseOffset);

            // 時間の経過に合わせて敵を徐々に表示する。
            while (elapsed < duration)
            {
                if (enemy == null)
                {
                    return;
                }

                elapsed += Time.deltaTime;

                float progress =
                    Mathf.Clamp01(elapsed / duration);

                SetDissolveAmount(
                    renderers,
                    propertyBlock,
                    progress,
                    noiseOffset);

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            // 演出終了時は完全に表示する。
            if (enemy != null)
            {
                SetDissolveAmount(
                    renderers,
                    propertyBlock,
                    1f,
                    noiseOffset);
            }
        }
        finally
        {
            // 敵が存在する場合は元の状態に戻す。
            if (enemy != null)
            {
                RestoreMaterials(
                    renderers,
                    originalMaterials,
                    originalPropertyBlocks);
            }

            // 正常終了・途中終了のどちらでもAIを再開する。
            if (enemy != null && stateMachine != null)
            {
                stateMachine.enabled = true;
            }
        }
    }

    // すべてのRendererのマテリアルをディゾルブ用に置き換える。
    private void SetDissolveMaterial(
        Renderer[] renderers,
        Material dissolveMaterial)
    {
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            Material[] materials =
                targetRenderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = dissolveMaterial;
            }

            targetRenderer.sharedMaterials = materials;
        }
    }

    // 元のマテリアルと個別設定を復元する。
    private void RestoreMaterials(
        Renderer[] renderers,
        Material[][] originalMaterials,
        MaterialPropertyBlock[] originalPropertyBlocks)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            // 元のマテリアル構成に戻す。
            if (originalMaterials[i] != null)
            {
                renderers[i].sharedMaterials =
                    originalMaterials[i];
            }

            // 元の個別設定に戻す。
            renderers[i].SetPropertyBlock(
                originalPropertyBlocks[i]);
        }
    }

    // ディゾルブの進行度とノイズ位置をRendererに設定する。
    private void SetDissolveAmount(
        Renderer[] renderers,
        MaterialPropertyBlock propertyBlock,
        float amount,
        Vector3 noiseOffset)
    {
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            // 既存の個別設定を取得してから必要な値を更新する。
            targetRenderer.GetPropertyBlock(propertyBlock);

            propertyBlock.SetFloat(
                DissolveAmountId,
                amount);

            propertyBlock.SetVector(
                NoiseOffsetId,
                noiseOffset);

            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
