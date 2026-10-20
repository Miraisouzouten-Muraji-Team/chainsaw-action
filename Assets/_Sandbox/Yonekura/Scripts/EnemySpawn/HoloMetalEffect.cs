using UnityEngine;
using Cysharp.Threading.Tasks;

// ホログラム金属の出現・実体化・消滅を制御する
public class HoloMetalEffect : MonoBehaviour
{
    [Header("表示する金属")]
    [SerializeField]
    private Renderer holoMetalRenderer;

    [SerializeField]
    private Renderer solidMetalRenderer;

    [Header("演出時間")]
    [SerializeField]
    private float hologramDuration = 0.2f;

    [SerializeField]
    private float solidDuration = 0.2f;

    [SerializeField]
    private float disappearDuration = 0.2f;

    [SerializeField]
    private float intervalMin = 0.2f;

    [SerializeField]
    private float intervalMax = 1.0f;

    private bool isPlaying;

    private void Awake()
    {
        // 初期状態は非表示
        if (holoMetalRenderer != null)
        {
            holoMetalRenderer.enabled = false;
        }

        if (solidMetalRenderer != null)
        {
            solidMetalRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        PlayLoopAsync().Forget();
    }

    private async UniTaskVoid PlayLoopAsync()
    {
        if (isPlaying)
        {
            return;
        }

        isPlaying = true;

        while (this != null && isActiveAndEnabled)
        {
            // ① ホログラム金属を表示
            SetVisible(true, false);
            await UniTask.Delay(
                (int)(hologramDuration * 1000));

            if (this == null || !isActiveAndEnabled)
            {
                break;
            }

            // ② 実体の金属に切り替える
            SetVisible(false, true);
            await UniTask.Delay(
                (int)(solidDuration * 1000));

            if (this == null || !isActiveAndEnabled)
            {
                break;
            }

            // ③ ホログラムに戻して消す
            SetVisible(true, false);
            await UniTask.Delay(
                (int)(disappearDuration * 1000));

            if (this == null || !isActiveAndEnabled)
            {
                break;
            }

            // 完全に非表示
            SetVisible(false, false);

            // ④ 次の出現までランダムに待つ
            float interval = Random.Range(
                intervalMin,
                intervalMax);

            await UniTask.Delay(
                (int)(interval * 1000));
        }

        isPlaying = false;
    }

    private void SetVisible(bool showHologram, bool showSolid)
    {
        if (holoMetalRenderer != null)
        {
            holoMetalRenderer.enabled = showHologram;
        }

        if (solidMetalRenderer != null)
        {
            solidMetalRenderer.enabled = showSolid;
        }
    }

    private void OnDisable()
    {
        // 無効化されたら見た目を消す
        SetVisible(false, false);
        isPlaying = false;
    }
}
