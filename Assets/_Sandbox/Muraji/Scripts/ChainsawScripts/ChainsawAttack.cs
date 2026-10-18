using System.Collections.Generic;
using UnityEngine;

public class ChainsawAttack : MonoBehaviour
{
    [Header("通常攻撃の判定。食い込み検出とは別")]
    [SerializeField] private Collider hitBox;

    [Header("ヒット演出")]
    [SerializeField] private GameObject hitParticle;
    [Tooltip("ゲーム画面のカメラを揺らすコンポーネントを指定してください。未設定なら自動取得します。")]
    [SerializeField] private CameraShake_System cameraShake;

    [Tooltip("通常攻撃・食い込み共通の揺れ時間です。0なら0.15秒を使用します。揺れの強さは各攻撃SOで設定します。")]
    [SerializeField, Min(0f)]
    private float duration = 0.15f;

    [Tooltip("命中時に時間を停止するコンポーネントを指定してください。未設定なら自動取得します。")]
    [SerializeField] private HitStop_System hitStopSystem;
    private const float DEFAULT_SHAKE_DURATION = 0.15f;

    // 設定不足の警告を、食い込みの連続命中で繰り返さないための記録。
    private bool shakeWarningLogged;
    private bool hitStopWarningLogged;

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
    }

    public void BeginDigging()
    {
        EndAttack();

        diggingActive = true;
    }

    public void EndDigging()
    {
        if (!diggingActive)
        {
            return;
        }

        diggingActive = false;
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
            return false;
        }

        if (!SendAttackHit(receiver, data))
        {
            return false;
        }

        // 食い込みも、呼び出し元が渡した攻撃SOの演出設定を使用する。
        PlayHitEffects(
            point,
            Mathf.Max(0f, data.hitStopTime),
            true,
            Mathf.Max(0f, data.cameraShack));

        return true;
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

        // 受信側で攻撃が中断されても、今回の命中に使う演出値を維持する。
        float hitStopTime = attackHitStopTime;
        float shakeMagnitude = attackShakeMagnitude;

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
            hitStopTime,
            true,
            shakeMagnitude);
    }
    // 命中送信が成功したときに、攻撃SOの設定で揺れ・時間停止・命中エフェクトを実行する。
    private void PlayHitEffects(
        Vector3 point,
        float hitStop,
        bool shake,
        float shakeMagnitude)
    {
        if (shake && shakeMagnitude > 0f)
        {
            if (cameraShake != null && cameraShake.isActiveAndEnabled)
            {
                float shakeDuration = duration > 0f
                    ? duration
                    : DEFAULT_SHAKE_DURATION;

                cameraShake.Shake(shakeDuration, shakeMagnitude);
            }
            else if (!shakeWarningLogged)
            {
                shakeWarningLogged = true;
                Debug.LogWarning(
                    "命中しましたがカメラシェイクを実行できません。"
                    + "Camera Shakeにゲーム画面の有効なCameraShake_Systemを指定してください。",
                    this);
            }
        }

        if (hitStop > 0f)
        {
            if (hitStopSystem != null && hitStopSystem.isActiveAndEnabled)
            {
                hitStopSystem.StopTime(hitStop);
            }
            else if (!hitStopWarningLogged)
            {
                hitStopWarningLogged = true;
                Debug.LogWarning(
                    "命中しましたがヒットストップを実行できません。"
                    + "Hit Stop Systemに有効なHitStop_Systemを指定してください。",
                    this);
            }
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

    /// <summary>
    /// 生存中の敵へ、攻撃データをIAttackHitReceiver経由で送信する。
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

        // 敵の受信実装を、既存の共通インターフェース経由で呼び出す。
        IAttackHitReceiver receiver = target;

        receiver.ReceiveAttackHit(
            new PlayerAttackHitData(attackData));

        return true;
    }
    private void OnDisable()
    {
        EndDigging();
        EndAttack();
    }
}
