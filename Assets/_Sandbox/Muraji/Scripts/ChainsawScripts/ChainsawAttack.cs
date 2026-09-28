using System.Collections.Generic;
using UnityEngine;

public class ChainsawAttack : MonoBehaviour
{
    [Header("通常攻撃の判定。食い込み検出とは別")]
    [SerializeField] private Collider hitBox;
    [Header("ヒット演出")]
    [SerializeField] private GameObject hitParticle;
    [SerializeField] private CameraShake_System cameraShake;
    [SerializeField] private float duration;
    [SerializeField] private float magnitude;
    [SerializeField] private HitStop_System hitStopSystem;
    [SerializeField] private ChainsawTrajectoryRecorder trajectoryRecorder;
    [Header("食い込みの演出（0秒ならヒットストップなし）")]
    [SerializeField, Min(0f)] private float diggingHitStopTime = 0f;
    [SerializeField] private bool shakeOnDigging = false;
    private AttackData currentData;
    private float attackHitStopTime;
    private bool attackPrepared;
    private bool hitWindowOpen;
    private bool diggingActive;
    private readonly HashSet<ChainsawDamageReceiver> hitEnemies = new HashSet<ChainsawDamageReceiver>();

    private void Awake()
    {
        if (hitBox == null)
        { Debug.LogError("ChainsawAttackのHit Boxを設定してください。", this); enabled = false; return; }
        hitBox.enabled = false;
        if (cameraShake == null) cameraShake = FindAnyObjectByType<CameraShake_System>();
        if (hitStopSystem == null) hitStopSystem = FindAnyObjectByType<HitStop_System>();
        if (trajectoryRecorder == null) trajectoryRecorder = GetComponent<ChainsawTrajectoryRecorder>();
    }

    public void BeginAttack(AttackData data)
    {
        EndDigging();
        EndAttack();
        if (data == null || hitBox == null || !isActiveAndEnabled) return;
        currentData = data;
        attackHitStopTime = Mathf.Max(0f, data.hitStopTime);
        attackPrepared = true;
        trajectoryRecorder?.BeginRecording();
    }
    public void EnableHitBox()
    {
        if (!attackPrepared || !isActiveAndEnabled || hitBox == null) return;
        hitWindowOpen = true;
        hitBox.enabled = true;
    }
    public void DisableHitBox()
    {
        hitWindowOpen = false;
        if (hitBox != null) hitBox.enabled = false;
    }
    public void EndAttack()
    {
        DisableHitBox();
        attackPrepared = false;
        currentData = null;
        attackHitStopTime = 0f;
        hitEnemies.Clear();
        if (!diggingActive) trajectoryRecorder?.EndRecording();
    }
    public void BeginDigging()
    {
        EndAttack();
        diggingActive = true;
        trajectoryRecorder?.BeginRecording();
    }
    public void EndDigging()
    {
        if (!diggingActive) return;
        diggingActive = false;
        trajectoryRecorder?.EndRecording();
    }
    public bool HitDigging(ChainsawDamageReceiver enemy, AttackData data, float multiplier, Vector3 point)
    {
        if (!isActiveAndEnabled || !diggingActive || enemy == null || data == null) return false;
        ChainsawHitInfo hit = CreateHit(data, multiplier, point, true);
        if (!enemy.ReceiveHit(hit)) return false;
        PlayHitEffects(point, diggingHitStopTime, shakeOnDigging);
        return true;
    }
    private ChainsawHitInfo CreateHit(AttackData data, float multiplier, Vector3 point, bool digging)
    {
        var trajectory = trajectoryRecorder != null ? trajectoryRecorder.Snapshot() :
            System.Array.AsReadOnly(new ChainsawTrajectorySample[0]);
        return new ChainsawHitInfo(data, multiplier, point, digging, trajectory);
    }
    private void OnTriggerEnter(Collider other) { TryHitSlash(other); }
    private void OnTriggerStay(Collider other) { TryHitSlash(other); }
    private void TryHitSlash(Collider other)
    {
        if (!isActiveAndEnabled || !attackPrepared || !hitWindowOpen ||
            hitBox == null || !hitBox.enabled || Time.timeScale <= 0f) return;
        ChainsawDamageReceiver enemy = other.GetComponentInParent<ChainsawDamageReceiver>();
        if (enemy == null || !enemy.CanReceiveHit || hitEnemies.Contains(enemy)) return;
        Vector3 point = other.ClosestPoint(hitBox.bounds.center);
        // 複数Colliderでも、同じ敵には1段につき1回。
        hitEnemies.Add(enemy);
        if (!enemy.ReceiveHit(CreateHit(currentData, 1f, point, false)))
        { hitEnemies.Remove(enemy); return; }
        PlayHitEffects(point, attackHitStopTime, true);
    }
    private void PlayHitEffects(Vector3 point, float hitStop, bool shake)
    {
        if (shake && cameraShake != null) cameraShake.Shake(duration, magnitude);
        if (hitStopSystem != null && hitStop > 0f) hitStopSystem.StopTime(hitStop);
        if (hitParticle == null) return;
        GameObject particle = Instantiate(hitParticle, point, Quaternion.identity);
        ParticleSystem particleSystem = particle.GetComponent<ParticleSystem>();
        float lifetime = particleSystem == null ? 2f :
            particleSystem.main.duration + particleSystem.main.startLifetime.constantMax;
        Destroy(particle, lifetime);
    }
    private void OnDisable() { EndDigging(); EndAttack(); }
}
