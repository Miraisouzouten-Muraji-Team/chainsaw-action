using System.Collections.Generic;
using UnityEngine;

// 敵の命中情報を受け取り、HP・やられ硬直・被弾後の無敵と点滅を管理する。
[DefaultExecutionOrder(2500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerAttackHitReceiver : MonoBehaviour, IAttackHitReceiver
{
    private static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");
    private static readonly int EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");

    [Header("参照")]
    [Tooltip("プレイヤーの操作処理です。未設定なら自分または親から取得します。")]
    [SerializeField] private SensorPlayerController playerController;

    [Tooltip("被弾時に画面の周囲を赤くする演出です。専用Volumeと同じオブジェクトのPlayerDamageScreenEffectを指定します。")]
    [SerializeField] private PlayerDamageScreenEffect damageScreenEffect;

    [Header("敵からの被弾")]
    [Tooltip("やられ状態で移動・攻撃・ジャンプ・食い込みの入力を止める秒数です。空中では落下を続けます。")]
    [SerializeField, Min(0.01f)] private float hurtDuration = 0.4f;
    [Tooltip("敵からの追加ダメージを無効にする秒数です。硬直時間とは別です。0なら追加の無敵時間を設けません。")]
    [SerializeField, Min(0f)] private float damageInvulnerabilityDuration = 2f;

    [Header("無敵中の白黒点滅")]
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

    private bool processingHit;
    private int lifecycleVersion;
    private float flashElapsed;
    private int lastFlashPhase = -1;
    private bool warnedFlashSettings;
    private Material whiteMaterial;
    private Material blackMaterial;

    private sealed class FlashPart
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] WhiteMaterials;
        public Material[] BlackMaterials;
        public MaterialPropertyBlock OriginalBlock;
        public MaterialPropertyBlock[] OriginalSlotBlocks;
    }

    private readonly List<FlashPart> activeFlashParts = new List<FlashPart>();

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

        playerHealth.Died += HandleDied;
        playerHealth.HealthRestored += HandleHealthRestored;
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
            BeginFlashing();
        }
        finally
        {
            processingHit = false;
        }
    }

    // 無敵時間と点滅を物理更新で進め、終了時に元の描画へ戻す。
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
            RestoreMaterials();
            return;
        }

        flashElapsed += deltaTime;
        ApplyFlashPhase();
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

    // 専用マテリアルを生成し、現在の材質とプロパティを保存して白から点滅を始める。
    private void BeginFlashing()
    {
        RestoreMaterials();
        if (!IsDamageInvulnerable) return;

        if (!PrepareFlashMaterials()) return;

        Renderer[] sources = flashRenderers != null && flashRenderers.Length > 0
            ? flashRenderers
            : flashModelRoot != null
                ? flashModelRoot.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];

        HashSet<Renderer> registered = new HashSet<Renderer>();
        foreach (Renderer source in sources)
        {
            if (source == null || !registered.Add(source) ||
                (source is not MeshRenderer && source is not SkinnedMeshRenderer)) continue;

            Material[] originals = source.sharedMaterials;
            if (originals.Length == 0) continue;
            FlashPart part = new FlashPart
            {
                Renderer = source,
                OriginalMaterials = originals,
                WhiteMaterials = new Material[originals.Length],
                BlackMaterials = new Material[originals.Length],
                OriginalBlock = new MaterialPropertyBlock(),
                OriginalSlotBlocks = new MaterialPropertyBlock[originals.Length]
            };
            source.GetPropertyBlock(part.OriginalBlock);
            for (int i = 0; i < originals.Length; i++)
            {
                part.WhiteMaterials[i] = whiteMaterial;
                part.BlackMaterials[i] = blackMaterial;
                part.OriginalSlotBlocks[i] = new MaterialPropertyBlock();
                source.GetPropertyBlock(part.OriginalSlotBlocks[i], i);
                source.SetPropertyBlock(null, i);
            }
            source.SetPropertyBlock(null);
            activeFlashParts.Add(part);
        }

        if (activeFlashParts.Count == 0 && !warnedFlashSettings)
        {
            warnedFlashSettings = true;
            Debug.LogWarning("被弾点滅：Flash Model RootまたはFlash Renderersに身体の描画対象を設定してください。", this);
        }
        flashElapsed = 0f;
        lastFlashPhase = -1;
        ApplyFlashPhase();
    }

    // 割り当てたURP Litの専用コピーを、白黒点滅用に作成する。
    private bool PrepareFlashMaterials()
    {
        if (whiteMaterial != null && blackMaterial != null) return true;
        if (flashMaterial == null || flashMaterial.shader == null ||
            flashMaterial.shader.name != "Universal Render Pipeline/Lit" ||
            !flashMaterial.shader.isSupported)
        {
            if (!warnedFlashSettings)
            {
                warnedFlashSettings = true;
                Debug.LogWarning("被弾点滅：Universal Render Pipeline/LitのマテリアルをFlash Materialへ設定してください。硬直と無敵は動作します。", this);
            }
            return false;
        }

        whiteMaterial = new Material(flashMaterial) { name = "PlayerHurt_White", hideFlags = HideFlags.DontSave };
        blackMaterial = new Material(flashMaterial) { name = "PlayerHurt_Black", hideFlags = HideFlags.DontSave };
        ConfigureLitFlashMaterial(whiteMaterial, Color.white);
        ConfigureLitFlashMaterial(blackMaterial, Color.black);
        return true;
    }

    // Litの反射・透過・テクスチャの影響を抑え、Emissionで白と黒を指定する。
    // 実行時のコピーだけに適用し、元のマテリアルは変更しない。
    private static void ConfigureLitFlashMaterial(Material material, Color color)
    {
        material.SetColor(BASE_COLOR, Color.black);
        material.SetColor(EMISSION_COLOR, color);
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetFloat("_ReceiveShadows", 0f);
        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);
        if (material.HasProperty("_ZWriteControl")) material.SetFloat("_ZWriteControl", 1f);
        if (material.HasProperty("_SrcBlendAlpha"))
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlendAlpha"))
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);

        material.EnableKeyword("_EMISSION");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_PARALLAXMAP");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_DETAIL_MULX2");
        material.DisableKeyword("_DETAIL_SCALED");
        material.DisableKeyword("_CLEARCOAT");
        material.DisableKeyword("_CLEARCOATMAP");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    // 指定間隔で白黒を切り替える。同じ色の間は再割り当てしない。
    private void ApplyFlashPhase()
    {
        int phase = Mathf.FloorToInt(flashElapsed / Mathf.Max(0.01f, flashInterval)) % 2;
        if (phase == lastFlashPhase) return;
        lastFlashPhase = phase;
        foreach (FlashPart part in activeFlashParts)
        {
            if (part.Renderer != null)
                part.Renderer.sharedMaterials = phase == 0 ? part.WhiteMaterials : part.BlackMaterials;
        }
    }

    // 保存したマテリアルとプロパティを戻し、点滅用の対象一覧を空にする。
    private void RestoreMaterials()
    {
        foreach (FlashPart part in activeFlashParts)
        {
            if (part.Renderer == null) continue;
            part.Renderer.sharedMaterials = part.OriginalMaterials;
            part.Renderer.SetPropertyBlock(part.OriginalBlock.isEmpty ? null : part.OriginalBlock);
            for (int i = 0; i < part.OriginalSlotBlocks.Length; i++)
            {
                MaterialPropertyBlock block = part.OriginalSlotBlocks[i];
                part.Renderer.SetPropertyBlock(block.isEmpty ? null : block, i);
            }
        }
        activeFlashParts.Clear();
        flashElapsed = 0f;
        lastFlashPhase = -1;
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
        RestoreMaterials();
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

    // イベント購読と、このコンポーネントが作成したマテリアルを破棄する。
    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= HandleDied;
            playerHealth.HealthRestored -= HandleHealthRestored;
        }
        RestoreEnemyCollisions();
        RestoreMaterials();
        if (whiteMaterial != null) Destroy(whiteMaterial);
        if (blackMaterial != null) Destroy(blackMaterial);
    }
}
