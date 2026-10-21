using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 共通Canvas上にEnemyごとのHPバーを生成し、表示位置と生存期間を管理する。
/// </summary>
/// <remarks>
/// Scene内で有効なインスタンスは1つだけを前提とする。
/// EnemyHealthBarTargetから登録されたEnemyについてHPバーを生成し、
/// Targetから取得した最終表示ワールド位置をScreen Space - Overlay Canvas上へ投影する。
/// HP値そのものの管理や、1本のゲージ描画は担当しない。
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class EnemyHealthBarCanvas : MonoBehaviour
{
    private static readonly HashSet<EnemyHealthBarTarget> ActiveTargets = new();
    private static EnemyHealthBarCanvas activeCanvas;

    [Header("参照")]
    [Tooltip("Enemyのワールド位置を画面座標へ変換するCamera。")]
    [SerializeField]
    private Camera worldCamera;

    [Tooltip("Enemy1体分のHPバーPrefab。")]
    [SerializeField]
    private EnemyHealthBar healthBarPrefab;

    [Tooltip("生成したHPバーを配置するRectTransform。未設定ならこのCanvas直下を使用する。")]
    [SerializeField]
    private RectTransform healthBarRoot;

    private readonly Dictionary<EnemyHealthBarTarget, EnemyHealthBar>
        healthBars = new();

    private readonly List<EnemyHealthBarTarget>
        targetsToRemove = new();

    private Canvas canvas;
    private bool isInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ActiveTargets.Clear();
        activeCanvas = null;
    }

    /// <summary>
    /// 有効になったEnemyを共通HPバーの表示対象として登録する。
    /// </summary>
    public static void RegisterTarget(EnemyHealthBarTarget target)
    {
        if (target == null)
        {
            return;
        }

        ActiveTargets.Add(target);

        if (activeCanvas != null && activeCanvas.isActiveAndEnabled)
        {
            activeCanvas.AddTarget(target);
        }
    }

    /// <summary>
    /// 無効化されたEnemyを共通HPバーの表示対象から解除する。
    /// </summary>
    public static void UnregisterTarget(EnemyHealthBarTarget target)
    {
        if (target == null)
        {
            return;
        }

        ActiveTargets.Remove(target);

        if (activeCanvas != null)
        {
            activeCanvas.RemoveTarget(target);
        }
    }

    private void Awake()
    {
        canvas = GetComponent<Canvas>();

        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                "CanvasのRender ModeはScreen Space - Overlayに設定してください。",
                this);

            enabled = false;
            return;
        }

        if (worldCamera == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                $"{nameof(worldCamera)} が設定されていません。",
                this);

            enabled = false;
            return;
        }

        if (healthBarPrefab == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                $"{nameof(healthBarPrefab)} が設定されていません。",
                this);

            enabled = false;
            return;
        }

        if (!(healthBarPrefab.transform is RectTransform))
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                $"{nameof(healthBarPrefab)} のルートはRectTransformである必要があります。",
                this);

            enabled = false;
            return;
        }

        if (healthBarRoot == null)
        {
            healthBarRoot = transform as RectTransform;
        }

        if (healthBarRoot == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                "HPバー配置先のRectTransformを取得できません。",
                this);

            enabled = false;
            return;
        }

        if (healthBarRoot != transform &&
            !healthBarRoot.IsChildOf(transform))
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                $"{nameof(healthBarRoot)} はこのCanvas配下に設定してください。",
                this);

            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized)
        {
            return;
        }

        if (activeCanvas != null && activeCanvas != this)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarCanvas)}: " +
                "有効な共通HPバーCanvasが複数存在しています。",
                this);

            enabled = false;
            return;
        }

        activeCanvas = this;

        foreach (EnemyHealthBarTarget target in ActiveTargets)
        {
            if (target != null)
            {
                AddTarget(target);
            }
        }
    }

    private void LateUpdate()
    {
        targetsToRemove.Clear();

        foreach (KeyValuePair<EnemyHealthBarTarget, EnemyHealthBar> pair
                 in healthBars)
        {
            EnemyHealthBarTarget target = pair.Key;
            EnemyHealthBar healthBar = pair.Value;

            if (target == null || healthBar == null)
            {
                targetsToRemove.Add(target);
                continue;
            }

            UpdateHealthBar(target, healthBar);
        }

        for (int index = 0; index < targetsToRemove.Count; index++)
        {
            RemoveTarget(targetsToRemove[index]);
        }
    }

    private void OnDisable()
    {
        if (activeCanvas == this)
        {
            activeCanvas = null;
        }

        ClearHealthBars();
    }

    private void AddTarget(EnemyHealthBarTarget target)
    {
        if (target == null ||
            !target.isActiveAndEnabled ||
            healthBars.ContainsKey(target))
        {
            return;
        }

        EnemyHealth enemyHealth = target.EnemyHealth;

        if (enemyHealth == null || enemyHealth.IsDead)
        {
            return;
        }

        EnemyHealthBar healthBar = Instantiate(
            healthBarPrefab,
            healthBarRoot,
            false);

        healthBar.name = $"{healthBarPrefab.name}_{target.name}";
        healthBar.Bind(enemyHealth);

        healthBars.Add(target, healthBar);
        UpdateHealthBar(target, healthBar);
    }

    private void RemoveTarget(EnemyHealthBarTarget target)
    {
        if (ReferenceEquals(target, null))
        {
            return;
        }

        if (!healthBars.TryGetValue(target, out EnemyHealthBar healthBar))
        {
            return;
        }

        healthBars.Remove(target);

        if (healthBar != null)
        {
            Destroy(healthBar.gameObject);
        }
    }

    private void UpdateHealthBar(
        EnemyHealthBarTarget target,
        EnemyHealthBar healthBar)
    {
        EnemyHealth enemyHealth = target.EnemyHealth;

        if (enemyHealth == null || enemyHealth.IsDead)
        {
            SetHealthBarVisible(healthBar, false);
            return;
        }

        Vector3 screenPosition =
            worldCamera.WorldToScreenPoint(target.HealthBarWorldPosition);

        // Camera背面の座標は画面座標へ投影すると反転して見えるため表示しない。
        if (screenPosition.z <= 0f)
        {
            SetHealthBarVisible(healthBar, false);
            return;
        }

        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                healthBarRoot,
                screenPosition,
                null,
                out Vector3 canvasWorldPosition))
        {
            SetHealthBarVisible(healthBar, false);
            return;
        }

        SetHealthBarVisible(healthBar, true);

        RectTransform healthBarRectTransform =
            (RectTransform)healthBar.transform;

        // 対象Enemyから引き継ぐのはTargetが確定したワールド表示位置だけ。
        // Enemyの回転・Scaleは共通Canvas上のHPバーへ反映しない。
        healthBarRectTransform.position = canvasWorldPosition;
    }

    private static void SetHealthBarVisible(
        EnemyHealthBar healthBar,
        bool isVisible)
    {
        if (healthBar != null &&
            healthBar.gameObject.activeSelf != isVisible)
        {
            healthBar.gameObject.SetActive(isVisible);
        }
    }

    private void ClearHealthBars()
    {
        foreach (EnemyHealthBar healthBar in healthBars.Values)
        {
            if (healthBar != null)
            {
                Destroy(healthBar.gameObject);
            }
        }

        healthBars.Clear();
        targetsToRemove.Clear();
    }
}
