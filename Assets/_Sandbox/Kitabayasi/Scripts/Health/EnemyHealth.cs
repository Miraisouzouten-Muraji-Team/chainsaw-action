using System;
using UnityEngine;

/// <summary>
/// EnemyのHP管理と死亡判定を担当する。
/// </summary>
/// <remarks>
/// EnemyDataReferenceからEnemyDataを取得し、
/// EnemyDataに設定された最大HPを使用して初期化する。
///
/// 現在HPの保持・変更と、HP変化および死亡の通知を担当する。
/// UI表示や死亡後のState遷移・演出は担当しない。
/// </remarks>
[RequireComponent(typeof(EnemyDataReference))]
public class EnemyHealth : MonoBehaviour
{
    private EnemyDataReference enemyDataReference;

    private int maxHealth;
    private int currentHealth;
    private bool isDead;

    // Awakeの実行順に依存せず、安全にHPを参照できるよう初期化済みかを保持する。
    private bool isInitialized;

    // EnemyDataが正常に取得できたかを保持する。
    private bool hasValidData;

    // HPが変化したときに、変更前HPと変更後HPを通知する。
    public event Action<int, int> HealthChanged;

    // HPが0になり、死亡が成立したときに通知する。
    public event Action Died;

    public int CurrentHealth
    {
        get
        {
            InitializeHealth();
            return currentHealth;
        }
    }

    public int MaxHealth
    {
        get
        {
            InitializeHealth();
            return maxHealth;
        }
    }

    public bool IsDead
    {
        get
        {
            InitializeHealth();
            return isDead;
        }
    }

    private void Awake()
    {
        InitializeHealth();
    }

    /// <summary>
    /// Enemyにダメージを適用する。
    /// </summary>
    /// <param name="damage">適用するダメージ量。</param>
    public void TakeDamage(int damage)
    {
        InitializeHealth();

        // EnemyDataが正常に取得できていない場合は処理しない。
        if (!hasValidData)
        {
            return;
        }

        // 無効なダメージと、死亡後の追加ダメージは処理しない。
        if (damage <= 0 || isDead)
        {
            return;
        }

        int previousHealth = currentHealth;

        // HPが0未満にならないよう下限を0にする。
        currentHealth = Mathf.Max(0, currentHealth - damage);

        // HP変化を通知する。
        HealthChanged?.Invoke(previousHealth, currentHealth);

        if (currentHealth > 0)
        {
            return;
        }

        // Diedを一度だけ通知するため、通知前に死亡状態を確定する。
        isDead = true;
        Died?.Invoke();
    }

    /// <summary>
    /// EnemyDataから最大HPを取得し、このEnemy個体のHPを初期化する。
    /// </summary>
    private void InitializeHealth()
    {
        // 複数箇所から呼ばれても初期化は一度だけ行う。
        if (isInitialized)
        {
            return;
        }

        enemyDataReference = GetComponent<EnemyDataReference>();

        if (enemyDataReference == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealth)}: " +
                $"{nameof(EnemyDataReference)} が見つかりません。",
                this);

            SetFallbackHealth();
            return;
        }

        EnemyData enemyData = enemyDataReference.Data;

        if (enemyData == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealth)}: " +
                $"{nameof(EnemyDataReference)} に " +
                $"{nameof(EnemyData)} が設定されていません。",
                this);

            SetFallbackHealth();
            return;
        }

        // EnemyDataから最大HPを取得し、
        // このEnemy個体の現在HPを最大HPで初期化する。
        maxHealth = Mathf.Max(1, enemyData.MaxHealth);
        currentHealth = maxHealth;

        hasValidData = true;
        isInitialized = true;
    }

    /// <summary>
    /// EnemyDataを取得できなかった場合に、
    /// HP割合計算などで0除算が発生しない安全な値を設定する。
    /// </summary>
    private void SetFallbackHealth()
    {
        maxHealth = 1;
        currentHealth = maxHealth;

        hasValidData = false;
        isInitialized = true;
    }
}
