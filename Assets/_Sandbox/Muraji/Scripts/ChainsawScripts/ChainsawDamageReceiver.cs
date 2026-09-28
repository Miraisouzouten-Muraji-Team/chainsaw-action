using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Events;

// AttackDataのフィールドはhitStopTime以外が未提供なので、基礎ダメージは受信側で決定。
public sealed class ChainsawHitInfo
{
    public AttackData AttackData { get; }
    public float DamageMultiplier { get; }
    public Vector3 HitPoint { get; }
    public bool IsDigging { get; }
    public ReadOnlyCollection<ChainsawTrajectorySample> Trajectory { get; }
    public ChainsawHitInfo(AttackData data, float multiplier, Vector3 point, bool digging,
        ReadOnlyCollection<ChainsawTrajectorySample> trajectory)
    {
        AttackData = data;
        DamageMultiplier = multiplier;
        HitPoint = point;
        IsDigging = digging;
        Trajectory = trajectory;
    }
}

// 敵ルートに追加。既存HPへ接続する場合はUse Test HealthをOFFにする。
// 独自クラスで継承しReceiveHitをoverrideすればSOと軌跡もそのまま利用可能。
public class ChainsawDamageReceiver : MonoBehaviour
{
    [SerializeField, Min(0f)] private float baseDamage = 10f;
    [SerializeField] private bool useTestHealth = true;
    [SerializeField, Min(1f)] private float testMaxHealth = 100f;
    [SerializeField] private UnityEvent<float> onDamage = new UnityEvent<float>();
    [SerializeField] private UnityEvent onDefeated = new UnityEvent();
    public float TestHealth { get; private set; }
    public ChainsawHitInfo LastHit { get; private set; }
    public virtual bool CanReceiveHit => isActiveAndEnabled && (!useTestHealth || TestHealth > 0f);
    protected virtual void Awake() { ResetTestHealth(); }
    public void ResetTestHealth() { TestHealth = testMaxHealth; }

    public virtual bool ReceiveHit(ChainsawHitInfo hit)
    {
        if (!CanReceiveHit) return false;
        LastHit = hit;
        float damage = baseDamage * Mathf.Max(0f, hit.DamageMultiplier);
        bool defeated = false;
        if (useTestHealth)
        {
            TestHealth = Mathf.Max(0f, TestHealth - damage);
            defeated = TestHealth <= 0f;
        }
        onDamage.Invoke(damage);
        if (defeated) onDefeated.Invoke();
        return true;
    }
}
