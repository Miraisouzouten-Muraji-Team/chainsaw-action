using UnityEngine;

// 敵の子に置いたColliderごとに、敵食い込み・被攻撃・壁食い込みの役割を指定する。
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ChainsawContactZone : MonoBehaviour
{
    [Header("このColliderの用途")]
    [Tooltip("通常の敵食い込みの開始を許可します。敵本体のEnemyAttackHitReceiverの子に配置してください。")]
    [SerializeField] private bool allowEnemyDigging = true;
    [Tooltip("プレイヤーからの弱攻撃と移動食い込みの接触ダメージを受け付けます。敵からプレイヤーへの攻撃設定ではありません。")]
    [SerializeField] private bool allowAttack = true;
    [Tooltip("このTriggerを壁食い込みの対象にします。盾などにはこれだけを有効にしてください。")]
    [SerializeField] private bool allowWallDigging;

    [Header("壁食い込みの設定")]
    [Tooltip("壁の外側へ向く方向です。標準ではワールド座標で、左側から登る面はX=-1、右側から登る面はX=1にします。モデルの親の回転には影響されません。")]
    [InspectorName("壁の外向き")]
    [SerializeField] private Vector3 localWallNormal = Vector3.left;
    [Tooltip("通常はオフにします。オンの場合だけ、壁の外向きをこのモデルのローカル座標として扱い、親の回転も反映します。")]
    [SerializeField] private bool useLocalWallNormal;
    [Tooltip("プレイヤー本体の原点を合わせる専用の空オブジェクトです。未設定、盾自身、その親、プレイヤーを指定した場合は、盾のCollider外側へ自動で合わせます。")]
    [SerializeField] private Transform playerEntryTarget;
    [Tooltip("オンなら移動先の高さにも合わせます。オフなら横位置だけを合わせ、接触した高さから壁登りを始めます。奥行きは変更しません。")]
    [SerializeField] private bool alignEntryHeight;

    // Colliderと同じオブジェクトの設定だけを調べ、親の設定を別の判定へ誤適用しない。
    public static ChainsawContactZone GetZone(Collider target)
    {
        return target != null ? target.GetComponent<ChainsawContactZone>() : null;
    }

    // 用途を分けた敵では未指定の本体Colliderを除外し、従来の未設定の敵はそのまま受け付ける。
    private static bool AllowsLegacyCollider(Collider target)
    {
        if (target == null) return false;
        EnemyAttackHitReceiver enemy = target.GetComponentInParent<EnemyAttackHitReceiver>();
        return enemy == null || enemy.GetComponentInChildren<ChainsawContactZone>(true) == null;
    }

    // 通常の敵食い込みを開始してよいColliderかを返す。
    public static bool AllowsEnemyDigging(Collider target)
    {
        ChainsawContactZone zone = GetZone(target);
        return zone != null
            ? zone.isActiveAndEnabled && zone.allowEnemyDigging
            : AllowsLegacyCollider(target);
    }

    // 弱攻撃と移動食い込みの命中を受け付けるColliderかを返す。
    public static bool AllowsAttack(Collider target)
    {
        ChainsawContactZone zone = GetZone(target);
        return zone != null
            ? zone.isActiveAndEnabled && zone.allowAttack
            : AllowsLegacyCollider(target);
    }

    // 明示した壁用Triggerだけを許可し、外向きの水平法線を返す。
    public bool TryGetWallNormal(Collider target, out Vector3 normal)
    {
        normal = Vector3.zero;
        if (!isActiveAndEnabled || !allowWallDigging || target == null ||
            target.gameObject != gameObject || !target.isTrigger ||
            !target.enabled || !target.gameObject.activeInHierarchy)
            return false;

        Vector3 direction = useLocalWallNormal
            ? transform.TransformDirection(localWallNormal)
            : localWallNormal;
        if (Mathf.Abs(direction.x) < 0.001f) return false;
        normal = Vector3.right * Mathf.Sign(direction.x);
        return true;
    }

    // 選択時に実際の壁の外向きを表示し、モデル階層の回転による設定間違いを確認できるようにする。
    private void OnDrawGizmosSelected()
    {
        Collider zoneCollider = GetComponent<Collider>();
        if (!TryGetWallNormal(zoneCollider, out Vector3 normal)) return;
        Vector3 origin = zoneCollider.bounds.center;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + normal * 0.5f);
        Gizmos.DrawSphere(origin + normal * 0.5f, 0.025f);
    }

    // 専用マーカーが有効な場合だけ、その横位置と任意の高さを返す。無効なら検出器が自動配置する。
    public bool TryGetEntryPosition(Vector3 currentPosition, out Vector3 position)
    {
        position = currentPosition;
        if (!isActiveAndEnabled || !allowWallDigging || playerEntryTarget == null ||
            playerEntryTarget == transform || transform.IsChildOf(playerEntryTarget) ||
            playerEntryTarget.GetComponentInParent<SensorPlayerController>() != null)
            return false;
        position.x = playerEntryTarget.position.x;
        if (alignEntryHeight) position.y = playerEntryTarget.position.y;
        return true;
    }
}
