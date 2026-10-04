using UnityEngine;

public enum ChainsawSurface
{
    None,
    Floor,
    Wall,
    Ceiling,
    Enemy
}

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
    [Header("刃の根元／先端。床・天井の判定に使用")]
    [SerializeField] private Transform root;
    [SerializeField] private Transform tip;
    [SerializeField] private Transform ownerRoot;

    [SerializeField] private LayerMask terrainLayers;
    [SerializeField] private LayerMask enemyLayers;

    [SerializeField, Min(0.01f)]
    private float probeRadius = 0.08f;

    [SerializeField, Min(0.01f)]
    private float probeDistance = 0.2f;

    [SerializeField, Range(0.1f, 0.95f)]
    private float surfaceNormalThreshold = 0.7f;

    [Header("プレイヤー前方の壁・敵判定")]
    [Tooltip("Player直下の胴体位置に置く。刃やアニメーションする骨には置かない")]
    [SerializeField] private Transform frontRayOrigin;

    [Tooltip("始点からの距離。プレイヤーの半幅より長く設定する")]
    [SerializeField, Min(0.01f)]
    private float frontRayDistance = 1f;

    [Header("床への食い込み開始判定")]
    [Tooltip("床への開始時だけ許可する追加距離。Probe Radiusは刃の実際の厚みに合わせる")]
    [SerializeField, Min(0f)]
    private float floorStartContactTolerance = 0.005f;

    private static readonly Vector3[] DIRECTIONS =
    {
        Vector3.down,
        Vector3.up,
        Vector3.left,
        Vector3.right
    };

    private void Awake()
    {
        if (ownerRoot == null)
        {
            ownerRoot = transform;
        }

        if (root == null || tip == null)
        {
            Debug.LogError(
                "ChainsawContactDetectorのRootとTipを設定してください。",
                this);

            enabled = false;
        }

        if (frontRayOrigin == null)
        {
            Debug.LogError(
                "Front Ray OriginをPlayerの胴体位置に設定してください。" +
                "壁・敵を検出できません。",
                this);
        }

        if (terrainLayers.value == 0 || enemyLayers.value == 0)
        {
            Debug.LogWarning(
                "食い込み検出のTerrain Layers / Enemy Layersを設定してください。",
                this);
        }
    }

    public bool TryGetContact(
        Collider preferred,
        out ChainsawContact contact,
        ChainsawSurface currentSurface = ChainsawSurface.None,
        float facingDirection = 0f)
    {
        contact = default;

        if (root == null || tip == null)
        {
            return false;
        }

        float bestScore = float.PositiveInfinity;

        // 壁と敵は、プレイヤー前方のレイだけで検出する。
        TryGetFrontContact(
            preferred,
            currentSurface,
            facingDirection,
            out contact,
            out bestScore);

        // 床・天井は、従来と同じ刃の3点・4方向で検出する。
        for (int sampleIndex = 0; sampleIndex < 3; sampleIndex++)
        {
            Vector3 origin = Vector3.Lerp(
                root.position,
                tip.position,
                sampleIndex * 0.5f);

            foreach (Vector3 direction in DIRECTIONS)
            {
                float backoff = probeRadius + 0.01f;
                Vector3 castOrigin = origin - direction * backoff;

                foreach (RaycastHit hit in Physics.SphereCastAll(
                    castOrigin,
                    probeRadius,
                    direction,
                    probeDistance + backoff,
                    terrainLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    if (IsOwner(hit.collider) || hit.distance <= 0.0001f)
                    {
                        continue;
                    }

                    // 奥行き方向を向いた面は対象にしない。
                    if (Mathf.Abs(hit.normal.z) > 0.5f)
                    {
                        continue;
                    }

                    ChainsawSurface surface;

                    if (hit.normal.y >= surfaceNormalThreshold)
                    {
                        surface = ChainsawSurface.Floor;
                    }
                    else if (hit.normal.y <= -surfaceNormalThreshold)
                    {
                        surface = ChainsawSurface.Ceiling;
                    }
                    else
                    {
                        // 刃が床の側面に触れても壁候補にはしない。
                        continue;
                    }

                    // 床への新規開始時は、先読み範囲に入っただけでは開始しない。
                    // backoffはキャスト開始点を後ろへずらした距離。
                    // 既に床へ食い込み中なら、継ぎ目対策として従来の探索距離を維持する。
                    if (surface == ChainsawSurface.Floor &&
                        currentSurface != ChainsawSurface.Floor &&
                        hit.distance > backoff + floorStartContactTolerance)
                    {
                        continue;
                    }

                    float distanceScore =
                        hit.distance / (1f + hit.distance);

                    float score = hit.collider == preferred
                        ? -100f + distanceScore
                        : distanceScore;

                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;

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

    private bool TryGetFrontContact(
        Collider preferred,
        ChainsawSurface currentSurface,
        float facingDirection,
        out ChainsawContact contact,
        out float bestScore)
    {
        contact = default;
        bestScore = float.PositiveInfinity;

        if (frontRayOrigin == null ||
            Mathf.Abs(facingDirection) <= 0.01f)
        {
            return false;
        }

        Vector3 origin = frontRayOrigin.position;

        Vector3 direction =
            Vector3.right * Mathf.Sign(facingDirection);

        float distance = Mathf.Max(0.01f, frontRayDistance);

        Debug.DrawRay(
            origin,
            direction * distance,
            Color.yellow);

        // RaycastAllの取得順に依存せず、
        // 一番手前の地形を選ぶ。
        RaycastHit nearestTerrainHit = default;
        float nearestTerrainDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in Physics.RaycastAll(
            origin,
            direction,
            distance,
            terrainLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (IsOwner(hit.collider) ||
                hit.distance >= nearestTerrainDistance)
            {
                continue;
            }

            nearestTerrainHit = hit;
            nearestTerrainDistance = hit.distance;
        }

        if (nearestTerrainHit.collider != null)
        {
            Vector3 normal = nearestTerrainHit.normal;

            bool isFrontWall =
                Mathf.Abs(normal.z) <= 0.5f &&
                Mathf.Abs(normal.y) < surfaceNormalThreshold &&
                normal.x * Mathf.Sign(facingDirection) < -0.1f;

            if (isFrontWall)
            {
                float distanceScore =
                    nearestTerrainHit.distance /
                    (1f + nearestTerrainHit.distance);

                bool prioritizeWall =
                    currentSurface == ChainsawSurface.None ||
                    currentSurface == ChainsawSurface.Floor ||
                    currentSurface == ChainsawSurface.Wall;

                bestScore = prioritizeWall
                    ? -200f + distanceScore * 0.49f
                        - (nearestTerrainHit.collider == preferred ? 0.5f : 0f)
                    : (nearestTerrainHit.collider == preferred ? -100f : 0f)
                        + distanceScore;

                contact = new ChainsawContact
                {
                    Collider = nearestTerrainHit.collider,
                    Surface = ChainsawSurface.Wall,
                    Point = nearestTerrainHit.point,
                    Normal = normal
                };
            }
        }

        // 敵のTriggerは従来通り検出する。
        // 地形より奥にいる敵は対象にしない。
        foreach (RaycastHit hit in Physics.RaycastAll(
            origin,
            direction,
            distance,
            enemyLayers,
            QueryTriggerInteraction.Collide))
        {
            if (IsOwner(hit.collider) ||
                hit.distance > nearestTerrainDistance)
            {
                continue;
            }

            ChainsawDamageReceiver enemy =
                hit.collider.GetComponentInParent<ChainsawDamageReceiver>();

            if (enemy == null || !enemy.CanReceiveHit)
            {
                continue;
            }

            float distanceScore =
                hit.distance / (1f + hit.distance);

            // 敵を地形より優先する。
            // 現在食い込んでいる敵のColliderを優先する。
            float score =
                (hit.collider == preferred ? -400f : -300f)
                + distanceScore;

            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;

            contact = new ChainsawContact
            {
                Collider = hit.collider,
                Enemy = enemy,
                Surface = ChainsawSurface.Enemy,
                Point = hit.point,
                Normal = Vector3.zero
            };
        }

        return contact.Collider != null;
    }

    private bool IsOwner(Collider collider)
    {
        return collider.transform == ownerRoot ||
               collider.transform.IsChildOf(ownerRoot);
    }

    private void OnDrawGizmosSelected()
    {
        if (frontRayOrigin != null)
        {
            // 編集時は左右の検出範囲を表示する。
            // 実行中の実際の向きはDebug.DrawRayで表示する。
            Gizmos.color = Color.yellow;

            Gizmos.DrawLine(
                frontRayOrigin.position,
                frontRayOrigin.position +
                Vector3.right * frontRayDistance);

            Gizmos.DrawLine(
                frontRayOrigin.position,
                frontRayOrigin.position +
                Vector3.left * frontRayDistance);
        }

        if (root == null || tip == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        Gizmos.DrawLine(
            root.position,
            tip.position);

        Gizmos.DrawWireSphere(
            root.position,
            probeRadius);

        Gizmos.DrawWireSphere(
            tip.position,
            probeRadius);

        foreach (Vector3 direction in DIRECTIONS)
        {
            Gizmos.DrawLine(
                tip.position,
                tip.position + direction * probeDistance);
        }
    }
}
