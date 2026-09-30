using System;
using UnityEngine;

/// <summary>
/// EnemyのHP管理と死亡判定を担当する。
/// </summary>
/// <remarks>
/// HPの保持・変更と、HP変化および死亡の通知を行う。
/// UI表示や死亡後のState遷移・演出は担当しない。
/// </remarks>
public class EnemyHealth : MonoBehaviour
{
    [Header("HP設定")]
    [Tooltip("敵の最大HP。1以上を設定してください。")]
    [SerializeField, Min(1)]
    private int maxHealth = 20;

    private int currentHealth;
    private bool isDead;

    // Awakeの実行順に依存せず、安全にHPを参照できるよう初期化済みかを保持する。
    private bool isInitialized;

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

    // Enemyにダメージを適用する。
    public void TakeDamage(int damage)
    {
        InitializeHealth();

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

    private void InitializeHealth()
    {
        // 複数箇所から呼ばれても初期化は一度だけ行う。
        if (isInitialized)
        {
            return;
        }

        // Inspector以外から不正な値が入っても、最大HPが1未満にならないよう保証する。
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;

        isInitialized = true;
    }
}
