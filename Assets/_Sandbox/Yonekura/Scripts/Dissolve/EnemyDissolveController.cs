using UnityEngine;
using Cysharp.Threading.Tasks;

// 敵のディゾルブ出現演出を制御する
public class EnemyDissolveController : MonoBehaviour
{
    // Shader GraphのReferenceと同じ名前を指定
    private static readonly int DissolveAmountId =
        Shader.PropertyToID("_DissolveAmount");

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

    // 指定された時間をかけて敵を表示する
    public async UniTask PlayAppearAsync(float duration)
    {
        if (isPlaying)
        {
            return;
        }

        isPlaying = true;

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

    // 取得したすべてのRendererに進行度を設定する
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
                amount);

            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
