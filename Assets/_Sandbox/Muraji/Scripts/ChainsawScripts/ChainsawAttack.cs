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

    // 通常攻撃の1段ごとに、送信済みの敵を記録する。
    private readonly HashSet<EnemyAttackHitReceiver> hitEnemies =
        new HashSet<EnemyAttackHitReceiver>();

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

    /// <summary>
    /// 食い込み対象の敵へ命中情報を送り、送信できた場合に専用演出を再生する。
    /// </summary>
    /// <remarks>
    /// 対象をComponentで受け取り、既存の食い込み側からの呼び出しを維持する。
    /// multiplierは呼び出し互換性のため保持する。
    /// 現在の受信側はAttackData.damageを使用するため、倍率は反映されない。
    /// </remarks>
    public bool HitDigging(
        Component enemy,
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

        EnemyAttackHitReceiver receiver =
            enemy.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null)
        {
            if (enableTrajectoryDebugLog)
            {
                Debug.LogWarning(
                    LOG_PREFIX +
                    $"食い込み対象自身または親にEnemyAttackHitReceiverがありません: " +
                    $"対象={enemy.name}",
                    enemy);
            }

            return false;
        }

        if (!SendAttackHit(receiver, data))
        {
            return false;
        }

        // 既存の食い込み専用演出設定を使用する。
        PlayHitEffects(
            point,
            diggingHitStopTime,
            shakeOnDigging,
            magnitude);

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

    /// <summary>
    /// 通常攻撃の命中を確認し、同じ敵へ1段につき1回だけ命中情報を送る。
    /// </summary>
    private void TryHitSlash(Collider other)
    {
        if (!isActiveAndEnabled ||
            !attackPrepared ||
            !hitWindowOpen ||
            hitBox == null ||
            !hitBox.enabled ||
            other == null ||
            Time.timeScale <= 0f)
        {
            return;
        }

        EnemyAttackHitReceiver receiver =
            other.GetComponentInParent<EnemyAttackHitReceiver>();

        if (receiver == null ||
            !receiver.isActiveAndEnabled ||
            hitEnemies.Contains(receiver))
        {
            return;
        }

        Vector3 point =
            other.ClosestPoint(hitBox.bounds.center);

        // 送信先の処理中に再度呼ばれても重複送信しないよう、先に登録する。
        hitEnemies.Add(receiver);

        if (!SendAttackHit(receiver, currentData))
        {
            // 送信できなかった場合は、次の接触通知で再試行できるようにする。
            hitEnemies.Remove(receiver);
            return;
        }

        PlayHitEffects(
            point,
            attackHitStopTime,
            true,
            attackShakeMagnitude);
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

    /// <summary>
    /// 生存中の敵へ、攻撃データと軌跡をIAttackHitReceiver経由で送信する。
    /// </summary>
    /// <returns>
    /// 命中情報を送信した場合はtrue。実際のHP変更は受信側が担当する。
    /// </returns>
    private bool SendAttackHit(
        EnemyAttackHitReceiver target,
        AttackData attackData)
    {
        if (target == null ||
            !target.isActiveAndEnabled ||
            attackData == null)
        {
            return false;
        }

        EnemyHealth enemyHealth =
            target.GetComponent<EnemyHealth>();

        if (enemyHealth == null || enemyHealth.IsDead)
        {
            return false;
        }

        if (bladeTrajectoryRecorder == null)
        {
            if (enableTrajectoryDebugLog)
            {
                Debug.LogWarning(
                    LOG_PREFIX +
                    "Blade Trajectory Recorderが未設定のため送信できません。",
                    this);
            }

            return false;
        }

        if (!bladeTrajectoryRecorder.TryCreateTrajectorySnapshot(
                out ChainsawAttackTrajectory trajectory))
        {
            // 攻撃開始直後など、まだ物理更新で記録されていない場合は、
            // 記録中であることを確認して命中時点のサンプルを追加する。
            if (bladeTrajectoryRecorder.IsRecording)
            {
                bladeTrajectoryRecorder.RecordSample();
            }

            if (!bladeTrajectoryRecorder.TryCreateTrajectorySnapshot(
                    out trajectory))
            {
                if (enableTrajectoryDebugLog)
                {
                    Debug.LogWarning(
                        LOG_PREFIX +
                        $"軌跡を取得できないため送信できません: 対象={target.name}",
                        target);
                }

                return false;
            }
        }

        // 敵の受信実装を、既存の共通インターフェース経由で呼び出す。
        IAttackHitReceiver receiver = target;

        receiver.ReceiveAttackHit(
            new PlayerAttackHitData(attackData, trajectory));

        if (enableTrajectoryDebugLog)
        {
            Debug.Log(
                LOG_PREFIX +
                $"命中情報を送信: 対象={target.name}, " +
                $"攻撃データ={attackData.name}, " +
                $"軌跡サンプル数={trajectory.SampleCount}",
                target);
        }

        return true;
    }
    private void OnDisable()
    {
        EndDigging();
        EndAttack();
    }
}
