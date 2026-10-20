using UnityEngine;

// Player直下の武器用の親を左右反転する。位置や子のアニメーション姿勢には触れない。
[DisallowMultipleComponent]
public sealed class ChainsawFacingRoot : MonoBehaviour
{
    [Tooltip("左右の向きを読み取るPlayerのSensorPlayerControllerです。未設定なら自分または親から取得します。")]
    [SerializeField] private SensorPlayerController playerController;

    private Quaternion rightFacingLocalRotation;
    private bool initialized;

    // Player直下の専用オブジェクトか確認し、右向き時の基準角度を保存する。
    private void Awake()
    {
        if (playerController == null)
        {
            playerController = GetComponentInParent<SensorPlayerController>();
        }

        if (playerController == null || transform.parent != playerController.transform)
        {
            Debug.LogError(
                "ChainsawFacingRoot：Player直下に新しく作成した武器用の空オブジェクトへ追加してください。" +
                "Player本体、手のボーン、食い込みセンサーには追加しないでください。",
                this);
            enabled = false;
            return;
        }

        rightFacingLocalRotation = transform.localRotation;
        initialized = true;
        ApplyFacingRotation();
    }

    // 再有効化時は、現在のPlayerの向きを反映する。
    private void OnEnable()
    {
        if (initialized)
        {
            ApplyFacingRotation();
        }
    }

    // 物理更新でPlayerの向きを読み、武器用の親だけを反転する。
    private void FixedUpdate()
    {
        if (initialized)
        {
            ApplyFacingRotation();
        }
    }

    // Controllerと同じY軸180度の方式で反転し、子のローカル位置と角度は維持する。
    private void ApplyFacingRotation()
    {
        if (playerController == null)
        {
            return;
        }

        float angle = playerController.FacingDirection < 0f ? 180f : 0f;
        transform.localRotation = rightFacingLocalRotation * Quaternion.Euler(0f, angle, 0f);
    }
}
