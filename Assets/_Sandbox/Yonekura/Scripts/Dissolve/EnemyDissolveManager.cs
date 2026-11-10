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

    /// <summary>
    /// Managerの生成時に重複を確認し、インスタンスを登録する。
    /// </summary>
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

    /// <summary>
    /// Managerの破棄時に登録したインスタンスを解除する。
    /// </summary>
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

    /// <summary>
    /// すべてのRendererのマテリアルをディゾルブ用に置き換える。
    /// </summary>
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

    /// <summary>
    /// 元のマテリアルと個別設定を復元する。
    /// </summary>
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

    /// <summary>
    /// ディゾルブの進行度とノイズ位置をRendererに設定する。
    /// </summary>
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

//using UnityEngine;
//using Cysharp.Threading.Tasks;
//using System.Collections.Generic;

///// <summary>
///// 敵のディゾルブ出現演出を一括管理するクラス
///// </summary>
//public class EnemyDissolveManager : MonoBehaviour
//{
//    // Shader GraphのReferenceと同じ名前を指定する
//    private static readonly int DissolveAmountId =
//        Shader.PropertyToID("_DissolveAmount");

//    private static readonly int NoiseOffsetId =
//        Shader.PropertyToID("_NoiseOffset");

//    // シーン内のEnemyDissolveManagerを1つだけ管理する
//    private static EnemyDissolveManager instance;

//    public static EnemyDissolveManager Instance => instance;

//    // Managerの生成時に重複を確認し、インスタンスを登録する
//    private void Awake()
//    {
//        // すでに別のManagerが存在する場合は重複を防ぐ
//        if (instance != null && instance != this)
//        {
//            Debug.LogError(
//                "EnemyDissolveManagerが複数存在しています。",
//                this);

//            Destroy(gameObject);
//            return;
//        }

//        instance = this;
//    }

//    // Managerの破棄時に登録したインスタンスを解除する
//    private void OnDestroy()
//    {
//        // 破棄されるManager自身が登録されている場合だけ解除する
//        if (instance == this)
//        {
//            instance = null;
//        }
//    }

//    /// <summary>
//    /// 指定されたマテリアルを使用して敵の出現演出を再生する。
//    /// </summary>
//    public async UniTask PlayAppearAsync(
//        GameObject enemy,
//        List<EnemyRendererMaterialEntry> materialEntries,
//        float duration)
//    {
//        // 敵が存在しない場合は処理しない
//        if (enemy == null)
//        {
//            return;
//        }

//        // マテリアル設定を確認する。
//        if (materialEntries == null || materialEntries.Count == 0)
//        {
//            Debug.LogError(
//                $"{enemy.name}: マテリアル設定が登録されていません。",
//                enemy);
//            return;
//        }

//        // 登録されたRendererとマテリアルをまとめて管理する。
//        List<Renderer> targetRenderers =
//            new List<Renderer>();

//        List<MaterialPropertyBlock[]> propertyBlocks =
//            new List<MaterialPropertyBlock[]>();

//        // 登録された設定を順番に処理する。
//        foreach (EnemyRendererMaterialEntry entry in materialEntries)
//        {
//            // マテリアル設定が未登録の場合は処理しない。
//            if (entry == null || entry.material == null)
//            {
//                Debug.LogError(
//                    $"{enemy.name}: マテリアルが未設定です。",
//                    enemy);
//                return;
//            }

//            // 空欄なら敵本体、それ以外なら指定した子オブジェクトを探す。
//            Transform targetTransform =
//                string.IsNullOrWhiteSpace(entry.targetPath)
//                    ? enemy.transform
//                    : enemy.transform.Find(entry.targetPath);

//            if (targetTransform == null)
//            {
//                Debug.LogError(
//                    $"{enemy.name}: 対象オブジェクトが見つかりません。" +
//                    $" Path: {entry.targetPath}",
//                    enemy);
//                return;
//            }

//            // 指定したオブジェクトからRendererを取得する。
//            Renderer targetRenderer =
//                targetTransform.GetComponent<Renderer>();

//            if (targetRenderer == null)
//            {
//                Debug.LogError(
//                    $"{targetTransform.name}: Rendererがありません。",
//                    targetTransform);
//                return;
//            }

//            // 元のマテリアルスロット数を維持する。
//            Material[] currentMaterials =
//                targetRenderer.sharedMaterials;

//            // 登録したマテリアルをすべてのスロットに適用する。
//            Material[] newMaterials =
//                new Material[currentMaterials.Length];

//            for (int i = 0; i < newMaterials.Length; i++)
//            {
//                newMaterials[i] = entry.material;
//            }

//            targetRenderer.sharedMaterials = newMaterials;

//            // マテリアルスロットごとの設定を用意する。
//            MaterialPropertyBlock[] blocks =
//                new MaterialPropertyBlock[newMaterials.Length];

//            for (int i = 0; i < blocks.Length; i++)
//            {
//                blocks[i] = new MaterialPropertyBlock();

//                targetRenderer.GetPropertyBlock(blocks[i], i);
//            }

//            targetRenderers.Add(targetRenderer);
//            propertyBlocks.Add(blocks);
//        }

//        // 出現中はAIを停止する。
//        EnemyStateMachine stateMachine =
//            enemy.GetComponent<EnemyStateMachine>();

//        if (stateMachine != null)
//        {
//            stateMachine.enabled = false;
//        }

//        try
//        {
//            // 敵ごとに異なるノイズ座標を設定する。
//            Vector3 noiseOffset = new Vector3(
//                Random.Range(0f, 100f),
//                Random.Range(0f, 100f),
//                Random.Range(0f, 100f));

//            duration = Mathf.Max(0f, duration);

//            float elapsed = 0f;

//            // 演出開始時は完全に非表示にする。
//            SetDissolveAmount(
//                targetRenderers,
//                propertyBlocks,
//                0f,
//                noiseOffset);

//            // 時間の経過に合わせて徐々に表示する。
//            while (elapsed < duration)
//            {
//                if (enemy == null)
//                {
//                    return;
//                }

//                elapsed += Time.deltaTime;

//                float progress =
//                    Mathf.Clamp01(elapsed / duration);

//                // 0 → 1 に変化させる
//                float dissolveAmount = 0f + progress;

//                SetDissolveAmount(
//                    targetRenderers,
//                    propertyBlocks,
//                    dissolveAmount,
//                    noiseOffset);

//                await UniTask.Yield(PlayerLoopTiming.Update);
//            }

//            // 演出終了時は完全に表示する。
//            if (enemy != null)
//            {
//                SetDissolveAmount(
//                    targetRenderers,
//                    propertyBlocks,
//                    1f,
//                    noiseOffset);
//            }
//        }
//        finally
//        {
//            // 敵が存在する場合はAIを再開する。
//            if (enemy != null && stateMachine != null)
//            {
//                stateMachine.enabled = true;
//            }
//        }
//    }

//    /// <summary>
//    /// すべての対象Rendererにディゾルブ値を設定する。
//    /// </summary>
//    private void SetDissolveAmount(
//        List<Renderer> renderers,
//        List<MaterialPropertyBlock[]> propertyBlocks,
//        float amount,
//        Vector3 noiseOffset)
//    {
//        for (int i = 0; i < renderers.Count; i++)
//        {
//            Renderer targetRenderer = renderers[i];

//            if (targetRenderer == null)
//            {
//                continue;
//            }

//            for (int j = 0; j < propertyBlocks[i].Length; j++)
//            {
//                MaterialPropertyBlock propertyBlock =
//                    propertyBlocks[i][j];

//                propertyBlock.SetFloat(
//                    DissolveAmountId,
//                    amount);

//                propertyBlock.SetVector(
//                    NoiseOffsetId,
//                    noiseOffset);

//                targetRenderer.SetPropertyBlock(
//                    propertyBlock,
//                    j);
//            }
//        }
//    }
//}
