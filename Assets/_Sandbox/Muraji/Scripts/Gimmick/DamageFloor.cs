using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DamageFloor : MonoBehaviour
{
    [Tooltip("HPが0になるまでの秒数と、減り方のカーブを設定するアセットです。")]
    [SerializeField] private DamageFloorData floorData;

    [Tooltip("手動で配置したダメージ領域を登録します。Is Triggerを有効にしたBox・Sphere・Capsule・Convex MeshColliderを使用してください。複数登録しても同じ対象へ重複加算しません。")]
    [SerializeField] private Collider[] damageColliders = new Collider[0];

    [Tooltip("床で実際にHPが減ったときの点滅時間です。接触中は減少するたびに延長し、床から離れるとこの秒数後に止まります。0なら床による点滅を行いません。無敵や硬直は付与しません。")]
    [SerializeField, Min(0f)] private float damageFlashDuration = 0.2f;

    private readonly List<PlayerHealth> players = new List<PlayerHealth>();
    private bool warnedMissingFlash;


    // 設定不足を通知する。既存Colliderの形状やTrigger設定は書き換えない。
    private void Awake()
    {
        if (floorData == null)
        {
            Debug.LogError("DamageFloor：ダメージ床設定を割り当ててください。", this);
            enabled = false;
            return;
        }

        if (damageColliders == null || damageColliders.Length == 0)
        {
            Debug.LogWarning("DamageFloor：ダメージ領域が未登録です。", this);
            return;
        }

        foreach (Collider zone in damageColliders)
        {
            if (!IsSupportedZone(zone))
                Debug.LogWarning("DamageFloor：未設定、非Trigger、または非対応形状の判定は無視します。", this);
        }
    }

    // 各物理更新で実際の重なりを調べ、接触中のプレイヤーだけへダメージを進める。
    private void FixedUpdate()
    {
        if (floorData == null || damageColliders == null) return;

        PlayerHealth.CopyActivePlayers(players);
        foreach (PlayerHealth player in players)
        {
            if (player == null || !player.isActiveAndEnabled || player.IsDead)
                continue;

            foreach (Collider zone in damageColliders)
            {
                if (!IsSupportedZone(zone) || !zone.enabled ||
                    !zone.gameObject.activeInHierarchy ||
                    !player.OverlapsDamageZone(zone)) continue;

                float healthBefore = player.CurrentHealth;
                player.ApplyFloorContact(floorData);

                // 重複判定などでHPが減らなかった場合や、死亡・無効化された場合は点滅させない。
                if (player != null && player.isActiveAndEnabled && !player.IsDead &&
                    player.CurrentHealth < healthBefore)
                {
                    PlayDamageFlash(player);
                }
                break;
            }
        }
    }

    // ダメージを受けたプレイヤーの共用点滅を呼ぶ。敵攻撃の無敵・硬直は変更しない。
    private void PlayDamageFlash(PlayerHealth player)
    {
        if (damageFlashDuration <= 0f) return;

        PlayerDamageMaterialFlash materialFlash =
            player.GetComponentInParent<PlayerDamageMaterialFlash>();
        if (materialFlash == null)
        {
            if (!warnedMissingFlash)
            {
                warnedMissingFlash = true;
                Debug.LogWarning(
                    "DamageFloor：被弾したPlayerHealthと同じオブジェクト、またはその親にPlayerDamageMaterialFlashを追加し、描画対象とマテリアルを設定してください。", this);
            }
            return;
        }

        materialFlash.PlayDamageFlash(damageFlashDuration);
    }

    // 重なり判定に使用できる、専用Triggerの形状かを確認する。
    private static bool IsSupportedZone(Collider zone)
    {
        return zone != null && zone.isTrigger &&
            (zone is BoxCollider || zone is SphereCollider || zone is CapsuleCollider ||
             (zone is MeshCollider mesh && mesh.convex));
    }
}
