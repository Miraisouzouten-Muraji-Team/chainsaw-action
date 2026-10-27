using System.Collections.Generic;
using UnityEngine;

// 敵の命中情報を受け取り、HP・やられ硬直・被弾後の無敵と点滅を管理する。
[DefaultExecutionOrder(2500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerAttackHitReceiver : MonoBehaviour, IAttackHitReceiver
{
    [Header("参照")]
    [Tooltip("プレイヤーの操作処理です。未設定なら自分または親から取得します。")]
    [SerializeField] private SensorPlayerController playerController;

    [Tooltip("被弾時に画面の周囲を赤くする演出です。専用Volumeと同じオブジェクトのPlayerDamageScreenEffectを指定します。")]
    [SerializeField] private PlayerDamageScreenEffect damageScreenEffect;

    [Tooltip("敵とギミックで共用する点滅処理です。未設定なら同じオブジェクトから取得し、なければ従来の設定を使って自動追加します。")]
    [SerializeField] private PlayerDamageMaterialFlash damageMaterialFlash;

    [Header("敵からの被弾")]
    [Tooltip("やられ状態で移動・攻撃・ジャンプ・食い込みの入力を止める秒数です。空中では落下を続けます。")]
    [SerializeField, Min(0.01f)] private float hurtDuration = 0.4f;
    [Tooltip("敵からの追加ダメージを無効にする秒数です。硬直時間とは別です。0なら追加の無敵時間を設けません。")]
    [SerializeField, Min(0f)] private float damageInvulnerabilityDuration = 2f;

    [Header("敵からの被弾時のカメラシェイク")]
    [Tooltip("被弾時に揺らす既存のCameraShake_Systemです。未設定ならMainCamera自身と親、子から探します。複数カメラがある場合は手動で指定してください。")]
    [SerializeField] private CameraShake_System damageCameraShake;
    [Tooltip("敵から攻撃を受けたときにカメラを揺らす秒数です。0なら揺らしません。既存システムに従いヒットストップ中も進みます。")]
    [SerializeField, Min(0f)] private float damageShakeDuration = 0.15f;
    [Tooltip("被弾時のカメラの揺れ幅です。値が大きいほど強く揺れます。0なら揺らしません。")]
    [SerializeField, Min(0f)] private float damageShakeMagnitude = 0.1f;

    [Header("点滅コンポーネント自動追加時の引き継ぎ設定")]
    [Tooltip("点滅させる身体・武器の親です。残像やエフェクトの親は含めないでください。")]
    [SerializeField] private Transform flashModelRoot;
    [Tooltip("個別指定する場合の描画対象です。空欄なら見た目の親からMeshRendererとSkinnedMeshRendererを取得します。")]
    [SerializeField] private Renderer[] flashRenderers;
    [Tooltip("白黒点滅の元にするUniversal Render Pipeline/Litのマテリアルです。実行時に専用コピーを作り、元の設定は変更しません。")]
    [SerializeField] private Material flashMaterial;
    [Tooltip("白と黒を切り替える間隔です。無敵時間が終わると元のマテリアルへ戻ります。")]
    [SerializeField, Min(0.01f)] private float flashInterval = 0.08f;

    [Header("実行中の確認")]
    [Tooltip("敵の攻撃に対する追加の無敵時間の残りです。")]
    [SerializeField] private float damageInvulnerabilityRemaining;

    public bool IsDamageInvulnerable => damageInvulnerabilityRemaining > 0f;

    private PlayerHealth playerHealth;
    private Rigidbody playerBody;

    // この処理が変更したColliderの組み合わせと、変更前の設定を記録する。
    private struct IgnoredEnemyPair
    {
        public Collider Player;
        public Collider Enemy;
        public bool WasIgnored;
    }

    private readonly List<Collider> playerCollisionBuffer = new List<Collider>();
    private readonly List<Collider> enemyCollisionBuffer = new List<Collider>();
    private readonly List<IgnoredEnemyPair> ignoredEnemyPairs = new List<IgnoredEnemyPair>();
    private readonly HashSet<(Collider, Collider)> registeredEnemyPairs =
        new HashSet<(Collider, Collider)>();

    // 被弾後の無敵と、既存の食い込み回避のどちらでも敵本体との衝突を無効にする。
    public bool IgnoresEnemyBodyCollisions =>
        isActiveAndEnabled && playerHealth != null && playerHealth.isActiveAndEnabled &&
        !playerHealth.IsDead && playerController != null && playerController.isActiveAndEnabled &&
        (IsDamageInvulnerable || !playerController.CanTakeDamage);

    private bool warnedDamageCameraShake;
    private bool processingHit;
    private int lifecycleVersion;
    private float previousHealth;

    // HP・親の操作処理を取得し、死亡と全回復による演出解除を登録する。
    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        if (playerController == null)
            playerController = GetComponentInParent<SensorPlayerController>();

        if (playerController == null)
        {
            Debug.LogError("被弾処理：自分または親のSensorPlayerControllerを指定してください。", this);
            enabled = false;
            return;
        }

        playerBody = playerController.GetComponent<Rigidbody>();

        // 既存のInspector設定を維持しつつ、材質を変更する担当を一つに統一する。
        if (damageMaterialFlash == null)
            damageMaterialFlash = GetComponent<PlayerDamageMaterialFlash>();
        if (damageMaterialFlash == null)
        {
            damageMaterialFlash = gameObject.AddComponent<PlayerDamageMaterialFlash>();
            damageMaterialFlash.ConfigureLegacySettings(
                flashModelRoot, flashRenderers, flashMaterial, flashInterval);
        }

        playerHealth.Died += HandleDied;
        playerHealth.HealthRestored += HandleHealthRestored;
    }

    // PlayerHealthのAwakeで初期HPが確定してから比較値とHP通知を登録する。
    private void Start()
    {
        if (playerHealth == null) return;
        previousHealth = playerHealth.CurrentHealth;
        playerHealth.HealthChanged += HandleHealthChanged;
    }

    // 有効な敵の命中だけを適用し、実際にHPが減った場合にやられと点滅を開始する。
    public void ReceiveAttackHit(AttackHitData attackHitData)
    {
        if (!isActiveAndEnabled || processingHit || playerHealth == null ||
            !playerHealth.isActiveAndEnabled || playerHealth.IsDead ||
            playerController == null || !playerController.isActiveAndEnabled ||
            IsDamageInvulnerable || !playerController.CanTakeDamage)
        {
            return;
        }

        if (attackHitData is not EnemyAttackHitData enemyAttackHitData)
        {
            Debug.LogWarning("PlayerAttackHitReceiver：敵の攻撃命中情報ではありません。", this);
            return;
        }

        float damage = enemyAttackHitData.Damage;
        if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;

        processingHit = true;
        try
        {
            float healthBefore = playerHealth.CurrentHealth;
            int versionBefore = lifecycleVersion;

            // HP変更イベント中に別の攻撃が来ても、同じ被弾を重ねない。
            damageInvulnerabilityRemaining = Mathf.Max(0f, damageInvulnerabilityDuration);
            playerHealth.TakeDamage(damage);

            // 死亡・復活や無効化がHP通知中に起きた場合、やられで上書きしない。
            if (versionBefore != lifecycleVersion) return;
            if (!isActiveAndEnabled || playerHealth.IsDead ||
                !playerController.isActiveAndEnabled ||
                playerHealth.CurrentHealth >= healthBefore)
            {
                ResetDamageFeedback(false);
                return;
            }

            // 無敵・回避で無効になった攻撃ではなく、実際にHPが減った被弾だけで演出する。
            if (damageScreenEffect != null)
                damageScreenEffect.PlayDamageEffect();

            playerController.BeginDamageStun(hurtDuration);
            // 被弾を受信した物理ステップから、敵本体に押されないようにする。
            UpdateEnemyCollisionIgnores();
            if (damageMaterialFlash != null)
                damageMaterialFlash.PlayDamageFlash(damageInvulnerabilityRemaining);
        }
        finally
        {
            processingHit = false;
        }
    }

    // ダメージ元を問わずHPの減少を検出する。回復では揺らさず、連続ダメージでは揺れを更新する。
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        bool healthDecreased = currentHealth < previousHealth;
        previousHealth = currentHealth;

        if (!healthDecreased || !isActiveAndEnabled || playerHealth == null ||
            !playerHealth.isActiveAndEnabled)
            return;

        PlayDamageCameraShake();
    }

    // 有効なシェイク参照を取得し、指定時間と強さで既存のカメラ演出を呼び出す。
    private void PlayDamageCameraShake()
    {
        if (damageShakeDuration <= 0f || damageShakeMagnitude <= 0f ||
            float.IsNaN(damageShakeDuration) || float.IsInfinity(damageShakeDuration) ||
            float.IsNaN(damageShakeMagnitude) || float.IsInfinity(damageShakeMagnitude))
        {
            return;
        }

        if (damageCameraShake == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                damageCameraShake = mainCamera.GetComponentInParent<CameraShake_System>();
                if (damageCameraShake == null)
                    damageCameraShake = mainCamera.GetComponentInChildren<CameraShake_System>();
            }
        }

        if (damageCameraShake == null || !damageCameraShake.isActiveAndEnabled)
        {
            if (!warnedDamageCameraShake)
            {
                warnedDamageCameraShake = true;
                Debug.LogWarning(
                    "被弾カメラシェイク：Damage Camera Shakeに、有効なCameraShake_Systemを指定してください。ダメージや硬直の処理は継続します。", this);
            }
            return;
        }

        damageCameraShake.Shake(damageShakeDuration, damageShakeMagnitude);
    }

    // 無敵時間と敵本体との衝突除外を物理更新で管理する。
    private void FixedUpdate()
    {
        // 回避だけが有効な場合や、途中で出現・再有効化した敵にも対応する。
        UpdateEnemyCollisionIgnores();
        if (!IsDamageInvulnerable) return;
        if (playerHealth == null || playerHealth.IsDead || playerController == null ||
            !playerController.isActiveAndEnabled)
        {
            ResetDamageFeedback(false);
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        damageInvulnerabilityRemaining = Mathf.Max(0f, damageInvulnerabilityRemaining - deltaTime);
        if (!IsDamageInvulnerable)
        {
            // 食い込みの回避も終了していれば、次の物理計算から衝突を元へ戻す。
            UpdateEnemyCollisionIgnores();
            return;
        }
    }

    // 無敵中だけ、プレイヤー本体と敵本体の物理Colliderの衝突を無効にする。
    // Triggerや地形のCollider、レイヤー全体の衝突設定は変更しない。
    private void UpdateEnemyCollisionIgnores()
    {
        if (!IgnoresEnemyBodyCollisions || playerBody == null)
        {
            RestoreEnemyCollisions();
            return;
        }

        // 身体や敵が途中で追加・再有効化された場合も、物理計算前に反映する。
        playerBody.GetComponentsInChildren<Collider>(true, playerCollisionBuffer);
        EnemyAttackHitReceiver[] enemies =
            Object.FindObjectsByType<EnemyAttackHitReceiver>(FindObjectsSortMode.None);

        foreach (EnemyAttackHitReceiver enemy in enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled ||
                enemy.transform.IsChildOf(playerController.transform)) continue;

            // Receiverが子にある場合は、その親の敵Rigidbodyから本体を収集する。
            Rigidbody enemyBody = enemy.GetComponentInParent<Rigidbody>();
            if (enemyBody == playerBody) continue;
            Transform enemyRoot = enemyBody != null ? enemyBody.transform : enemy.transform;
            enemyRoot.GetComponentsInChildren<Collider>(true, enemyCollisionBuffer);

            foreach (Collider enemyCollider in enemyCollisionBuffer)
            {
                if (!IsActiveBodyCollider(enemyCollider) ||
                    enemyCollider.attachedRigidbody == playerBody ||
                    (enemyBody != null && enemyCollider.attachedRigidbody != enemyBody)) continue;

                foreach (Collider playerCollider in playerCollisionBuffer)
                {
                    if (!IsActiveBodyCollider(playerCollider) ||
                        playerCollider.attachedRigidbody != playerBody) continue;

                    var key = (playerCollider, enemyCollider);
                    if (registeredEnemyPairs.Add(key))
                    {
                        ignoredEnemyPairs.Add(new IgnoredEnemyPair
                        {
                            Player = playerCollider,
                            Enemy = enemyCollider,
                            WasIgnored = Physics.GetIgnoreCollision(playerCollider, enemyCollider)
                        });
                    }

                    // Colliderの再有効化などで設定が消えても適用し直す。
                    if (!Physics.GetIgnoreCollision(playerCollider, enemyCollider))
                        Physics.IgnoreCollision(playerCollider, enemyCollider, true);
                }
            }
        }
    }

    // 有効な非Trigger Colliderだけを物理的な身体として扱う。
    private static bool IsActiveBodyCollider(Collider target)
    {
        return target != null && target.enabled && target.gameObject.activeInHierarchy &&
            !target.isTrigger;
    }

    // この処理で変更した組み合わせだけを、無敵開始前の衝突設定へ戻す。
    private void RestoreEnemyCollisions()
    {
        foreach (IgnoredEnemyPair pair in ignoredEnemyPairs)
        {
            if (pair.Player == null || pair.Enemy == null) continue;
            Physics.IgnoreCollision(pair.Player, pair.Enemy, pair.WasIgnored);
        }
        ignoredEnemyPairs.Clear();
        registeredEnemyPairs.Clear();
        playerCollisionBuffer.Clear();
        enemyCollisionBuffer.Clear();
    }

    // 復活処理などから被弾演出と無敵を解除する。
    public void ResetDamageFeedback(bool returnToIdle)
    {
        lifecycleVersion++;
        damageInvulnerabilityRemaining = 0f;
        // 死亡・復活・無効化の際、画面の赤い縁取りも残さない。
        if (damageScreenEffect != null)
            damageScreenEffect.ResetEffect();
        RestoreEnemyCollisions();
        if (damageMaterialFlash != null) damageMaterialFlash.StopFlashing();
        if (playerController != null) playerController.CancelDamageStun(returnToIdle);
    }

    // 死亡時は待機モーションを再生せず、被弾演出だけを解除する。
    private void HandleDied()
    {
        ResetDamageFeedback(false);
    }

    // 全回復時は復活後へ無敵や点滅を持ち越さない。
    private void HandleHealthRestored()
    {
        ResetDamageFeedback(true);
    }

    // 無効化された際に白黒の材質と無敵を残さない。
    private void OnDisable()
    {
        ResetDamageFeedback(playerHealth != null && !playerHealth.IsDead);
    }

    // イベント購読を解除し、変更した衝突設定と点滅を元へ戻す。
    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= HandleHealthChanged;
            playerHealth.Died -= HandleDied;
            playerHealth.HealthRestored -= HandleHealthRestored;
        }
        RestoreEnemyCollisions();
        if (damageMaterialFlash != null) damageMaterialFlash.StopFlashing();
    }
}
