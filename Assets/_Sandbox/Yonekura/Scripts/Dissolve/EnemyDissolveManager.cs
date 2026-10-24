using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 敵のディゾルブ出現演出を一括管理するクラス
/// </summary>
public class EnemyDissolveManager : MonoBehaviour
{
    // Shader GraphのReferenceと同じ名前を指定する
    private static readonly int DissolveAmountId =
        Shader.PropertyToID("_DissolveAmount");

    private static readonly int NoiseOffsetId =
        Shader.PropertyToID("_NoiseOffset");

    // シーン内のEnemyDissolveManagerを1つだけ管理する
    private static EnemyDissolveManager instance;

    public static EnemyDissolveManager Instance => instance;

    /// <summary>
    /// Managerの生成時に重複を確認し、インスタンスを登録する
    /// </summary>
    private void Awake()
    {
        // すでに別のManagerが存在する場合は重複を防ぐ
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
    /// Managerの破棄時に登録したインスタンスを解除する
    /// </summary>
    private void OnDestroy()
    {
        // 破棄されるManager自身が登録されている場合だけ解除する
        if (instance == this)
        {
            instance = null;
        }
    }

    // 敵のディゾルブ出現演出を再生する
    public async UniTask PlayAppearAsync(
        GameObject enemy,
        Material dissolveMaterial,
        float duration)
    {
        // 敵が存在しない場合は処理しない
        if (enemy == null)
        {
            return;
        }

        // 演出用マテリアルが未設定の場合は処理しない
        if (dissolveMaterial == null)
        {
            Debug.LogError("ディゾルブマテリアルが未設定です。");
            return;
        }

        // 出現中はAIを停止する
        EnemyStateMachine stateMachine =
            enemy.GetComponent<EnemyStateMachine>();

        if (stateMachine != null)
        {
            stateMachine.enabled = false;
        }

        try
        {
            // 敵本体と子オブジェクトのRendererを取得する
            Renderer[] renderers =
                enemy.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                Debug.LogWarning(
                    $"{enemy.name}: Rendererが見つかりません。",
                    enemy);
                return;
            }

            // すべてのRendererのマテリアルを演出用に置き換える
            SetDissolveMaterial(renderers, dissolveMaterial);

            // Rendererごとの表示設定を変更するためのデータ
            MaterialPropertyBlock propertyBlock =
                new MaterialPropertyBlock();

            // 敵ごとに異なるノイズ座標を設定する
            Vector3 noiseOffset = new Vector3(
                Random.Range(0f, 100f),
                Random.Range(0f, 100f),
                Random.Range(0f, 100f));

            // 演出時間が負の値にならないようにする
            duration = Mathf.Max(0f, duration);

            float elapsed = 0f;

            // 演出開始時は敵を完全に非表示にする
            SetDissolveAmount(
                renderers, propertyBlock, 0f, noiseOffset);

            // 時間の経過に合わせて敵を徐々に表示する
            while (elapsed < duration)
            {
                // 演出中に敵が破棄された場合は終了する
                if (enemy == null)
                {
                    return;
                }

                elapsed += Time.deltaTime;

                // 経過時間を0～1の進行度に変換する
                float progress = Mathf.Clamp01(elapsed / duration);

                SetDissolveAmount(
                    renderers,
                    propertyBlock,
                    progress,
                    noiseOffset);

                // 次のフレームまで待機する
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            // 演出終了時は敵を完全に表示する
            if (enemy != null)
            {
                SetDissolveAmount(
                    renderers, propertyBlock, 1f, noiseOffset);
            }
        }
        finally
        {
            // 正常終了・途中終了のどちらでも、敵が存在すればAIを再開する
            if (enemy != null && stateMachine != null)
            {
                stateMachine.enabled = true;
            }
        }
    }

    // すべてのRendererのマテリアルをディゾルブ用に置き換える
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

            // 元のマテリアルスロット数を維持して置き換える
            Material[] materials = targetRenderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = dissolveMaterial;
            }

            targetRenderer.sharedMaterials = materials;
        }
    }

    // ディゾルブの進行度とノイズ位置をRendererに設定する
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

            // 既存の個別設定を取得してから必要な値を更新する
            targetRenderer.GetPropertyBlock(propertyBlock);

            propertyBlock.SetFloat(
                DissolveAmountId, amount);

            propertyBlock.SetVector(
                NoiseOffsetId, noiseOffset);

            // 更新した設定をRendererに反映する
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

}
