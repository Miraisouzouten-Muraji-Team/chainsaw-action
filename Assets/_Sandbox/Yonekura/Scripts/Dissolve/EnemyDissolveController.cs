using UnityEngine;
using Cysharp.Threading.Tasks;

// 敵のディゾルブ出現演出を制御する
public class EnemyDissolveController : MonoBehaviour
{
    // Shader GraphのReferenceと同じ名前を指定
    private static readonly int DissolveAmountId =
        Shader.PropertyToID("_DissolveAmount");

    // ノイズの座標をずらすためのReference
    private static readonly int NoiseOffsetId =
        Shader.PropertyToID("_NoiseOffset");

    // 敵の子オブジェクトも含めてRendererを取得する
    private Renderer[] renderers;

    // マテリアルを直接変更せず、個別に値を設定する
    private MaterialPropertyBlock propertyBlock;

    private bool isPlaying;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock = new MaterialPropertyBlock();
    }

    // スポナーから受け取ったマテリアルを適用する
    public void SetDissolveMaterial(Material material)
    {
        if (material == null)
        {
            Debug.LogError(
                $"{name}: ディゾルブマテリアルが設定されていません。",
                this);
            return;
        }
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            // Rendererが持つマテリアル枠を取得
            Material[] materials = targetRenderer.sharedMaterials;

            // すべての枠に指定マテリアルを適用
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            targetRenderer.sharedMaterials = materials;
        }
    }

    // 指定された時間をかけて敵を表示する
    public async UniTask PlayAppearAsync(float duration)
    {
        if (isPlaying)
        {
            return;
        }

        isPlaying = true;

        // スポーンするたびにノイズの座標をランダムにずらす
        SetRandomNoiseOffset();

        // 最初は非表示にする
        SetDissolveAmount(0f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            // このオブジェクトが削除されていたら終了
            if (this == null || !gameObject)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float progress = duration > 0f
                ? Mathf.Clamp01(elapsed / duration)
                : 1f;

            SetDissolveAmount(progress);

            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        // 最後は確実に完全表示にする
        SetDissolveAmount(1f);

        isPlaying = false;
    }

    // ノイズの座標をランダムにずらす
    private void SetRandomNoiseOffset()
    {
        Vector3 randomOffset = new Vector3(
            Random.Range(0f, 100f),
            Random.Range(0f, 100f),
            Random.Range(0f, 100f)
        );

        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);

            propertyBlock.SetVector(
                NoiseOffsetId,
                randomOffset
            );

            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    //  すべてのRendererにディゾルブ進行度を設定
    private void SetDissolveAmount(float amount)
    {
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);

            propertyBlock.SetFloat(
                DissolveAmountId,
                amount
            );

            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
