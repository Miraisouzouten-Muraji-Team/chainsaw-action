
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾のHP・破壊・再生成と再生成演出を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossShieldController : MonoBehaviour
    {
        private static readonly int DISSOLVE_AMOUNT_ID = Shader.PropertyToID("_DissolveAmount");
        private static readonly int NOISE_OFFSET_ID = Shader.PropertyToID("_NoiseOffset");

        [Header("盾の設定")]
        [SerializeField, Min(1f)] private float maxHealth = 50f;
        [SerializeField, Min(0f)] private float respawnTime = 20f;

        [Header("盾オブジェクト")]
        [SerializeField] private GameObject shieldVisual;
        [SerializeField] private Collider[] shieldColliders;
        [SerializeField] private BossShieldMotionController motionController;

        [Header("再生成演出")]
        [SerializeField] private Material dissolveMaterial;
        [SerializeField, Min(0f)] private float dissolveDuration = 1f;

        private float currentHealth;
        private float respawnElapsedTime;
        private CancellationTokenSource restorationCancellation;

        public bool IsActive { get; private set; }
        public bool IsRestoring { get; private set; }
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;

        public event Action ShieldBroken;
        public event Action ShieldRestored;

        private void Awake()
        {
            currentHealth = maxHealth;
            respawnElapsedTime = 0f;
            IsActive = true;
            IsRestoring = false;

            motionController ??= GetComponent<BossShieldMotionController>();
            SetShieldVisible(true);
        }

        /// <summary>
        /// 盾破壊後だけ再生成タイマーを進める。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsActive || IsRestoring) { return; }

            respawnElapsedTime += Mathf.Max(0f, deltaTime);
        }

        /// <summary>
        /// ダメージを受けた場合にtrueを返す。
        /// </summary>
        public bool TakeDamage(float damage)
        {
            if (!IsActive || damage <= 0f ||
                float.IsNaN(damage) || float.IsInfinity(damage))
            {
                return false;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);

            if (currentHealth <= 0f)
            {
                BreakShield();
            }

            return true;
        }

        private void BreakShield()
        {
            if (!IsActive) { return; }

            IsActive = false;
            currentHealth = 0f;
            respawnElapsedTime = 0f;

            motionController?.CancelMotion();
            SetShieldVisible(false);
            ShieldBroken?.Invoke();
        }

        /// <summary>
        /// 20秒経過後、行動の区切りで盾の再生成を開始する。
        /// </summary>
        public bool TryRestore()
        {
            if (!isActiveAndEnabled || IsActive || IsRestoring || respawnElapsedTime < respawnTime)
            {
                return false;
            }

            IsRestoring = true;

            restorationCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            RestoreAsync(restorationCancellation).Forget();
            return true;
        }

        private async UniTaskVoid RestoreAsync(CancellationTokenSource source)
        {
            bool restored = false;

            try
            {
                motionController?.CancelMotion();
                motionController?.SnapToSocket();

                // ディゾルブ中は見た目だけ表示し、当たり判定は無効。
                SetShieldVisible(true, enableColliders: false);

                await PlayRestoreDissolveAsync(source.Token);
                source.Token.ThrowIfCancellationRequested();

                if (!isActiveAndEnabled) { return; }

                currentHealth = maxHealth;
                respawnElapsedTime = 0f;
                IsActive = true;
                restored = true;

                SetShieldVisible(true);
                ShieldRestored?.Invoke();
            }
            catch (OperationCanceledException)
                when (source.IsCancellationRequested)
            {
                // 死亡・シーン遷移・無効化での正常な中断。
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                if (!restored)
                {
                    IsActive = false;
                    SetShieldVisible(false);

                    // 中断後も破壊済みのままなら、次の行動区切りで再試行可能。
                    respawnElapsedTime = respawnTime;
                }

                IsRestoring = false;

                if (ReferenceEquals(restorationCancellation, source))
                {
                    restorationCancellation = null;
                }

                source.Dispose();
            }
        }

        /// <summary>
        /// 既存のディゾルブ素材を盾のRendererへ一時適用する。
        /// </summary>
        private async UniTask PlayRestoreDissolveAsync(
            CancellationToken cancellationToken)
        {
            if (shieldVisual == null || dissolveMaterial == null)
            {
                Debug.LogWarning(
                    "BossShieldController: ディゾルブ素材が未設定です。",
                    this);
                return;
            }

            Renderer[] renderers =
                shieldVisual.GetComponentsInChildren<Renderer>(true);

            Material[][] originalMaterials =
                new Material[renderers.Length][];

            MaterialPropertyBlock[] originalBlocks =
                new MaterialPropertyBlock[renderers.Length];

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer target = renderers[i];
                originalMaterials[i] = target.sharedMaterials;

                var originalBlock = new MaterialPropertyBlock();
                target.GetPropertyBlock(originalBlock);
                originalBlocks[i] = originalBlock;

                var temporaryMaterials =
                    new Material[originalMaterials[i].Length];

                for (int j = 0; j < temporaryMaterials.Length; j++)
                {
                    temporaryMaterials[j] = dissolveMaterial;
                }

                target.sharedMaterials = temporaryMaterials;
            }

            try
            {
                float duration = Mathf.Max(0f, dissolveDuration);
                float elapsed = 0f;

                Vector3 noiseOffset = new Vector3(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f));

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float progress = duration <= 0f
                        ? 1f
                        : Mathf.Clamp01(elapsed / duration);

                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer target = renderers[i];

                        if (target == null) { continue; }

                        var block = new MaterialPropertyBlock();
                        target.GetPropertyBlock(block);
                        block.SetFloat(DISSOLVE_AMOUNT_ID, progress);
                        block.SetVector(NOISE_OFFSET_ID, noiseOffset);
                        target.SetPropertyBlock(block);
                    }

                    if (progress >= 1f) { break; }

                    await UniTask.Yield(
                        PlayerLoopTiming.Update,
                        cancellationToken);

                    elapsed += Time.deltaTime;
                }
            }
            finally
            {
                // 正常終了でもキャンセルでも元のMaterialへ戻す。
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer target = renderers[i];

                    if (target == null) { continue; }

                    target.sharedMaterials = originalMaterials[i];
                    target.SetPropertyBlock(originalBlocks[i]);
                }
            }
        }

        /// <summary>
        /// 進行中の再生成を中断する。
        /// </summary>
        public void CancelRestoration()
        {
            if (restorationCancellation != null &&
                !restorationCancellation.IsCancellationRequested)
            {
                restorationCancellation.Cancel();
            }
        }

        private void SetShieldVisible(
            bool visible,
            bool enableColliders = true)
        {
            if (shieldVisual != null)
            {
                shieldVisual.SetActive(visible);
            }

            if (shieldColliders == null) { return; }

            foreach (Collider shieldCollider in shieldColliders)
            {
                if (shieldCollider != null)
                {
                    shieldCollider.enabled = visible && enableColliders;
                }
            }
        }

        private void OnDisable()
        {
            CancelRestoration();
        }

#if UNITY_EDITOR
        [ContextMenu("Debug/Break Shield")]
        private void DebugBreakShield()
        {
            TakeDamage(currentHealth);
        }
#endif
    }
}
