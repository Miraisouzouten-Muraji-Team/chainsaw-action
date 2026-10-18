using UnityEngine;

/// <summary>
/// 1体のEnemyについて、共通HPバーが参照するEnemyHealthと表示位置を提供する。
/// </summary>
/// <remarks>
/// HPバーの生成・描画・追従処理は担当しない。
/// 有効化中のみEnemyHealthBarCanvasへ登録する。
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyHealthBarTarget : MonoBehaviour
{
    [Header("表示位置")]
    [Tooltip("HPバーを表示するワールド位置。頭上などに配置したTransformを指定する。")]
    [SerializeField]
    private Transform healthBarAnchor;

    private EnemyHealth enemyHealth;
    private bool isInitialized;

    public EnemyHealth EnemyHealth => enemyHealth;
    public Transform HealthBarAnchor => healthBarAnchor;

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

        if (healthBarAnchor == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBarTarget)}: " +
                $"{nameof(healthBarAnchor)} が設定されていません。",
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
