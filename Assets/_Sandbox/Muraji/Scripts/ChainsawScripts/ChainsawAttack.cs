using System.Collections.Generic;
using UnityEngine;

public class ChainsawAttack : MonoBehaviour
{
    [Header("通常攻撃の判定。食い込み検出とは別")]
    [SerializeField] private Collider hitBox;

    [Header("ヒット演出")]
    [SerializeField] private GameObject hitParticle;
    [SerializeField] private CameraShake_System cameraShake;

    [Tooltip("通常攻撃・食い込み共通のカメラシェイク時間（実時間の秒）")]
    [SerializeField, Min(0f)]
    private float duration;

    [Tooltip("食い込み専用の揺れの強さ。通常攻撃ではAttackData.cameraShackを使用")]
    [SerializeField, Min(0f)]
    private float magnitude;

    [SerializeField] private HitStop_System hitStopSystem;
    [SerializeField] private ChainsawTrajectoryRecorder trajectoryRecorder;

    [Header("食い込みの演出（0秒ならヒットストップなし）")]
    [SerializeField, Min(0f)]
    private float diggingHitStopTime = 0f;

    [SerializeField] private bool shakeOnDigging = false;

    [Header("新軌跡記録（IAttackHitReceiver送信用）")]
    [SerializeField]
    private ChainsawBladeTrajectoryRecorder bladeTrajectoryRecorder;

    [Tooltip("ONにすると、軌跡記録と命中送信の状況をConsoleへ出力する。")]
    [SerializeField] private bool enableTrajectoryDebugLog = true;

    [Tooltip("RecordSampleのログを何回ごとに出すか。1なら毎回。")]
    [SerializeField, Min(1)]
    private int sampleLogInterval = 10;

    private int sampleCallCount;
    private const string LOG_PREFIX = "[ChainsawTrail] ";

    private AttackData currentData;
    private float attackHitStopTime;
    private float attackShakeMagnitude;

    private bool attackPrepared;
    private bool hitWindowOpen;
    private bool diggingActive;

    private readonly HashSet<ChainsawDamageReceiver> hitEnemies =
        new HashSet<ChainsawDamageReceiver>();

    private void Awake()
    {
        if (hitBox == null)
        {
            Debug.LogError(
                "ChainsawAttackのHit Boxを設定してください。",
                this
            );

            enabled = false;
            return;
        }

        hitBox.enabled = false;

        if (cameraShake == null)
        {
            cameraShake =
                FindAnyObjectByType<CameraShake_System>();
        }

        if (hitStopSystem == null)
        {
            hitStopSystem =
                FindAnyObjectByType<HitStop_System>();
        }

        if (trajectoryRecorder == null)
        {
            trajectoryRecorder =
                GetComponent<ChainsawTrajectoryRecorder>();
        }

        if (bladeTrajectoryRecorder == null)
        {
            bladeTrajectoryRecorder =
                GetComponent<ChainsawBladeTrajectoryRecorder>();
        }
    }

    private void FixedUpdate()
    {
        if (bladeTrajectoryRecorder == null ||
            !bladeTrajectoryRecorder.IsRecording)
        {
            return;
        }

        bladeTrajectoryRecorder.RecordSample();
        sampleCallCount++;

        if (enableTrajectoryDebugLog &&
            sampleCallCount % sampleLogInterval == 0)
        {
            Debug.Log(
                LOG_PREFIX +
                $"記録中: サンプル数={bladeTrajectoryRecorder.RecordedSampleCount}",
                this
            );
        }
    }

    public void BeginAttack(AttackData data)
    {
        EndDigging();
        EndAttack();

        if (data == null ||
            hitBox == null ||
            !isActiveAndEnabled)
        {
            return;
        }

        currentData = data;

        // 各段の開始時点で演出値を保存する。
        attackHitStopTime = Mathf.Max(0f, data.hitStopTime);
        attackShakeMagnitude = Mathf.Max(0f, data.cameraShack);

        attackPrepared = true;

        trajectoryRecorder?.BeginRecording();
        BeginBladeRecording("攻撃開始", data);
    }

    public void EnableHitBox()
    {
        if (!attackPrepared ||
            !isActiveAndEnabled ||
            hitBox == null)
        {
            return;
        }

        hitWindowOpen = true;
        hitBox.enabled = true;
    }

    public void DisableHitBox()
    {
        hitWindowOpen = false;

        if (hitBox != null)
        {
            hitBox.enabled = false;
        }
    }

    public void EndAttack()
    {
        DisableHitBox();

        attackPrepared = false;
        currentData = null;
        attackHitStopTime = 0f;
        attackShakeMagnitude = 0f;

        hitEnemies.Clear();

        if (!diggingActive)
        {
            trajectoryRecorder?.EndRecording();
            EndBladeRecording("攻撃終了");
        }
    }

    public void BeginDigging()
    {
        EndAttack();

        diggingActive = true;

        trajectoryRecorder?.BeginRecording();
        BeginBladeRecording("食い込み開始", null);
    }

    public void EndDigging()
    {
        if (!diggingActive)
        {
            return;
        }

        diggingActive = false;

        trajectoryRecorder?.EndRecording();
        EndBladeRecording("食い込み終了");
    }

    public bool HitDigging(
        ChainsawDamageReceiver enemy,
        AttackData data,
        float multiplier,
        Vector3 point)
    {
        if (!isActiveAndEnabled ||
            !diggingActive ||
            enemy == null ||
            data == null)
        {
            return false;
        }

        ChainsawHitInfo hit =
            CreateHit(data, multiplier, point, true);

        if (!enemy.ReceiveHit(hit))
        {
            return false;
        }

        SendAttackHit(enemy, data);

        // 食い込みは既存の専用演出設定を維持。
        PlayHitEffects(
            point,
            diggingHitStopTime,
            shakeOnDigging,
            magnitude
        );

        return true;
    }

    private ChainsawHitInfo CreateHit(
        AttackData data,
        float multiplier,
        Vector3 point,
        bool digging)
    {
        var trajectory =
            trajectoryRecorder != null
                ? trajectoryRecorder.Snapshot()
                : System.Array.AsReadOnly(
                    new ChainsawTrajectorySample[0]
                );

        return new ChainsawHitInfo(
            data,
            multiplier,
            point,
            digging,
            trajectory
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        TryHitSlash(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryHitSlash(other);
    }

    private void TryHitSlash(Collider other)
    {
        if (!isActiveAndEnabled ||
            !attackPrepared ||
            !hitWindowOpen ||
            hitBox == null ||
            !hitBox.enabled ||
            Time.timeScale <= 0f)
        {
            return;
        }

        ChainsawDamageReceiver enemy =
            other.GetComponentInParent<ChainsawDamageReceiver>();

        if (enemy == null ||
            !enemy.CanReceiveHit ||
            hitEnemies.Contains(enemy))
        {
            return;
        }

        Vector3 point =
            other.ClosestPoint(hitBox.bounds.center);

        // 複数Colliderでも、同じ敵には1段につき1回。
        hitEnemies.Add(enemy);

        if (!enemy.ReceiveHit(
                CreateHit(currentData, 1f, point, false)))
        {
            hitEnemies.Remove(enemy);
            return;
        }

        SendAttackHit(other, currentData);

        PlayHitEffects(
            point,
            attackHitStopTime,
            true,
            attackShakeMagnitude
        );
    }

    private void PlayHitEffects(
        Vector3 point,
        float hitStop,
        bool shake,
        float shakeMagnitude)
    {
        if (shake &&
            cameraShake != null &&
            shakeMagnitude > 0f &&
            duration > 0f)
        {
            cameraShake.Shake(duration, shakeMagnitude);
        }

        if (hitStopSystem != null && hitStop > 0f)
        {
            hitStopSystem.StopTime(hitStop);
        }

        if (hitParticle == null)
        {
            return;
        }

        GameObject particle = Instantiate(
            hitParticle,
            point,
            Quaternion.identity
        );

        ParticleSystem particleSystem =
            particle.GetComponent<ParticleSystem>();

        float lifetime =
            particleSystem == null
                ? 2f
                : particleSystem.main.duration +
                  particleSystem.main.startLifetime.constantMax;

        Destroy(particle, lifetime);
    }

    private void BeginBladeRecording(
        string label,
        ScriptableObject attackData)
    {
        if (bladeTrajectoryRecorder == null)
        {
            return;
        }

        sampleCallCount = 0;

        bool started =
            bladeTrajectoryRecorder.BeginRecording();

        if (enableTrajectoryDebugLog)
        {
            Debug.Log(
                LOG_PREFIX +
                $"{label}: 記録開始={(started ? "成功" : "失敗")}, " +
                $"攻撃データ={(attackData != null ? attackData.name : "なし")}",
                this
            );
        }
    }

    private void EndBladeRecording(string label)
    {
        if (bladeTrajectoryRecorder == null ||
            !bladeTrajectoryRecorder.IsRecording)
        {
            return;
        }

        ChainsawAttackTrajectory finalTrajectory =
            bladeTrajectoryRecorder.EndRecording();

        if (enableTrajectoryDebugLog)
        {
            Debug.Log(
                LOG_PREFIX +
                (finalTrajectory != null
                    ? $"{label}: 最終サンプル数={finalTrajectory.SampleCount}"
                    : $"{label}: 軌跡サンプルなし"),
                this
            );
        }
    }

    private void SendAttackHit(
        Component target,
        ScriptableObject attackData)
    {
        if (bladeTrajectoryRecorder == null || target == null)
        {
            return;
        }

        if (!bladeTrajectoryRecorder.TryCreateTrajectorySnapshot(
                out ChainsawAttackTrajectory trajectory))
        {
            if (enableTrajectoryDebugLog)
            {
                Debug.LogWarning(
                    LOG_PREFIX +
                    $"命中したが軌跡サンプルが0件のため送信しません: 対象={target.name}",
                    target
                );
            }

            return;
        }

        IAttackHitReceiver receiver =
            target.GetComponentInParent<IAttackHitReceiver>();

        if (receiver == null)
        {
            if (enableTrajectoryDebugLog)
            {
                Debug.LogWarning(
                    LOG_PREFIX +
                    $"命中対象にIAttackHitReceiverがありません: 対象={target.name}",
                    target
                );
            }

            return;
        }

        receiver.ReceiveAttackHit(
            new PlayerAttackHitData(attackData, trajectory)
        );

        if (enableTrajectoryDebugLog)
        {
            Debug.Log(
                LOG_PREFIX +
                $"命中情報を送信: 対象={target.name}, " +
                $"攻撃データ={(attackData != null ? attackData.name : "なし")}, " +
                $"軌跡サンプル数={trajectory.SampleCount}",
                target
            );
        }
    }

    private void OnDisable()
    {
        EndDigging();
        EndAttack();
    }
}
