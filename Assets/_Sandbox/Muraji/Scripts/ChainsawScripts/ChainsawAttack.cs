using UnityEngine;

public class ChainsawAttack : MonoBehaviour
{
    [Header("攻撃判定")]
    [SerializeField] Collider hitBox;

    [Header("ヒットエフェクト")]
    [SerializeField] GameObject hitParticle;

    [Header("カメラシェイク")]
    [SerializeField] CameraShake cameraShake;

    [SerializeField] float duration;
    [SerializeField] float magnitude;

    [Header("ヒットストップ")]
    [SerializeField] HitStop_System hitStopSystem;

    // その攻撃の開始時に値をコピーして保持する。
    float attackHitStopTime;

    bool attackPrepared;
    bool hitWindowOpen;

    void Awake()
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

        // 同名のローカル変数を作らず、フィールドへ代入。
        // Inspectorに設定済みなら、そちらを優先する。
        if (cameraShake == null)
        {
            cameraShake = FindAnyObjectByType<CameraShake>();
        }

        if (hitStopSystem == null)
        {
            hitStopSystem = FindAnyObjectByType<HitStop_System>();
        }

        if (hitStopSystem == null)
        {
            Debug.LogWarning(
                "HitStop Systemを設定してください。",
                this
            );
        }
    }

    // 各段の攻撃開始時に呼ぶ。
    public void BeginAttack(AttackData data)
    {
        EndAttack();

        if (data == null ||
            hitBox == null ||
            !isActiveAndEnabled)
        {
            return;
        }

        attackHitStopTime = Mathf.Max(0f, data.hitStopTime);
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
        attackHitStopTime = 0f;
    }

    void OnDisable()
    {
        EndAttack();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled ||
            !attackPrepared ||
            !hitWindowOpen ||
            hitBox == null ||
            !hitBox.enabled ||
            !other.CompareTag("Enemy"))
        {
            return;
        }

        Vector3 hitPosition =
            other.ClosestPoint(transform.position);

        if (cameraShake != null)
        {
            cameraShake.Shake(duration, magnitude);
        }

        // 命中時にController.CurrentAttackDataを読み直さない。
        // 攻撃開始時に確定した時間を使う。
        // 0秒の場合はStopTime自体を呼ばない。
        if (hitStopSystem != null && attackHitStopTime > 0f)
        {
            hitStopSystem.StopTime(attackHitStopTime);
        }

        if (hitParticle != null)
        {
            GameObject particle = Instantiate(
                hitParticle,
                hitPosition,
                Quaternion.identity
            );

            ParticleSystem particleSystem =
                particle.GetComponent<ParticleSystem>();

            float lifetime = particleSystem != null
                ? particleSystem.main.duration +
                  particleSystem.main.startLifetime.constantMax
                : 2f;

            Destroy(particle, lifetime);
        }
    }
}