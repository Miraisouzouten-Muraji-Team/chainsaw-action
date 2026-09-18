using UnityEngine;

public class ChainsawAttack : MonoBehaviour
{
    [Header("攻撃判定")]
    [SerializeField] Collider hitBox;

    [Header("ヒットエフェクト")]
    [SerializeField] GameObject hitParticle;

    [Header("カメラシェイク")]
    [SerializeField] CameraShake cameraShake;

    [Header("横揺れの強さ")]
    [SerializeField] float duration;

    [Header("縦揺れの強さ")]
    [SerializeField] float magnitude;

    [Header("ヒットストップ")]
    [SerializeField] HitStop_System hitStopSystem;

    [Header("ヒットストップの時間")]
    [SerializeField] float hitStopDuration;

    [Header("プレイヤーコントローラー")]
    [SerializeField] PlayerController playerController;


    void Awake()
    {
        // 最初は攻撃判定OFF
        hitBox.enabled = false;

        // CameraShake取得
        CameraShake cameraShake = FindAnyObjectByType<CameraShake>();

        // HitStop_System取得
        HitStop_System hitstopSystem = FindAnyObjectByType<HitStop_System>();

        // PlayerController取得
        PlayerController playerController = FindAnyObjectByType<PlayerController>();
    }

    // Animation Eventから呼ぶ
    public void EnableHitBox()
    {
        hitBox.enabled = true;

    }

    
    // Animation Eventから呼ぶ
    public void DisableHitBox()
    {
        hitBox.enabled = false;
    }

    // 敵に当たった瞬間
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Enemy"))
        {
            return;
        }

        // 敵のCollider表面に一番近い位置を取得
        Vector3 hitPosition =
            other.ClosestPoint(transform.position);

        // カメラシェイク
        cameraShake.Shake(duration, magnitude);

        // 攻撃データの取得
        

        // ヒットストップ
        AttackData data=playerController.CurrentAttackData;
        hitStopSystem.StopTime(data.hitStopTime);

        // パーティクル生成
        if (hitParticle != null)
        {
            GameObject particle = Instantiate(
                hitParticle,
                hitPosition,
                Quaternion.identity
            );
            // パーティクルの再生時間が終わったら自動で削除する
            ParticleSystem particleSystem =particle.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                Destroy(
                    particle,
                    particleSystem.main.duration + particleSystem.main.startLifetime.constantMax
                    );
            }
            else
            {
                Destroy(particle, 2.0f);
            }
        }
    }
}