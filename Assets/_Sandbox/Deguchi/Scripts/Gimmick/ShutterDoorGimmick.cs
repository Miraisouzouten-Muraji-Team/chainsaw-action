using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// 条件達成時にシャッターを開くギミック。
///
/// ・条件監視
/// ・ゲーム操作停止
/// ・扉へのカメラ演出
/// ・シャッター開放
/// ・元のカメラへ復帰
///
/// を担当します。
public class ShutterDoorGimmick : MonoBehaviour
{
    /// 複数の条件をどのように判定するか。
    public enum ConditionMode
    {
        // すべての条件を達成したら作動
        All,

        // どれか1つを達成したら作動
        Any
    }

    [Header("解除条件")]

    [SerializeField]
    private ConditionMode conditionMode = ConditionMode.All;

    [Tooltip("この扉を開くための条件を登録します。")]
    [SerializeField]
    private List<GimmickConditionBase> conditions
        = new List<GimmickConditionBase>();

    [Header("ゲーム操作ロック")]

    [SerializeField]
    private GameplaySequenceLock gameplaySequenceLock;

    [Header("シャッター")]

    [Tooltip("実際に上下移動させるシャッター本体")]
    [SerializeField]
    private Transform shutter;

    [Tooltip("閉じた位置からどれだけ動かすか")]
    [SerializeField]
    private Vector3 openOffset = new Vector3(0f, 4f, 0f);

    [Tooltip("シャッターが開く時間")]
    [SerializeField]
    private float openDuration = 1.5f;

    [Tooltip("シャッター開閉の速度変化")]
    [SerializeField]
    private AnimationCurve openCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("シャッター振動")]

    [Tooltip("開く直前に振動する時間")]
    [SerializeField]
    private float shakeDuration = 0.3f;

    [Tooltip("振動の大きさ")]
    [SerializeField]
    private float shakeAmount = 0.03f;

    [Tooltip("振動速度")]
    [SerializeField]
    private float shakeFrequency = 25f;

    [Header("カメラ演出")]

    [Tooltip("普段プレイヤーを映しているMain Camera")]
    [SerializeField]
    private Camera mainCamera;

    [Tooltip(
        "シャッター演出時のカメラ位置。"
        + "空のGameObjectを置いて設定してください。"
    )]
    [SerializeField]
    private Transform doorCameraPoint;

    [Tooltip("扉へカメラが移動する時間")]
    [SerializeField]
    private float cameraMoveDuration = 0.8f;

    [Tooltip("扉を映したあと開き始めるまでの時間")]
    [SerializeField]
    private float beforeOpenWait = 0.4f;

    [Tooltip("扉が開いたあと見せておく時間")]
    [SerializeField]
    private float afterOpenWait = 0.7f;

    [Tooltip("カメラが元に戻る時間")]
    [SerializeField]
    private float cameraReturnDuration = 0.8f;

    [Tooltip("扉を映しているときのField Of View")]
    [SerializeField]
    private float doorCameraFov = 40f;

    private Vector3 shutterClosedPosition;

    private bool sequenceStarted;
    private bool doorOpened;
    private bool ownsGameplayLock;

    private void Awake()
    {
        if (shutter != null)
        {
            // シャッターの初期位置を「閉じた位置」として記録する。
            shutterClosedPosition = shutter.localPosition;
        }
    }

    private void OnEnable()
    {
        // 各条件の達成イベントを監視する。
        foreach (GimmickConditionBase condition in conditions)
        {
            if (condition == null)
            {
                continue;
            }

            condition.Completed += OnConditionCompleted;
        }
    }

    private void Start()
    {
        // Scene開始時点ですでに条件を満たしている可能性もあるため確認。
        TryStartSequence();
    }

    private void OnDisable()
    {
        // イベント登録解除。
        foreach (GimmickConditionBase condition in conditions)
        {
            if (condition == null)
            {
                continue;
            }

            condition.Completed -= OnConditionCompleted;
        }

        // 演出途中でこのコンポーネントが無効化された場合の保険。
        if (ownsGameplayLock && gameplaySequenceLock != null)
        {
            gameplaySequenceLock.UnlockGameplay();
            ownsGameplayLock = false;
        }
    }

    /// いずれかの条件が達成されたときに呼ばれる。
    private void OnConditionCompleted(GimmickConditionBase condition)
    {
        TryStartSequence();
    }

    /// 現在の条件状態を確認して、
    /// 扉演出を開始できるなら開始する。
    private void TryStartSequence()
    {
        // 一度開始したら二重起動させない。
        if (sequenceStarted || doorOpened)
        {
            return;
        }

        if (!AreConditionsMet())
        {
            return;
        }

        StartCoroutine(OpenDoorSequence());
    }

    /// 設定されている解除条件を確認する。
    private bool AreConditionsMet()
    {
        if (conditions.Count == 0)
        {
            return false;
        }

        if (conditionMode == ConditionMode.All)
        {
            // 全条件達成方式
            foreach (GimmickConditionBase condition in conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                if (!condition.IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }

        // どれか1つ達成方式
        foreach (GimmickConditionBase condition in conditions)
        {
            if (condition == null)
            {
                continue;
            }

            if (condition.IsCompleted)
            {
                return true;
            }
        }

        return false;
    }

    /// 扉を開く一連の演出。
    private IEnumerator OpenDoorSequence()
    {
        sequenceStarted = true;

        /*
         * ---------------------------
         * ゲーム操作停止
         * ---------------------------
         */

        if (gameplaySequenceLock != null)
        {
            gameplaySequenceLock.LockGameplay();
            ownsGameplayLock = true;
        }

        /*
         * ---------------------------
         * カメラ状態保存
         * ---------------------------
         */

        Vector3 originalCameraPosition = Vector3.zero;
        Quaternion originalCameraRotation = Quaternion.identity;
        float originalCameraFov = 60f;

        if (mainCamera != null)
        {
            originalCameraPosition =
                mainCamera.transform.position;

            originalCameraRotation =
                mainCamera.transform.rotation;

            originalCameraFov =
                mainCamera.fieldOfView;
        }

        /*
         * ---------------------------
         * 扉へカメラ移動
         * ---------------------------
         */

        if (mainCamera != null && doorCameraPoint != null)
        {
            yield return MoveCamera(
                doorCameraPoint.position,
                doorCameraPoint.rotation,
                doorCameraFov,
                cameraMoveDuration
            );
        }

        // TimeScaleが0なので、
        // WaitForSecondsではなくRealtimeを使用する。
        yield return new WaitForSecondsRealtime(beforeOpenWait);

        /*
         * ---------------------------
         * シャッター開放
         * ---------------------------
         */

        yield return PlayShutterOpening();

        doorOpened = true;

        yield return new WaitForSecondsRealtime(afterOpenWait);

        /*
         * ---------------------------
         * カメラを元へ戻す
         * ---------------------------
         */

        if (mainCamera != null)
        {
            yield return MoveCamera(
                originalCameraPosition,
                originalCameraRotation,
                originalCameraFov,
                cameraReturnDuration
            );
        }

        /*
         * ---------------------------
         * ゲーム再開
         * ---------------------------
         */

        if (gameplaySequenceLock != null)
        {
            gameplaySequenceLock.UnlockGameplay();
            ownsGameplayLock = false;
        }
    }

    /// シャッターをコードで開けるアニメーション。
    private IEnumerator PlayShutterOpening()
    {
        if (shutter == null)
        {
            yield break;
        }

        /*
         * ---------------------------
         * 開く直前の振動
         * ---------------------------
         */

        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress =
                Mathf.Clamp01(elapsed / shakeDuration);

            // 徐々に振動を弱くする。
            float power = 1f - progress;

            float shake =
                Mathf.Sin(
                    elapsed
                    * shakeFrequency
                    * Mathf.PI
                    * 2f
                )
                * shakeAmount
                * power;

            shutter.localPosition =
                shutterClosedPosition
                + Vector3.right * shake;

            yield return null;
        }

        shutter.localPosition = shutterClosedPosition;

        /*
         * ---------------------------
         * シャッター上昇
         * ---------------------------
         */

        Vector3 openPosition =
            shutterClosedPosition + openOffset;

        elapsed = 0f;

        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress =
                Mathf.Clamp01(elapsed / openDuration);

            // AnimationCurveを使用することで、
            // 一定速度ではなく自然な加速・減速にする。
            float curvedProgress =
                openCurve.Evaluate(progress);

            shutter.localPosition =
                Vector3.LerpUnclamped(
                    shutterClosedPosition,
                    openPosition,
                    curvedProgress
                );

            yield return null;
        }

        // 最終位置を確実に合わせる。
        shutter.localPosition = openPosition;
    }

    /// カメラを指定位置へ滑らかに移動する。
    ///
    /// TimeScale = 0中でも動かすため、
    /// unscaledDeltaTimeを使用しています。
    private IEnumerator MoveCamera(
        Vector3 targetPosition,
        Quaternion targetRotation,
        float targetFov,
        float duration
    )
    {
        Vector3 startPosition =
            mainCamera.transform.position;

        Quaternion startRotation =
            mainCamera.transform.rotation;

        float startFov =
            mainCamera.fieldOfView;

        if (duration <= 0f)
        {
            mainCamera.transform.position =
                targetPosition;

            mainCamera.transform.rotation =
                targetRotation;

            mainCamera.fieldOfView =
                targetFov;

            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress =
                Mathf.Clamp01(elapsed / duration);

            // カメラが急に動き始めたり止まったりしないようにする。
            float smoothProgress =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    progress
                );

            mainCamera.transform.position =
                Vector3.Lerp(
                    startPosition,
                    targetPosition,
                    smoothProgress
                );

            mainCamera.transform.rotation =
                Quaternion.Slerp(
                    startRotation,
                    targetRotation,
                    smoothProgress
                );

            mainCamera.fieldOfView =
                Mathf.Lerp(
                    startFov,
                    targetFov,
                    smoothProgress
                );

            yield return null;
        }

        mainCamera.transform.position =
            targetPosition;

        mainCamera.transform.rotation =
            targetRotation;

        mainCamera.fieldOfView =
            targetFov;
    }
}
