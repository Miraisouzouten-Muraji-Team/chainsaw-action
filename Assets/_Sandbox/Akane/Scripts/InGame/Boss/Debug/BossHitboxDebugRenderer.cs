using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃判定をワイヤーフレームで表示する。
    /// SceneビューはGizmos、GameビューはGUI上へ描画する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossHitboxDebugRenderer : MonoBehaviour
    {
        private static readonly int[,] BOX_EDGES =
        {
            { 0, 1 }, { 0, 2 }, { 0, 4 },
            { 1, 3 }, { 1, 5 },
            { 2, 3 }, { 2, 6 },
            { 3, 7 },
            { 4, 5 }, { 4, 6 },
            { 5, 7 }, { 6, 7 }
        };

        [SerializeField] private BossHitboxDebugSettings settings;
        [SerializeField] private BossVerticalSlashRange slashPreview;
        [SerializeField] private BossController boss;
        [SerializeField] private Camera gameCamera;

        private readonly List<BossAttackHitbox> hitboxes = new();

        private void Awake()
        {
            settings ??= GetComponent<BossHitboxDebugSettings>();
            boss ??= GetComponent<BossController>();
            gameCamera ??= Camera.main;
            RefreshHitboxes();
        }

        /// <summary>
        /// 現在の階層から攻撃判定を再取得する。
        /// </summary>
        public void RefreshHitboxes()
        {
            hitboxes.Clear();
            GetComponentsInChildren(true, hitboxes);
        }

        /// <summary>
        /// 途中で生成された判定を登録する。
        /// </summary>
        public void RegisterHitbox(BossAttackHitbox hitbox)
        {
            if (hitbox != null && !hitboxes.Contains(hitbox))
                hitboxes.Add(hitbox);
        }

        public void UnregisterHitbox(BossAttackHitbox hitbox)
        {
            hitboxes.Remove(hitbox);
        }


        /// <summary>
        /// 表示範囲と表示タイミングに応じてHitboxの表示を判定する。
        /// </summary>
        private bool IsVisible(BossAttackHitbox hitbox)
        {
            if (settings == null || hitbox == null ||
                settings.Scope == BossHitboxDebugScope.None)
            {
                return false;
            }

            bool activeOnly =
                settings.Scope == BossHitboxDebugScope.ActiveOnly;

            bool anyActive = false;
            float latestOpen = -1f;
            float latestHit = -1f;

            if (!activeOnly)
            {
                foreach (BossAttackHitbox candidate in hitboxes)
                {
                    if (candidate == null)
                        continue;

                    anyActive |= candidate.IsWindowOpen;
                    latestOpen = Mathf.Max(
                        latestOpen, candidate.LastOpenedAt);
                    latestHit = Mathf.Max(
                        latestHit, candidate.LastHitAt);
                }
            }

            float openedAt = activeOnly
                ? hitbox.LastOpenedAt
                : latestOpen;

            float hitAt = activeOnly
                ? hitbox.LastHitAt
                : latestHit;

            float now = Time.realtimeSinceStartup;
            float duration = settings.MomentDuration;

            return settings.Timing switch
            {
                BossHitboxDebugTiming.Always =>
                    !activeOnly || hitbox.IsWindowOpen,

                BossHitboxDebugTiming.DuringAttackWindow =>
                    activeOnly ? hitbox.IsWindowOpen : anyActive,

                BossHitboxDebugTiming.OnActivation =>
                    openedAt >= 0f &&
                    now - openedAt <= duration,

                BossHitboxDebugTiming.OnActualHit =>
                    hitAt >= 0f &&
                    now - hitAt <= duration,

                _ => false
            };
        }

        private bool IsPreviewVisible()
        {
            if (settings == null || settings.Content == BossHitboxDebugContent.ActualOnly)
                return false;

            if (settings == null || slashPreview == null ||
                !settings.ShowVerticalSlashPreview ||
                settings.Scope == BossHitboxDebugScope.None)
                return false;

            if (settings.Scope == BossHitboxDebugScope.All &&
                settings.Timing == BossHitboxDebugTiming.Always)
                return true;

            bool slashSelected = boss != null &&
                boss.SelectedAttack?.Definition.Type ==
                    BossAttackType.VerticalSlash &&
                boss.StateMachine?.CurrentType == BossStateType.Attack;

            if (!slashSelected) { return false; }

            foreach (BossAttackHitbox hitbox in hitboxes)
            {
                if (IsVisible(hitbox)) { return true; }
            }

            return false;
        }

        private static Vector3[] GetBoxCorners(BoxCollider collider)
        {
            Vector3 center = collider.center;
            Vector3 half = collider.size * 0.5f;
            Vector3[] corners = new Vector3[8];

            for (int i = 0; i < 8; i++)
            {
                Vector3 local = center + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);

                corners[i] = collider.transform.TransformPoint(local);
            }

            return corners;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (settings == null)
                settings = GetComponent<BossHitboxDebugSettings>();

            if (settings == null || !settings.ShowInSceneView)
                return;

            // 編集中は階層の追加・削除を反映する。
            if (!Application.isPlaying)
                RefreshHitboxes();

            if (settings.Content != BossHitboxDebugContent.PreviewOnly)
            {
                foreach (BossAttackHitbox hitbox in hitboxes)
                {
                    if (!IsVisible(hitbox) ||
                    !hitbox.TryGetComponent(out BoxCollider collider))
                        continue;

                    Gizmos.color = hitbox.IsWindowOpen
                        ? settings.ActiveColor : settings.InactiveColor;

                    Gizmos.matrix = collider.transform.localToWorldMatrix;
                    Gizmos.DrawWireCube(collider.center, collider.size);
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }

            if (IsPreviewVisible())
            {
                Gizmos.color = settings.PreviewColor;
                DrawPreviewGizmos();
            }
        }

        private void DrawPreviewGizmos()
        {
            const int SEGMENTS = 32;

            for (int i = 0; i < SEGMENTS; i++)
            {
                Vector3 a = slashPreview.transform.TransformPoint(
                    slashPreview.GetArcPoint((float)i / SEGMENTS));

                Vector3 b = slashPreview.transform.TransformPoint(
                    slashPreview.GetArcPoint((float)(i + 1) / SEGMENTS));

                Gizmos.DrawLine(a, b);
            }

            foreach ((Vector3 a, Vector3 b) in GetPreviewEdges())
            {
                Gizmos.DrawLine(
                    slashPreview.transform.TransformPoint(a),
                    slashPreview.transform.TransformPoint(b));
            }
        }


        private void OnGUI()
        {
            if (!Application.isPlaying ||
                settings == null ||
                !settings.ShowInGameView ||
                settings.Scope == BossHitboxDebugScope.None ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            Camera camera = gameCamera != null ? gameCamera : Camera.main;

            if (camera == null)
            {
                return;
            }

            int previousDepth = GUI.depth;
            GUI.depth = -1000;

            try
            {
                // 実際の攻撃Colliderを描画する。
                if (settings.Content != BossHitboxDebugContent.PreviewOnly)
                {
                    foreach (BossAttackHitbox hitbox in hitboxes)
                    {
                        if (!IsVisible(hitbox) ||
                            !hitbox.TryGetComponent(out BoxCollider collider))
                        {
                            continue;
                        }

                        Color color = hitbox.IsWindowOpen ? settings.ActiveColor : settings.InactiveColor;

                        Vector3[] corners = GetBoxCorners(collider);

                        for (int i = 0; i < BOX_EDGES.GetLength(0); i++)
                        {
                            DrawScreenLine(
                                camera,
                                corners[BOX_EDGES[i, 0]],
                                corners[BOX_EDGES[i, 1]],
                                color);
                        }
                    }
                }

                // 予定範囲はIsPreviewVisible内でもContentを判定する。
                if (IsPreviewVisible())
                {
                    DrawPreviewGUI(camera);
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private void DrawPreviewGUI(Camera camera)
        {
            const int SEGMENTS = 32;

            for (int i = 0; i < SEGMENTS; i++)
            {
                Vector3 a = slashPreview.transform.TransformPoint(
                    slashPreview.GetArcPoint((float)i / SEGMENTS));

                Vector3 b = slashPreview.transform.TransformPoint(
                    slashPreview.GetArcPoint((float)(i + 1) / SEGMENTS));

                DrawScreenLine(camera, a, b, settings.PreviewColor);
            }

            foreach ((Vector3 a, Vector3 b) in GetPreviewEdges())
            {
                DrawScreenLine(
                    camera,
                    slashPreview.transform.TransformPoint(a),
                    slashPreview.transform.TransformPoint(b),
                    settings.PreviewColor);
            }
        }

        private void DrawScreenLine(
            Camera camera,
            Vector3 worldA,
            Vector3 worldB,
            Color color)
        {
            Vector3 projectedA = camera.WorldToScreenPoint(worldA);
            Vector3 projectedB = camera.WorldToScreenPoint(worldB);

            if (projectedA.z <= 0f || projectedB.z <= 0f)
                return;

            Vector2 a = new Vector2(
                projectedA.x, Screen.height - projectedA.y);
            Vector2 b = new Vector2(
                projectedB.x, Screen.height - projectedB.y);

            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length <= 0.01f)
                return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);

            GUI.DrawTexture(
                new Rect(
                    a.x,
                    a.y - settings.LineWidth * 0.5f,
                    length,
                    settings.LineWidth),
                Texture2D.whiteTexture);

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
#endif


        /// <summary>
        /// 半円の中心から真下へ続く切り下ろし範囲を取得する。
        /// </summary>
        private IEnumerable<(Vector3, Vector3)> GetPreviewEdges()
        {
            if (slashPreview == null)
                yield break;

            Vector3 forward = slashPreview.LocalForward;

            // 左右の幅は中心線からの距離。
            float halfWidth = slashPreview.RectangleWidthPadding;

            // 半円の半径分を切り下ろした後、さらに下へ移動する。
            float totalDepth = slashPreview.Radius + slashPreview.DropDistance;

            Vector3 topLeft = forward * halfWidth;
            Vector3 topRight = -forward * halfWidth;
            Vector3 bottomLeft = topLeft + Vector3.down * totalDepth;
            Vector3 bottomRight = topRight + Vector3.down * totalDepth;

            yield return (topLeft, topRight);
            yield return (topRight, bottomRight);
            yield return (bottomRight, bottomLeft);
            yield return (bottomLeft, topLeft);
        }
    }
}
