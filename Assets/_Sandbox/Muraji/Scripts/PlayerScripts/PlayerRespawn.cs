using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class PlayerRespawn : MonoBehaviour
{
    private enum RespawnPhase
    {
        None,
        DeathMotion,
        FadeOut,
        RespawnMotion
    }

    [Header("参照")]

    [Tooltip("このステージで使用するリスポーン設定SOです。")]
    [SerializeField]
    private StageRespawnData stageData;

    [Tooltip("復活させるプレイヤーの移動Controllerです。")]
    [SerializeField]
    private SensorPlayerController playerController;

    [Tooltip("同じプレイヤーを追従しているPlayerCameraです。")]
    [SerializeField]
    private PlayerCamera playerCamera;

    [Tooltip(
        "画面全体を覆う黒いImageに付けたCanvasGroupです。"
        + "専用のCanvasGroupを指定してください。")]
    [SerializeField]
    private CanvasGroup fadeGroup;

    [Header("専用モーション")]

    [Tooltip("死亡ステートのフルパスです。Animatorに新しく作成してください。")]
    [SerializeField]
    private string deathState = "Base Layer.Death";

    [Tooltip("復活ステートのフルパスです。Animatorに新しく作成してください。")]
    [SerializeField]
    private string respawnState = "Base Layer.Respawn";

    [Tooltip("復活完了後に戻す通常待機ステートのフルパスです。")]
    [SerializeField]
    private string idleState = "Base Layer.Idle";

    [Header("エフェクトPrefab")]
    [SerializeField] private GameObject deathEffectPrefab;
    [SerializeField] private GameObject respawnEffectPrefab;
    [SerializeField, Min(0f)] private float effectLifetime = 5f;
    [SerializeField] private Vector3 effectOffset = Vector3.zero;

    private void SpawnEffect(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;
        GameObject instance = Instantiate(prefab, position + effectOffset, Quaternion.identity);
        if (effectLifetime > 0f) Destroy(instance, effectLifetime);
    }

    [Header("演出の追加先")]

    [Tooltip("死亡モーション開始時に呼びます。死亡エフェクトなどを登録できます。")]
    [SerializeField]
    private UnityEvent deathStarted = new UnityEvent();

    [Tooltip(
        "復活地点への移動直後に呼びます。"
        + "復活エフェクトなどを登録できます。")]
    [SerializeField]
    private UnityEvent respawnStarted = new UnityEvent();

    [Tooltip("HP回復と操作再開が完了したときに呼びます。")]
    [SerializeField]
    private UnityEvent respawnCompleted = new UnityEvent();

    [Tooltip(
        "実際にダメージを受けるPlayerHealthを指定してください。"
        + "今回の構成ではMid_ColliderのPlayerHealthを登録します。")]
    [SerializeField]
    private PlayerHealth playerHealth;
    private PlayerAnimator playerAnimator;
    private SensorChainsawDigging chainsawDigging;
    private Rigidbody playerBody;
    private Animator animator;

    private RespawnPhase phase;
    private float phaseElapsed;
    private bool initialized;
    private bool stateSaved;

    private StageRespawnData.RespawnPoint selectedPoint;
    private int selectedAreaNumber;

    private RigidbodyConstraints savedConstraints;
    private bool savedUseGravity;

    private bool savedControllerEnabled;
    private bool savedPlayerAnimatorEnabled;
    private bool savedDiggingEnabled;

    private AnimatorUpdateMode savedAnimatorUpdateMode;
    private AnimatorCullingMode savedAnimatorCullingMode;
    private bool savedApplyRootMotion;
    private float savedAnimatorSpeed;

    // HP側が持つRigidbody。移動用Rigidbodyと同じ場合は追加処理を行わない。
    private Rigidbody healthBody;

    // 復活時に戻す、HP側オブジェクトの初期ローカル姿勢。
    private Vector3 initialHealthLocalPosition;
    private Quaternion initialHealthLocalRotation;

    // 演出前の物理設定。
    private RigidbodyConstraints savedHealthConstraints;
    private bool savedHealthUseGravity;
    private bool healthBodyStateSaved;

    public bool IsRespawning => phase != RespawnPhase.None;

    // 必要な参照と設定を確認し、フェードを初期化する。
    private void Awake()
    {
        if (stageData == null ||
            playerController == null ||
            playerCamera == null ||
            fadeGroup == null)
        {
            StopWithError(
                "Stage Data・Player Controller・Player Camera・"
                + "Fade Groupを設定してください.");
            return;
        }

        playerAnimator = playerController.GetComponent<PlayerAnimator>();
        chainsawDigging = playerController.GetComponent<SensorChainsawDigging>();
        playerBody = playerController.GetComponent<Rigidbody>();
        animator = playerController.GetComponent<Animator>();

        if (playerHealth == null ||
            playerAnimator == null ||
            chainsawDigging == null ||
            playerBody == null ||
            animator == null)
        {
            StopWithError(
                "Player HealthにはMid_ColliderのPlayerHealthを指定してください。"
                + "Player Controllerと同じオブジェクトには"
                + "PlayerAnimator・SensorChainsawDigging・Rigidbody・Animatorが必要です。");
            return;
        }

        if (!playerCamera.UsesPlayer(playerController))
        {
            StopWithError(
                "PlayerCameraとPlayerRespawnに、同じプレイヤーを設定してください。");
            return;
        }

        if (!stageData.ValidateSettings(
                playerCamera.AreaCount, out string error))
        {
            StopWithError(error);
            return;
        }

        if (!HasAnimationState(deathState) ||
            !HasAnimationState(respawnState) ||
            !HasAnimationState(idleState))
        {
            StopWithError(
                "死亡・復活・待機ステートが見つかりません。"
                + "Animatorのステート名とレイヤー名を確認してください。");
            return;
        }

        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;

        // HP側の独立した物理オブジェクトを初期化する。
        if (!InitializeHealthBody())
        {
            return;
        }
        initialized = true;
    }

    // 有効化時に死亡通知を購読する。
    private void OnEnable()
    {
        if (initialized)
        {
            playerHealth.Died += HandleDeath;
        }
    }

    // 実時間で演出を進める。通常時は死亡通知の取りこぼしだけを確認する。
    private void Update()
    {
        if (!initialized)
            return;

        if (!IsRespawning)
        {
            // 初期化順などで通知前に購読できなかった場合にも対応する。
            if (playerHealth.IsDead)
            {
                HandleDeath();
            }

            return;
        }

        playerController.ClearRespawnInput();
        phaseElapsed += Time.unscaledDeltaTime;

        switch (phase)
        {
            case RespawnPhase.DeathMotion:
                if (phaseElapsed >= stageData.DeathMotionSeconds)
                {
                    ChangePhase(RespawnPhase.FadeOut);
                }
                break;

            case RespawnPhase.FadeOut:
                fadeGroup.alpha = CalculateProgress(
                    phaseElapsed, stageData.FadeOutSeconds);

                if (fadeGroup.alpha >= 1f)
                {
                    MoveToRespawnPoint();
                }
                break;

            case RespawnPhase.RespawnMotion:
                fadeGroup.alpha = 1f - CalculateProgress(
                    phaseElapsed, stageData.FadeInSeconds);

                float waitSeconds = Mathf.Max(
                    stageData.FadeInSeconds,
                    stageData.RespawnMotionSeconds);

                if (phaseElapsed >= waitSeconds)
                {
                    CompleteRespawn();
                }
                break;
        }
    }

    // HP側のRigidbodyを確認し、復活時に使用する初期配置を保存する。
    // HPと移動処理が、同じPlayerのRigidbodyを使用することを確認する。
    private bool InitializeHealthBody()
    {
        SensorPlayerController healthController =
            playerHealth.GetComponentInParent<SensorPlayerController>();

        if (healthController != playerController)
        {
            StopWithError(
                "Player Healthには、指定したPlayer Controllerと"
                + "同じオブジェクト、またはその子にある"
                + "PlayerHealthを設定してください。");

            return false;
        }

        // Awakeで取得済みの、親Playerの移動用Rigidbodyを共有する。
        healthBody = playerBody;

        return true;
    }

    // HP側の独立したRigidbodyを、演出中だけ停止する。
    private void StopHealthBody()
    {
        if (healthBody == null ||
            healthBody == playerBody ||
            healthBodyStateSaved)
        {
            return;
        }

        savedHealthConstraints = healthBody.constraints;
        savedHealthUseGravity = healthBody.useGravity;
        healthBodyStateSaved = true;

        if (!healthBody.isKinematic)
        {
            healthBody.linearVelocity = Vector3.zero;
            healthBody.angularVelocity = Vector3.zero;
        }

        healthBody.useGravity = false;
        healthBody.constraints = RigidbodyConstraints.FreezeAll;
    }

    // 親の移動後に、HP側の当たり判定を初期ローカル配置へ戻す。
    private void AlignHealthBodyToRespawn()
    {
        if (healthBody == null || healthBody == playerBody)
        {
            return;
        }

        Transform healthTransform = healthBody.transform;

        healthTransform.localPosition = initialHealthLocalPosition;
        healthTransform.localRotation = initialHealthLocalRotation;

        // Transformだけでなく、物理側の位置と回転にも反映する。
        healthBody.position = healthTransform.position;
        healthBody.rotation = healthTransform.rotation;

        if (!healthBody.isKinematic)
        {
            healthBody.linearVelocity = Vector3.zero;
            healthBody.angularVelocity = Vector3.zero;
        }
    }

    // HP側の物理設定を元に戻す。死亡前の速度は復元しない。
    private void RestoreHealthBody()
    {
        if (!healthBodyStateSaved)
        {
            return;
        }

        healthBodyStateSaved = false;

        if (healthBody == null)
        {
            return;
        }

        if (!healthBody.isKinematic)
        {
            healthBody.linearVelocity = Vector3.zero;
            healthBody.angularVelocity = Vector3.zero;
        }

        healthBody.constraints = savedHealthConstraints;
        healthBody.useGravity = savedHealthUseGravity;
    }

    // 死亡時のエリアを確定し、操作停止と死亡演出を開始する。
    private void HandleDeath()
    {
        if (!initialized || IsRespawning || !playerHealth.IsDead)
            return;

        if (!playerController.isActiveAndEnabled ||
            !playerAnimator.isActiveAndEnabled ||
            !chainsawDigging.isActiveAndEnabled ||
            !playerCamera.isActiveAndEnabled ||
            !animator.isActiveAndEnabled ||
            playerBody.isKinematic)
        {
            StopWithError(
                "プレイヤー・カメラ・Animatorを有効にし、"
                + "RigidbodyのIs Kinematicをオフにしてください。");
            return;
        }

        selectedAreaNumber = playerCamera.GetRespawnAreaNumber();

        if (selectedAreaNumber == 0)
        {
            selectedAreaNumber = stageData.FallbackAreaNumber;
        }

        if (!stageData.TryGetPoint(
                selectedAreaNumber, out selectedPoint))
        {
            StopWithError(
                $"エリア{selectedAreaNumber}の復活設定を取得できません。");
            return;
        }

        Collider area = playerCamera.GetRespawnArea(selectedAreaNumber);

        if (area == null ||
            !area.bounds.Contains(selectedPoint.Position))
        {
            StopWithError(
                $"エリア{selectedAreaNumber}のColliderが無効、"
                + "または復活座標がエリア範囲外です。");
            return;
        }

        ChangePhase(RespawnPhase.DeathMotion);
        SaveAndStopPlayer();

        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = true;

        animator.Play(Animator.StringToHash(deathState), 0, 0f);
        SpawnEffect(deathEffectPrefab, playerController.transform.position);
        deathStarted.Invoke();
    }

    // 通常設定を保存し、移動・食い込み・攻撃と物理移動を停止する。
    private void SaveAndStopPlayer()
    {
        savedControllerEnabled = playerController.enabled;
        savedPlayerAnimatorEnabled = playerAnimator.enabled;
        savedDiggingEnabled = chainsawDigging.enabled;
        savedUseGravity = playerBody.useGravity;

        savedAnimatorUpdateMode = animator.updateMode;
        savedAnimatorCullingMode = animator.cullingMode;
        savedApplyRootMotion = animator.applyRootMotion;
        savedAnimatorSpeed = animator.speed;

        // 強衝突中のFreezeAllはControllerのOnDisableで解除される。
        // 本来の制約は、その解除後に保存する。
        playerController.PrepareForRespawn();
        savedConstraints = playerBody.constraints;

        // OnDisableで回避時間やアニメーションの管理状態も解除する。
        chainsawDigging.enabled = false;
        playerAnimator.enabled = false;

        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        playerBody.useGravity = false;
        playerBody.constraints = RigidbodyConstraints.FreezeAll;

        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.speed = 1f;

        // 親だけでなく、Mid_Collider側の物理移動も停止する。
        StopHealthBody();
        stateSaved = true;
    }

    // 完全暗転中に復活地点へ移動し、復活モーションとフェードインを開始する。
    private void MoveToRespawnPoint()
    {
        fadeGroup.alpha = 1f;

        // RigidbodyとTransformをそろえ、カメラも同じ位置を参照できるようにする。
        playerBody.position = selectedPoint.Position;
        playerController.transform.position = selectedPoint.Position;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;

        playerController.SetRespawnFacing(selectedPoint.FaceRight);

        // 親の位置と向きが確定した後、Mid_Colliderの位置も合わせる。
        AlignHealthBodyToRespawn();

        Physics.SyncTransforms();

        if (!playerCamera.SnapToRespawnArea(selectedAreaNumber))
        {
            StopWithError("復活先のカメラエリアを設定できませんでした。");
            return;
        }

        animator.Play(Animator.StringToHash(respawnState), 0, 0f);

        ChangePhase(RespawnPhase.RespawnMotion);
        SpawnEffect(respawnEffectPrefab, selectedPoint.Position);
        respawnStarted.Invoke();
    }

    // HPと床履歴を初期化して、通常のアニメーション・物理設定・操作へ戻す。
    private void CompleteRespawn()
    {
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;

        animator.Play(Animator.StringToHash(idleState), 0, 0f);
        playerController.ClearRespawnInput();

        // 演出が終わるまではHPを0のまま保ち、既存の死亡ガードを利用する。
        playerHealth.RestoreFullHealth();

        RestorePlayerSettings();
        ChangePhase(RespawnPhase.None);

        respawnCompleted.Invoke();
    }

    // 演出前に保存した設定を復元する。死亡前の速度は復元しない。
    private void RestorePlayerSettings()
    {
        // 途中終了の場合も、Mid_Colliderの物理固定を解除する。
        RestoreHealthBody();
        if (!stateSaved)
            return;

        stateSaved = false;

        if (playerBody != null)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.constraints = savedConstraints;
            playerBody.useGravity = savedUseGravity;
        }

        if (animator != null)
        {
            animator.updateMode = savedAnimatorUpdateMode;
            animator.cullingMode = savedAnimatorCullingMode;
            animator.applyRootMotion = savedApplyRootMotion;
            animator.speed = savedAnimatorSpeed;
        }

        if (chainsawDigging != null)
        {
            chainsawDigging.enabled = savedDiggingEnabled;
        }

        if (playerAnimator != null)
        {
            playerAnimator.enabled = savedPlayerAnimatorEnabled;
        }

        if (playerController != null)
        {
            playerController.ClearRespawnInput();
            playerController.enabled = savedControllerEnabled;
        }
    }

    // 演出段階を切り替え、経過時間をリセットする。
    private void ChangePhase(RespawnPhase nextPhase)
    {
        phase = nextPhase;
        phaseElapsed = 0f;
    }

    // 指定したAnimatorステートが基本レイヤーに存在するか確認する。
    private bool HasAnimationState(string statePath)
    {
        return !string.IsNullOrWhiteSpace(statePath) &&
            animator.HasState(0, Animator.StringToHash(statePath));
    }

    // 0秒設定にも対応して、演出の進行率を求める。
    private static float CalculateProgress(float elapsed, float duration)
    {
        return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
    }

    // 設定不備を通知し、リスポーン処理を停止する。
    private void StopWithError(string message)
    {
        Debug.LogError($"PlayerRespawn：{message}", this);
        enabled = false;
    }

    // 死亡通知の購読を解除し、途中終了時に物理固定や黒画面を残さない。
    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= HandleDeath;
        }

        RestorePlayerSettings();
        ChangePhase(RespawnPhase.None);

        if (fadeGroup != null)
        {
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
        }
    }
}
