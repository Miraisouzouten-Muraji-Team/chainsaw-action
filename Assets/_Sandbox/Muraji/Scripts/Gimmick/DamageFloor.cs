using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DamageFloor : MonoBehaviour
{
    [Tooltip("HPが0になるまでの秒数と、減り方のカーブを設定するアセットです。")]
    [SerializeField] private DamageFloorData floorData;

    [Tooltip("手動で配置したダメージ領域を登録します。Is Triggerを有効にしたBox・Sphere・Capsule・Convex MeshColliderを使用してください。複数登録しても同じ対象へ重複加算しません。")]
    [SerializeField] private Collider[] damageColliders = new Collider[0];

    private readonly List<PlayerHealth> players = new List<PlayerHealth>();

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

                player.ApplyFloorContact(floorData);
                break;
            }
        }
    }

    // 重なり判定に使用できる、専用Triggerの形状かを確認する。
    private static bool IsSupportedZone(Collider zone)
    {
        return zone != null && zone.isTrigger &&
            (zone is BoxCollider || zone is SphereCollider || zone is CapsuleCollider ||
             (zone is MeshCollider mesh && mesh.convex));
    }
}
