using System.Collections.ObjectModel;
using UnityEngine;

public sealed class ChainsawHitInfo
{
    public AttackData AttackData { get; }
    public float DamageMultiplier { get; }
    public Vector3 HitPoint { get; }
    public bool IsDigging { get; }
    public ReadOnlyCollection<ChainsawTrajectorySample> Trajectory { get; }

    public ChainsawHitInfo(
        AttackData data,
        float multiplier,
        Vector3 point,
        bool digging,
        ReadOnlyCollection<ChainsawTrajectorySample> trajectory)
    {
        AttackData = data;
        DamageMultiplier = multiplier;
        HitPoint = point;
        IsDigging = digging;
        Trajectory = trajectory;
    }
}


public class ChainsawDamageReceiver : MonoBehaviour
{
    [SerializeField, Min(0f)]
    private float baseDamage = 10f;

    [SerializeField]
    private bool useHealth = true;

    [SerializeField, Min(1f)]
    private float enemyMaxHealth = 100f;


    public float TestHealth { get; private set; }

    public ChainsawHitInfo LastHit { get; private set; }


    public virtual bool CanReceiveHit =>
        isActiveAndEnabled &&
        (!useHealth || TestHealth > 0f);


    protected virtual void Awake()
    {
        ResetTestHealth();
    }


    public void ResetTestHealth()
    {
        TestHealth = enemyMaxHealth;
    }


    public virtual bool ReceiveHit(ChainsawHitInfo hit)
    {
        if (!CanReceiveHit)
            return false;


        LastHit = hit;


        float damage =
            baseDamage *
            Mathf.Max(0f, hit.DamageMultiplier);


        if (useHealth)
        {
            TestHealth =
                Mathf.Max(0f, TestHealth - damage);
        }


        bool defeated =
            useHealth &&
            TestHealth <= 0f;


        // ここに直接処理を書く
        if (defeated)
        {
            OnDefeated();
        }
        else
        {
            OnDamage(damage);
        }


        return true;
    }


    protected virtual void OnDamage(float damage)
    {
        Debug.Log($"Enemy Damage : {damage}");
    }


    protected virtual void OnDefeated()
    {
        Debug.Log("Enemy Defeated");
    }
}
