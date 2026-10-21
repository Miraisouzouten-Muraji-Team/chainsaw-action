using UnityEngine;

/// <summary>
/// 1体のEnemyについて、共通HPバーが参照するEnemyHealthと最終表示位置を提供する。
/// </summary>
/// <remarks>
/// Health Bar Anchorが設定されている場合はAnchor位置を優先し、
/// 未設定の場合はEnemy位置にDefault Position Offsetを加えた位置を提供する。
/// HPバーの生成・描画・追従処理は担当しない。
/// 有効化中のみEnemyHealthBarCanvasへ登録する。
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyHealthBarTarget : MonoBehaviour
{
    [Header("表示位置")]
    [Tooltip("特殊なEnemyでHPバー位置を個別指定する場合に使用する任意のTransform。未設定ならDefault Position Offsetを使用する。")]
    [SerializeField]
    private Transform healthBarAnchor;

    [Tooltip("Health Bar Anchor未設定時に、Enemyのワールド位置へ加算する表示位置Offset。")]
    [SerializeField]
    private Vector3 defaultPositionOffset;

    private EnemyHealth enemyHealth;
    private bool isInitialized;

    public EnemyHealth EnemyHealth => enemyHealth;
    public Transform HealthBarAnchor => healthBarAnchor;

    /// <summary>
    /// 共通HPバーが使用する最終的なワールド表示位置を取得する。
    /// </summary>
    public Vector3 HealthBarWorldPosition =>
        healthBarAnchor != null
            ? healthBarAnchor.position
            : transform.position + defaultPositionOffset;

    private void Awake()
    {
        enemyHealth = GetComponent<EnemyHealth>();

        if (enemyHealth == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarTarget)}: " +
                $"{nameof(EnemyHealth)} が見つかりません。",
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

        EnemyHealthBarCanvas.RegisterTarget(this);
    }

    private void OnDisable()
    {
        EnemyHealthBarCanvas.UnregisterTarget(this);
    }
}
