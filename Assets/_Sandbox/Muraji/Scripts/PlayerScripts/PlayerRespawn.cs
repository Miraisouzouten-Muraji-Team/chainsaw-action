using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class PlayerRespawn : MonoBehaviour
{
    private const float GROUND_SAMPLE_INSET = 0.001f;
    private const float LANDING_VERTICAL_SPEED_TOLERANCE = 0.1f;
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

    [Tooltip("Player Controllerに指定しているものと同じPlayerParameterアセットを設定してください。死亡中も重力倍率・落下重力の増加時間と最大倍率・最大落下速度を使用します。")]
    [SerializeField] private PlayerParameter playerParameter;

    [Tooltip("指定Layerを初期化する管理コンポーネントです。未指定ならプレイヤーと同じシーンから自動取得します。管理側のReset Layersだけを設定し、敵の個別登録は不要です。")]
    [SerializeField] private EnemyRespawnManager enemyRespawnManager;

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

    [Tooltip("オンならゲーム開始時と同じ向きで復活します。オフならStageRespawnDataの復活地点ごとのFaceRightを使用します。")]
    [SerializeField] private bool useInitialFacingOnRespawn = true;

    [Header("死亡中の落下")]
    [Tooltip("死亡中、足元に床があるとみなす距離です。地形との接触余白に合わせて調整してください。")]
    [SerializeField, Min(0.001f)] private float deathGroundTolerance = 0.05f;

    [Tooltip("地面のない場所で死亡したときの落下待機の上限秒数です。通常は着地してから暗転します。0なら着地するまで無制限に待ちます。ゲーム内時間で計測します。")]
    [SerializeField, Min(0f)] private float maximumDeathFallSeconds = 10f;

    [Header("エフェクトPrefab")]
    [SerializeField] private GameObject deathEffectPrefab;
    [SerializeField] private GameObject respawnEffectPrefab;
    [SerializeField, Min(0f)] private float effectLifetime = 5f;
    [SerializeField] private Vector3 effectOffset = Vector3.zero;

    // 指定した演出Prefabを有効な状態で生成し、設定時間後に破棄する。
    private void SpawnEffect(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
            return;

        GameObject instance = Instantiate(
            prefab,
            position + effectOffset,
            Quaternion.identity);

        instance.SetActive(true);

        if (effectLifetime > 0f)
            Destroy(instance, effectLifetime);
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
    private ChainsawAccelerator chainsawAccelerator;
    private Quaternion initialPlayerLocalRotation;
    private bool initialFaceRight;
    private bool respawnFaceRight;
    private bool reapplyFacingAfterAnimation;

    private RespawnPhase phase;
    private float phaseElapsed;
    private bool initialized;
    private bool stateSaved;
    private bool deathHasLanded;
    private float deathFallElapsed;
    private Collider[] deathBodyColliders;

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
            playerParameter == null ||
            playerCamera == null ||
            fadeGroup == null)
        {
            StopWithError(
                "Stage Data・Player Controller・Player Parameter・Player Camera・"
                + "Fade Groupを設定してください.");
            return;
        }

        playerAnimator = playerController.GetComponent<PlayerAnimator>();
        chainsawDigging = playerController.GetComponent<SensorChainsawDigging>();
        playerBody = playerController.GetComponent<Rigidbody>();
        animator = playerController.GetComponent<Animator>();
        // アクセルはプレイヤーの子へ配置できるため、非アクティブな子も含めて取得する。
        chainsawAccelerator = playerController.GetComponentInChildren<ChainsawAccelerator>(true);
        initialPlayerLocalRotation = playerController.transform.localRotation;
        initialFaceRight = playerController.FacingDirection >= 0f;

        if (playerHealth == null ||
            playerAnimator == null ||
            chainsawDigging == null ||
            playerBody == null ||
            chainsawAccelerator == null ||
            animator == null)
        {
            StopWithError(
                "Player HealthにはMid_ColliderのPlayerHealthを指定してください。"
                + "Player Controllerと同じオブジェクトには"
                + "PlayerAnimator・SensorChainsawDigging・Rigidbody・Animatorが必要です。"
                + "ChainsawAcceleratorはPlayer Controllerと同じオブジェクト、またはその子に配置してください。");
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
        // Controllerが無効な死亡中も、同じRigidbodyの本体Colliderから足元を調べる。
        deathBodyColliders = playerBody.GetComponentsInChildren<Collider>(true);
        if (enemyRespawnManager == null)
            enemyRespawnManager = EnemyRespawnManager.FindForScene(playerController.gameObject.scene);
        if (enemyRespawnManager == null)
            Debug.LogWarning("PlayerRespawn：Layer初期化には同じシーンに有効なEnemyRespawnManagerを1つ配置し、Reset Layersを設定してください。", this);
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
                bool fallTimedOut = maximumDeathFallSeconds > 0f &&
                    deathFallElapsed >= maximumDeathFallSeconds;
                if (phaseElapsed >= stageData.DeathMotionSeconds &&
                    (deathHasLanded || fallTimedOut))
                {
                    // 通常は着地後だけ固定する。床のない場所では待機上限後に暗転して復帰する。
                    FreezeRespawnBody();
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

    // 復活モーションと待機への切り替えが回転を上書きしても、描画前に復活する向きへそろえる。
    private void LateUpdate()
    {
        if (!initialized || (phase != RespawnPhase.RespawnMotion && !reapplyFacingAfterAnimation))
            return;
        ApplyRespawnFacing();
        reapplyFacingAfterAnimation = false;
    }

    // 本体の初期回転を戻してから、見た目・内部の左右向き・食い込みセンサー・物理姿勢をそろえる。
    private void ApplyRespawnFacing()
    {
        playerController.transform.localRotation = initialPlayerLocalRotation;
        playerController.SetRespawnFacing(respawnFaceRight);
        playerBody.rotation = playerController.transform.rotation;
    }

    // 死亡アニメーション中も重力落下を続け、実際に足元へ着いた段階で物理移動を固定する。
    private void FixedUpdate()
    {
        if (!initialized || phase != RespawnPhase.DeathMotion || deathHasLanded ||
            playerBody == null || playerBody.isKinematic) return;

        // 重力加算前の物理結果で着地を確認し、接地している本体へ落下速度を重ねない。
        if (Mathf.Abs(playerBody.linearVelocity.y) <= LANDING_VERTICAL_SPEED_TOLERANCE &&
            HasDeathGroundSupport())
        {
            deathHasLanded = true;
            FreezeRespawnBody();
            return;
        }

        ApplyDeathFallGravity(Time.fixedDeltaTime);
        deathFallElapsed += Time.fixedDeltaTime;
    }

    // 通常時と同じPlayerParameterの落下重力カーブと速度上限で、死亡中の上下速度を更新する。
    private void ApplyDeathFallGravity(float deltaTime)
    {
        float fallProgress = Mathf.Clamp01(deathFallElapsed /
            Mathf.Max(playerParameter.fallGravityIncreaseTime, 0.01f));
        float gravityMultiplier = Mathf.SmoothStep(1f,
            playerParameter.maxFallGravityMultiplier, fallProgress);
        Vector3 velocity = playerBody.linearVelocity;
        velocity.y += Physics.gravity.y * playerParameter.gravityScale * gravityMultiplier * deltaTime;
        velocity.y = Mathf.Max(velocity.y, -playerParameter.maxFallSpeed);
        playerBody.linearVelocity = velocity;
    }

    // 各本体Colliderの底面を5本のRayで調べ、壁や天井を着地と誤認しないよう床法線も確認する。
    private bool HasDeathGroundSupport()
    {
        if (deathBodyColliders == null) return false;
        int terrainLayers = chainsawDigging.TerrainLayerMask;
        if (terrainLayers == 0) return false;
        float tolerance = Mathf.Max(0.001f, deathGroundTolerance);
        foreach (Collider bodyCollider in deathBodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.attachedRigidbody != playerBody)
                continue;
            Bounds bounds = bodyCollider.bounds;
            float insetX = Mathf.Max(0f, bounds.extents.x - GROUND_SAMPLE_INSET);
            float insetZ = Mathf.Max(0f, bounds.extents.z - GROUND_SAMPLE_INSET);
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 origin = bounds.center;
                origin.y = bounds.max.y + tolerance;
                if (sample > 0)
                {
                    origin.x += (sample <= 2 ? -insetX : insetX);
                    origin.z += (sample % 2 == 0 ? -insetZ : insetZ);
                }
                foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down,
                    bounds.size.y + tolerance * 2f, terrainLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || hit.collider.attachedRigidbody == playerBody ||
                        hit.transform == playerController.transform ||
                        hit.transform.IsChildOf(playerController.transform) ||
                        Physics.GetIgnoreLayerCollision(bodyCollider.gameObject.layer, hit.collider.gameObject.layer) ||
                        Physics.GetIgnoreCollision(bodyCollider, hit.collider)) continue;
                    if (hit.normal.y >= playerController.GroundNormalThreshold &&
                        Mathf.Abs(hit.point.y - bounds.min.y) <= tolerance)
                        return true;
                }
            }
        }
        return false;
    }

    // 着地後と暗転・復活演出中だけ本体を固定し、転送後に再落下しないようにする。
    private void FreezeRespawnBody()
    {
        if (playerBody == null) return;
        if (!playerBody.isKinematic)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
        }
        playerBody.useGravity = false;
        playerBody.constraints = RigidbodyConstraints.FreezeAll;
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

    // 通常設定を保存して操作・食い込み・攻撃を停止し、死亡中の重力落下だけを許可する。
    private void SaveAndStopPlayer()
    {
        // Controllerの無効化で速度が消される前に、既に落下していた速度だけを保存する。
        float fallingSpeed = Mathf.Min(0f, playerBody.linearVelocity.y);
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
        // 食い込み解除側の減速処理が終わってから、死亡時のアクセル値を初期値へ戻す。
        chainsawAccelerator.ResetForRespawn();

        playerBody.constraints = RigidbodyConstraints.FreezePositionX |
            RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        // Unity標準の自動重力は止め、FixedUpdateでPlayerParameterの重力だけを加える。
        playerBody.useGravity = false;
        playerBody.linearVelocity = Vector3.up * fallingSpeed;
        playerBody.angularVelocity = Vector3.zero;
        deathHasLanded = false;
        deathFallElapsed = 0f;

        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.speed = 1f;

        // 独立したHP側Rigidbodyがある場合だけ停止する。共有本体の落下は停止しない。
        StopHealthBody();
        stateSaved = true;
    }

    // 完全暗転中に復活地点へ移動し、復活モーションとフェードインを開始する。
    private void MoveToRespawnPoint()
    {
        fadeGroup.alpha = 1f;
        FreezeRespawnBody();

        // RigidbodyとTransformをそろえ、カメラも同じ位置を参照できるようにする。
        playerBody.position = selectedPoint.Position;
        playerController.transform.position = selectedPoint.Position;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;

        // 初期状態へ戻す場合は、復活地点SOの左右設定で逆向きへ上書きしない。
        respawnFaceRight = useInitialFacingOnRespawn ? initialFaceRight : selectedPoint.FaceRight;
        ApplyRespawnFacing();

        // 親の位置と向きが確定した後、Mid_Colliderの位置も合わせる。
        AlignHealthBodyToRespawn();

        Physics.SyncTransforms();

        // 完全暗転中に指定Layerの対象を開始時から作り直し、復活演出の前に配置を戻す。
        if (enemyRespawnManager == null)
            enemyRespawnManager = EnemyRespawnManager.FindForScene(playerController.gameObject.scene);
        if (enemyRespawnManager != null)
            enemyRespawnManager.ResetLayerObjects();

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

        // 操作再開直前にも初期化し、演出中に残った加減速要求を持ち越さない。
        chainsawAccelerator.ResetForRespawn();

        RestorePlayerSettings();
        ChangePhase(RespawnPhase.None);
        // 待機アニメーションが評価された後にも、一度だけ正しい向きを反映する。
        reapplyFacingAfterAnimation = true;

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

    // 途中終了時に物理固定や黒画面を残さない。
    private void OnDisable()
    {
        reapplyFacingAfterAnimation = false;
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
