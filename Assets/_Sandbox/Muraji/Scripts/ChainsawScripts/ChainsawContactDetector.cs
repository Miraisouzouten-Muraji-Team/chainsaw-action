using UnityEngine;

public enum ChainsawSurface { None, Floor, Wall, Ceiling, Enemy }

public struct ChainsawContact
{
    public Collider Collider;
    public ChainsawDamageReceiver Enemy;
    public ChainsawSurface Surface;
    public Vector3 Point;
    public Vector3 Normal;
}

public class ChainsawContactDetector : MonoBehaviour
{
    [Header("刃の根元／先端。プレイヤー本体ではなく刃の位置に置く")]
    [SerializeField] private Transform root;
    [SerializeField] private Transform tip;
    [SerializeField] private Transform ownerRoot;
    [SerializeField] private LayerMask terrainLayers;
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField, Min(0.01f)] private float probeRadius = 0.08f;
    [SerializeField, Min(0.01f)] private float probeDistance = 0.2f;
    [SerializeField, Range(0.1f, 0.95f)] private float surfaceNormalThreshold = 0.7f;
    private static readonly Vector3[] DIRECTIONS = { Vector3.down, Vector3.up, Vector3.left, Vector3.right };

    private void Awake()
    {
        if (ownerRoot == null) ownerRoot = transform;
        if (root == null || tip == null)
        {
            Debug.LogError("ChainsawContactDetectorのRootとTipを設定してください。", this);
            enabled = false;
        }
        if (terrainLayers.value == 0 || enemyLayers.value == 0)
            Debug.LogWarning("食い込み検出のTerrain Layers / Enemy Layersを設定してください。", this);
    }

    public bool TryGetContact(Collider preferred, out ChainsawContact contact)
    {
        contact = default;
        if (root == null || tip == null) return false;
        float bestScore = float.PositiveInfinity;
        foreach (Collider collider in Physics.OverlapCapsule(root.position, tip.position,
            probeRadius, enemyLayers, QueryTriggerInteraction.Collide))
        {
            if (IsOwner(collider)) continue;
            ChainsawDamageReceiver enemy = collider.GetComponentInParent<ChainsawDamageReceiver>();
            if (enemy == null || !enemy.CanReceiveHit) continue;
            Vector3 point = collider.ClosestPoint(tip.position);
            float score = collider == preferred ? -100f : -1f + Vector3.Distance(tip.position, point);
            if (score >= bestScore) continue;
            bestScore = score;
            contact = new ChainsawContact
            {
                Collider = collider,
                Enemy = enemy,
                Surface = ChainsawSurface.Enemy,
                Point = point,
                Normal = Vector3.zero
            };
        }
        for (int sampleIndex = 0; sampleIndex < 3; sampleIndex++)
        {
            Vector3 origin = Vector3.Lerp(root.position, tip.position, sampleIndex * 0.5f);
            foreach (Vector3 direction in DIRECTIONS)
            {
                // triggerは地形として扱わない。SphereCastのnormalで床／壁／天井を分類。
                float backoff = probeRadius + 0.01f;
                Vector3 castOrigin = origin - direction * backoff;
                foreach (RaycastHit hit in Physics.SphereCastAll(castOrigin, probeRadius, direction,
                    probeDistance + backoff, terrainLayers, QueryTriggerInteraction.Ignore))
                {
                    if (IsOwner(hit.collider) || hit.distance <= 0.0001f) continue;
                    // Z方向の面は2D移動面の地形として扱わない。
                    if (Mathf.Abs(hit.normal.z) > 0.5f) continue;
                    float score = hit.collider == preferred ? -100f : hit.distance;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    ChainsawSurface surface = hit.normal.y >= surfaceNormalThreshold ? ChainsawSurface.Floor :
                        hit.normal.y <= -surfaceNormalThreshold ? ChainsawSurface.Ceiling : ChainsawSurface.Wall;
                    contact = new ChainsawContact
                    {
                        Collider = hit.collider,
                        Surface = surface,
                        Point = hit.point,
                        Normal = hit.normal
                    };
                }
            }
        }
        return contact.Collider != null;
    }

    private bool IsOwner(Collider collider)
    {
        return collider.transform == ownerRoot || collider.transform.IsChildOf(ownerRoot);
    }
    private void OnDrawGizmosSelected()
    {
        if (root == null || tip == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(root.position, tip.position);
        Gizmos.DrawWireSphere(root.position, probeRadius);
        Gizmos.DrawWireSphere(tip.position, probeRadius);
        foreach (Vector3 direction in DIRECTIONS)
            Gizmos.DrawLine(tip.position, tip.position + direction * probeDistance);
    }
}
