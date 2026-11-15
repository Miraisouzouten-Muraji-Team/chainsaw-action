using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃中にプレイヤーと接触した場合、攻撃情報を送信する。
    /// 1回の判定開始につき同じプレイヤーには1回だけ通知する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class BossAttackHitbox : MonoBehaviour
    {
        private readonly HashSet<SensorPlayerController> hitPlayers = new();
        // 同じ攻撃内の別Hitboxと命中履歴を共有する場合に使用する。
        private HashSet<SensorPlayerController> sharedHitPlayers;

        [Header("特殊形状")]
        [Tooltip("縦切り用の上半円判定として扱う")]
        [SerializeField] private bool useDomeShape;
        [SerializeField, Min(0.01f)] private float domeRadius = 4f;
        [SerializeField, Min(0.01f)] private float domeDepth = 1.2f;
        [SerializeField] private Vector3 domeForwardDirection = Vector3.left;
        [SerializeField] private float domeGroundOffset = -2f;
        [SerializeField, Min(0f)] private float domeRectangleWidthPadding = 0.5f;

        private Collider hitCollider;
        private EnemyPlayerReference playerReference;
        private BossAttackDefinition activeDefinition;
        private float activeDamage;
        private bool hitWindowOpen;
        private BossAttackHitboxController ownerController;

        /// <summary>縦切りのモーションと共有する判定寸法。</summary>
        public float VerticalSlashRadius => domeRadius;
        public float VerticalSlashGroundOffset => domeGroundOffset;
        public float VerticalSlashWidthPadding => domeRectangleWidthPadding;
        public Vector3 VerticalSlashForward => GetDomeForwardDirection();
#if UNITY_EDITOR
        private static readonly Color DEBUG_COLOR =
            new(1f, 0.08f, 0.02f, 0.28f);

        private Texture2D runtimeOverlayTexture;
        private Bounds runtimeOverlayBounds;
        private float runtimeOverlayPadding;
        private Vector3 runtimeOverlayForward;
#endif

        private void Awake()
        {
            // 投擲で親から外れた盾も同じ表示スイッチを参照し続ける。
            ownerController = GetComponentInParent<BossAttackHitboxController>();
            hitCollider = GetComponent<Collider>();
            playerReference = new EnemyPlayerReference();

            if (useDomeShape)
            {
                SynchronizeDomeCollider();
            }

            if (!hitCollider.isTrigger)
            {
                Debug.LogError("BossAttackHitbox: Is TriggerをONにしてください。", this);
                enabled = false;
                return;
            }

            hitCollider.enabled = false;
        }

        /// <summary>
        /// 複数のColliderで一つの攻撃を構成する場合、命中履歴を共有する。
        /// </summary>
        public void SetSharedHitHistory(HashSet<SensorPlayerController> sharedHits)
        {
            sharedHitPlayers = sharedHits;
        }

        /// <summary>
        /// 指定した攻撃データとダメージで判定を開始する。
        /// </summary>
        public void OpenHitWindow(BossAttackDefinition definition, float damage)
        {
            if (!isActiveAndEnabled || hitCollider == null || definition == null) { return; }

            CloseHitWindow();

            activeDefinition = definition;
            activeDamage = Mathf.Max(0f, damage);
            hitPlayers.Clear();
            hitWindowOpen = true;
            hitCollider.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 判定を終了し、命中済みの情報を破棄する。
        /// </summary>
        public void CloseHitWindow()
        {
            hitWindowOpen = false;
            activeDefinition = null;
            activeDamage = 0f;
            hitPlayers.Clear();

            if (hitCollider != null)
            {
                hitCollider.enabled = false;
            }
        }

        /// <summary>
        /// 同じ親を持つCubeのTransformへBoxColliderの範囲を一致させる。
        /// </summary>
        public void SynchronizeWithCube(Transform sourceCube)
        {
            if (sourceCube == null ||
                !TryGetComponent(out BoxCollider boxCollider))
            {
                return;
            }

            transform.localPosition = sourceCube.localPosition;
            transform.localRotation = sourceCube.localRotation;
            transform.localScale = sourceCube.localScale;
            boxCollider.center = Vector3.zero;
            boxCollider.size = Vector3.one;
        }

        /// <summary>
        /// 仕様書の射程を上半円判定の直径として反映する。
        /// </summary>
        public void SetDomeDiameter(float diameter)
        {
            useDomeShape = true;
            domeRadius = Mathf.Max(0.01f, diameter * 0.5f);
            SynchronizeDomeCollider();
        }

        /// <summary>
        /// Editor設定時に上半円判定の寸法を初期化する。
        /// </summary>
        public void ConfigureDome(float diameter, float depth)
        {
            domeDepth = Mathf.Max(0.01f, depth);
            SetDomeDiameter(diameter);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryHitPlayer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryHitPlayer(other);
        }

        /// <summary>
        /// 判定が有効な間、Colliderに入っているプレイヤーへ命中情報を送る。
        /// </summary>
        private void TryHitPlayer(Collider other)
        {
            if (!hitWindowOpen || activeDefinition == null || activeDamage <= 0f || other == null || other.isTrigger) { return; }

            if (useDomeShape && !IsInsideVerticalSlashShape(other)) { return; }

            if (!playerReference.IsPlayerCollider(other)) { return; }

            var player = other.GetComponentInParent<SensorPlayerController>();

            if (player == null || hitPlayers.Contains(player)) { return; }

            if (sharedHitPlayers != null && sharedHitPlayers.Contains(player))
            {
                return;
            }

            if (!playerReference.TryGetAttackHitReceiver(
                    requireActiveAndEnabled: true,
                    out IAttackHitReceiver receiver,
                    out _))
            {
                return;
            }

            // 送信前に記録し、同じ攻撃で複数回通知されることを防ぐ。
            hitPlayers.Add(player);
            sharedHitPlayers?.Add(player);

            receiver.ReceiveAttackHit(new EnemyAttackHitData(activeDefinition, activeDamage));
        }

        private bool IsInsideVerticalSlashShape(Collider other)
        {
            Vector3 closestPoint = other.ClosestPoint(transform.position);
            Vector3 localPoint = transform.InverseTransformPoint(closestPoint);
            return IsInsideVerticalSlashLocalPoint(localPoint);
        }

        /// <summary>縦切りの命中判定とGameビュー表示で共通の形状判定。</summary>
        private bool IsInsideVerticalSlashLocalPoint(Vector3 localPoint)
        {
            float planarDistanceSquared =
                localPoint.x * localPoint.x + localPoint.y * localPoint.y;
            bool insideDome = localPoint.y >= -0.01f &&
                planarDistanceSquared <= domeRadius * domeRadius;
            Vector3 forwardDirection = GetDomeForwardDirection();
            float forwardDistance = Vector3.Dot(localPoint, forwardDirection);
            bool insideRectangle = localPoint.y <= 0.01f &&
                localPoint.y >= domeGroundOffset &&
                forwardDistance <= domeRadius &&
                forwardDistance >= -domeRectangleWidthPadding;

            return insideDome || insideRectangle;
        }

        private void SynchronizeDomeCollider()
        {
            if (!TryGetComponent(out BoxCollider boxCollider)) { return; }

            Vector3 forwardDirection = GetDomeForwardDirection();
            transform.localPosition = forwardDirection * domeRadius;
            transform.localScale = Vector3.one;
            float colliderBottom = domeGroundOffset;
            float colliderTop = domeRadius;
            boxCollider.center = new Vector3(
                0f,
                (colliderBottom + colliderTop) * 0.5f,
                0f);
            boxCollider.size = new Vector3(
                domeRadius * 2f,
                colliderTop - colliderBottom,
                domeDepth);
        }

        private Vector3 GetDomeForwardDirection()
        {
            Vector3 forwardDirection = domeForwardDirection;
            forwardDirection.y = 0f;

            if (forwardDirection.sqrMagnitude <= 0.0001f)
            {
                forwardDirection = Vector3.left;
            }

            return forwardDirection.normalized;
        }


        /// <summary>
        /// BoxCollider本来の位置・大きさ・回転に合わせて1回だけ命中判定する。
        /// </summary>
        public void HitOnce(BossAttackDefinition definition, float damage)
        {
            if (!isActiveAndEnabled ||
                !TryGetComponent(out BoxCollider boxCollider) ||
                definition == null)
            {
                return;
            }

            OpenHitWindow(definition, damage);

            try
            {
                Transform hitTransform = boxCollider.transform;
                Vector3 scale = hitTransform.lossyScale;

                Vector3 halfExtents = Vector3.Scale(boxCollider.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;

                Vector3 center = hitTransform.TransformPoint(boxCollider.center);

                Collider[] overlaps = Physics.OverlapBox(
                    center,
                    halfExtents,
                    hitTransform.rotation,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide);

                foreach (Collider overlap in overlaps)
                {
                    TryHitPlayer(overlap);
                }
            }
            finally
            {
                CloseHitWindow();
            }
        }

        private void OnDisable()
        {
            CloseHitWindow();
#if UNITY_EDITOR
            ReleaseRuntimeVisualization();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 映像合成後に実際の判定形状を重ね、GameビューのGizmos設定に依存せず表示する。
        /// </summary>
        private void OnGUI()
        {
            if (!Application.isPlaying || Event.current.type != EventType.Repaint)
            {
                return;
            }

            BossAttackHitboxController controller = ownerController != null
                ? ownerController
                : GetComponentInParent<BossAttackHitboxController>();
            Camera viewCamera = Camera.main;

            if (viewCamera == null ||
                (controller != null && !controller.ShowHitboxGizmos) ||
                !TryGetComponent(out BoxCollider boxCollider)) { return; }

            Bounds localBounds = new Bounds(boxCollider.center, boxCollider.size);

            if (runtimeOverlayTexture == null || runtimeOverlayBounds != localBounds ||
                runtimeOverlayPadding != domeRectangleWidthPadding ||
                runtimeOverlayForward != GetDomeForwardDirection())
            {
                BuildRuntimeOverlay(localBounds);
            }

            // サイドビューのXY面を画面へ投影し、反転・回転・カメラ移動にも追従する。
            Vector3 topLeft = transform.TransformPoint(new Vector3(
                localBounds.min.x, localBounds.max.y, localBounds.center.z));
            Vector3 topRight = transform.TransformPoint(new Vector3(
                localBounds.max.x, localBounds.max.y, localBounds.center.z));
            Vector3 bottomLeft = transform.TransformPoint(new Vector3(
                localBounds.min.x, localBounds.min.y, localBounds.center.z));
            Vector3 origin = viewCamera.WorldToScreenPoint(topLeft);
            Vector3 right = viewCamera.WorldToScreenPoint(topRight);
            Vector3 bottom = viewCamera.WorldToScreenPoint(bottomLeft);

            if (origin.z <= 0f || right.z <= 0f || bottom.z <= 0f) { return; }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            Matrix4x4 overlayMatrix = Matrix4x4.identity;
            overlayMatrix.SetColumn(0, new Vector4(
                right.x - origin.x, origin.y - right.y, 0f, 0f));
            overlayMatrix.SetColumn(1, new Vector4(
                bottom.x - origin.x, origin.y - bottom.y, 0f, 0f));
            overlayMatrix.SetColumn(3, new Vector4(
                origin.x, Screen.height - origin.y, 0f, 1f));
            GUI.matrix = overlayMatrix;
            GUI.color = Color.white;
            GUI.depth = -1000;
            GUI.DrawTexture(new Rect(0f, 0f, 1f, 1f), runtimeOverlayTexture);
            GUI.depth = previousDepth;
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        /// <summary>ダメージ判定と同じ寸法から半透明の確認用画像を生成する。</summary>
        private void BuildRuntimeOverlay(Bounds localBounds)
        {
            const int RESOLUTION = 256;

            if (runtimeOverlayTexture != null) { Destroy(runtimeOverlayTexture); }

            runtimeOverlayBounds = localBounds;
            runtimeOverlayPadding = domeRectangleWidthPadding;
            runtimeOverlayForward = GetDomeForwardDirection();
            runtimeOverlayTexture = new Texture2D(
                RESOLUTION, RESOLUTION, TextureFormat.RGBA32, false)
            {
                name = "BossHitboxOverlay",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[RESOLUTION * RESOLUTION];

            for (int y = 0; y < RESOLUTION; y++)
            {
                for (int x = 0; x < RESOLUTION; x++)
                {
                    Vector3 point = new Vector3(
                        Mathf.Lerp(localBounds.min.x, localBounds.max.x,
                            (x + 0.5f) / RESOLUTION),
                        Mathf.Lerp(localBounds.min.y, localBounds.max.y,
                            (y + 0.5f) / RESOLUTION), 0f);
                    bool inside = !useDomeShape || IsInsideVerticalSlashLocalPoint(point);
                    pixels[y * RESOLUTION + x] = inside ? DEBUG_COLOR : Color.clear;
                }
            }

            runtimeOverlayTexture.SetPixels(pixels);
            runtimeOverlayTexture.Apply(false, true);
        }

        private void OnValidate()
        {
            if (useDomeShape)
            {
                SynchronizeDomeCollider();
            }
        }

        private void OnDrawGizmos()
        {
            BossAttackHitboxController controller = ownerController != null
                ? ownerController
                : GetComponentInParent<BossAttackHitboxController>();

            if (controller != null && !controller.ShowHitboxGizmos)
            {
                return;
            }

            if (useDomeShape)
            {
                DrawVerticalSlashGizmo();
                return;
            }

            if (!TryGetComponent(out BoxCollider boxCollider)) { return; }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.15f, 0.05f, 0.12f);
            Gizmos.DrawCube(boxCollider.center, boxCollider.size);
            Gizmos.color = new Color(1f, 0.3f, 0.05f, 0.9f);
            Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void DrawVerticalSlashGizmo()
        {
            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            Vector3 forwardDirection = GetDomeForwardDirection();
            Vector3 rectangleFront = forwardDirection * domeRadius;
            Vector3 rectangleRear = -forwardDirection * domeRectangleWidthPadding;
            Vector3 groundOffset = Vector3.up * domeGroundOffset;
            Vector3[] rectangleVertices =
            {
                rectangleFront,
                rectangleRear,
                rectangleRear + groundOffset,
                rectangleFront + groundOffset
            };
            Handles.matrix = transform.localToWorldMatrix;
            Handles.color = new Color(1f, 0.1f, 0.05f, 0.16f);
            Handles.DrawSolidArc(
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                180f,
                domeRadius);
            Handles.DrawSolidRectangleWithOutline(
                rectangleVertices,
                new Color(1f, 0.1f, 0.05f, 0.16f),
                new Color(1f, 0.25f, 0.05f, 0.95f));
            Handles.color = new Color(1f, 0.25f, 0.05f, 0.95f);
            Handles.DrawWireArc(
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                180f,
                domeRadius);
            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        /// <summary>
        /// 再生終了や無効化時に確認用画像を解放する。
        /// </summary>
        private void ReleaseRuntimeVisualization()
        {
            if (runtimeOverlayTexture != null)
            {
                Destroy(runtimeOverlayTexture);
                runtimeOverlayTexture = null;
            }
        }
#endif
    }
}
