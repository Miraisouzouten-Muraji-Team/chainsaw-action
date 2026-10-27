using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class RuntimeDebugVisualizer : MonoBehaviour
{
    public enum DrawMode { ScreenOverlay, WorldMesh }
    [Header("描画方式")]
    public DrawMode drawMode = DrawMode.ScreenOverlay;
    [Tooltip("キャラを実際に描画しているCamera。未指定ならCamera.main。合成専用Cameraではなく元の3D Cameraを指定。")]
    public Camera projectionCamera;
    [Range(1f, 8f)] public float lineWidth = 2f;
    [Tooltip("合成画像が全画面以外に配置される場合にON。")]
    public bool useCustomOutputRect;
    [Tooltip("最終画面上の配置。左上原点、0～1の正規化座標。通常は(0,0,1,1)。")]
    public Rect outputRect = new Rect(0, 0, 1, 1);

    public RuntimeDebugSettings settings;
    public string group = "Sensor";
    [Tooltip("ONでメニューのグループ切替を無視して表示。表示確認用。")]
    public bool forceVisible;
    [SerializeField, TextArea] private string displayStatus;
    public Color color = Color.green;
    [Tooltip("表示したい3D Colliderだけを登録。子の全Colliderを自動収集しません。")]
    public Collider[] targets = new Collider[0];
    public RayEntry[] rays = new RayEntry[0];
    [Range(8, 64)] public int segments = 24;
    [Tooltip("描画オブジェクトのLayer。カメラのCulling Maskに含めてください。")]
    [Range(0, 31)] public int drawingLayer;
    [Serializable]
    public sealed class RayEntry
    {
        public Transform origin;
        public Vector3 direction = Vector3.right;
        public bool localDirection = true;
        [Min(0)] public float length = 1f;
    }

    private readonly List<Vector3> vertices = new List<Vector3>(512);
    private readonly List<Color> colors = new List<Color>(512);
    private readonly List<int> indices = new List<int>(512);
    private GameObject drawing;
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private bool readyToDraw;
    private int Steps => Mathf.Clamp(segments, 8, 64);

    private void LateUpdate()
    {
        readyToDraw = false;
        string reason = null;
        if (drawMode == DrawMode.WorldMesh && settings == null) reason = "Settings is missing.";
        else if (drawMode == DrawMode.WorldMesh && settings.lineMaterial == null) reason = "Line Material is missing.";
        else if (drawMode == DrawMode.WorldMesh && !settings.lineMaterial.shader.isSupported) reason = "Line Material shader is unsupported.";
        else if (!forceVisible && !RuntimeDebugMenu.IsGroupVisible(group))
            reason = "Group is OFF or menu is inactive. Enable group or Force Visible.";
        if (reason != null)
        {
            displayStatus = reason;
            if (drawing != null) drawing.SetActive(false);
            return;
        }
        if (drawing != null) drawing.SetActive(drawMode == DrawMode.WorldMesh);
        vertices.Clear(); colors.Clear(); indices.Clear();
        if (targets != null)
            foreach (var target in targets)
                if (target != null && target.enabled && target.gameObject.activeInHierarchy)
                    DrawCollider(target);
        if (rays != null)
            foreach (var ray in rays)
            {
                if (ray == null || ray.origin == null || !ray.origin.gameObject.activeInHierarchy) continue;
                Vector3 direction = ray.localDirection ? ray.origin.TransformDirection(ray.direction) : ray.direction;
                Line(ray.origin.position, ray.origin.position + direction.normalized * Mathf.Max(0, ray.length));
            }
        if (drawMode == DrawMode.ScreenOverlay)
        {
            Camera camera = projectionCamera != null ? projectionCamera : Camera.main;
            readyToDraw = camera != null && vertices.Count > 0 && color.a > 0f;
            displayStatus = camera == null ? "Assign Projection Camera (the actual 3D render camera)."
                : vertices.Count == 0 ? "No lines: check Targets and enabled Colliders."
                : color.a <= 0f ? "Color alpha is zero."
                : $"Screen overlay: {vertices.Count / 2} lines, Camera={camera.name}. No Shader/Layer filtering.";
            return;
        }
        if (drawing == null) CreateDrawing();
        drawing.SetActive(true);
        drawing.layer = drawingLayer;
        meshRenderer.sharedMaterial = settings.lineMaterial;
        mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetColors(colors);
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
        displayStatus = vertices.Count == 0
            ? "No lines: assign Targets/Rays; check Collider enabled and active."
            : $"Drawing {vertices.Count / 2} lines. If invisible: check Color alpha and camera Culling Mask.";
    }

    private void OnGUI()
    {
        if (!isActiveAndEnabled || drawMode != DrawMode.ScreenOverlay ||
            !readyToDraw || Event.current.type != EventType.Repaint ||
            (!forceVisible && !RuntimeDebugMenu.IsGroupVisible(group))) return;
        Camera camera = projectionCamera != null ? projectionCamera : Camera.main;
        if (camera == null || camera.targetDisplay != 0) return;

        Rect normalized = useCustomOutputRect ? outputRect
            : camera.targetTexture != null ? new Rect(0, 0, 1, 1)
            : new Rect(camera.rect.x, 1f - camera.rect.yMax, camera.rect.width, camera.rect.height);
        Rect area = new Rect(normalized.x * Screen.width, normalized.y * Screen.height,
            normalized.width * Screen.width, normalized.height * Screen.height);
        if (area.width <= 0 || area.height <= 0) return;
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        int oldDepth = GUI.depth;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.color = color;
            GUI.depth = 100; // 通常のGUIメニューの背面に描く。
            GUI.BeginGroup(area);
            try
            {
                for (int i = 0; i + 1 < vertices.Count; i += 2)
                {
                    Vector3 a = vertices[i], b = vertices[i + 1];
                    if (!ClipDepth(camera, ref a, ref b)) continue;
                    Vector3 va = camera.WorldToViewportPoint(a);
                    Vector3 vb = camera.WorldToViewportPoint(b);
                    Vector2 from = new Vector2(va.x * area.width, (1f - va.y) * area.height);
                    Vector2 to = new Vector2(vb.x * area.width, (1f - vb.y) * area.height);
                    if (!Finite(from) || !Finite(to) ||
                        !ClipScreen(ref from, ref to, area.width, area.height)) continue;
                    Vector2 delta = to - from;
                    float length = delta.magnitude;
                    if (length < 0.01f) continue;
                    Matrix4x4 beforeLine = GUI.matrix;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
                    GUI.DrawTexture(new Rect(from.x, from.y - lineWidth * 0.5f,
                        length, lineWidth), Texture2D.whiteTexture);
                    GUI.matrix = beforeLine;
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth; }
    }

    private static bool Finite(Vector2 p)
    {
        return !float.IsNaN(p.x) && !float.IsNaN(p.y) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y);
    }

    // カメラ背後の端点を直接投影せず、near/farの範囲へ線分を切り詰める。
    private static bool ClipDepth(Camera camera, ref Vector3 a, ref Vector3 b)
    {
        Vector3 origin = camera.transform.position, forward = camera.transform.forward;
        float za = Vector3.Dot(a - origin, forward), zb = Vector3.Dot(b - origin, forward);
        float t0 = 0, t1 = 1;
        float near = Mathf.Max(0.0001f, camera.nearClipPlane);
        if (!ClipBoundary(-(zb - za), za - near, ref t0, ref t1) ||
            !ClipBoundary(zb - za, camera.farClipPlane - za, ref t0, ref t1)) return false;
        Vector3 start = a, delta = b - a;
        a = start + delta * t0; b = start + delta * t1;
        return true;
    }

    private static bool ClipScreen(ref Vector2 a, ref Vector2 b, float width, float height)
    {
        Vector2 delta = b - a;
        float t0 = 0, t1 = 1;
        if (!ClipBoundary(-delta.x, a.x, ref t0, ref t1) ||
            !ClipBoundary(delta.x, width - a.x, ref t0, ref t1) ||
            !ClipBoundary(-delta.y, a.y, ref t0, ref t1) ||
            !ClipBoundary(delta.y, height - a.y, ref t0, ref t1)) return false;
        Vector2 start = a;
        a = start + delta * t0; b = start + delta * t1;
        return true;
    }

    private static bool ClipBoundary(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Abs(p) < 0.000001f) return q >= 0;
        float t = q / p;
        if (p < 0) { if (t > t1) return false; t0 = Mathf.Max(t0, t); }
        else { if (t < t0) return false; t1 = Mathf.Min(t1, t); }
        return true;
    }

    private void CreateDrawing()
    {
        // 頂点はワールド座標。親のScale/回転を二重適用しない。
        drawing = new GameObject("Runtime Debug Lines");
        drawing.hideFlags = HideFlags.DontSave;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(drawing, gameObject.scene);
        mesh = new Mesh { name = "Runtime Debug Lines", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        drawing.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = drawing.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private void Line(Vector3 a, Vector3 b)
    {
        indices.Add(vertices.Count); vertices.Add(a); colors.Add(color);
        indices.Add(vertices.Count); vertices.Add(b); colors.Add(color);
    }

    private void Ring(Vector3 center, Vector3 u, Vector3 v, float radius)
    {
        Vector3 previous = center + u * radius;
        for (int i = 1; i <= Steps; i++)
        {
            float angle = i * Mathf.PI * 2f / Steps;
            Vector3 next = center + radius * (u * Mathf.Cos(angle) + v * Mathf.Sin(angle));
            Line(previous, next); previous = next;
        }
    }

    private void Box(Vector3 center, Vector3 size, Matrix4x4 matrix)
    {
        Vector3 h = size * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 a = center + new Vector3((i & 1) == 0 ? -h.x : h.x,
                (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            for (int axis = 0; axis < 3; axis++)
            {
                if ((i & (1 << axis)) != 0) continue;
                Vector3 b = a; b[axis] += size[axis];
                Line(matrix.MultiplyPoint3x4(a), matrix.MultiplyPoint3x4(b));
            }
        }
    }

    private void DrawCollider(Collider target)
    {
        Transform t = target.transform;
        Vector3 s = t.lossyScale;
        s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        if (target is BoxCollider box)
        {
            Box(Vector3.zero, Vector3.Scale(box.size, s),
                Matrix4x4.TRS(t.TransformPoint(box.center), t.rotation, Vector3.one));
        }
        else if (target is SphereCollider sphere)
        {
            Vector3 c = t.TransformPoint(sphere.center);
            float r = sphere.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            Ring(c, Vector3.right, Vector3.up, r);
            Ring(c, Vector3.up, Vector3.forward, r);
            Ring(c, Vector3.forward, Vector3.right, r);
        }
        else if (target is CapsuleCollider capsule)
        {
            int axisIndex = capsule.direction;
            Vector3 localAxis = Vector3.zero; localAxis[axisIndex] = 1;
            Vector3 localU = Vector3.zero; localU[(axisIndex + 1) % 3] = 1;
            Vector3 axis = t.rotation * localAxis;
            Vector3 u = t.rotation * localU, v = Vector3.Cross(axis, u);
            float r = capsule.radius * Mathf.Max(s[(axisIndex + 1) % 3], s[(axisIndex + 2) % 3]);
            float half = Mathf.Max(0, capsule.height * s[axisIndex] * 0.5f - r);
            Vector3 center = t.TransformPoint(capsule.center);
            Vector3 top = center + axis * half, bottom = center - axis * half;
            Ring(top, u, v, r); Ring(bottom, u, v, r);
            for (int side = 0; side < 4; side++)
            {
                float angle = side * Mathf.PI * 0.5f;
                Vector3 radial = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                Line(bottom + radial * r, top + radial * r);
                Vector3 previousTop = top + radial * r, previousBottom = bottom + radial * r;
                for (int i = 1; i <= Steps / 2; i++)
                {
                    float a = i * Mathf.PI / Steps;
                    Vector3 nextTop = top + r * (radial * Mathf.Cos(a) + axis * Mathf.Sin(a));
                    Vector3 nextBottom = bottom + r * (radial * Mathf.Cos(a) - axis * Mathf.Sin(a));
                    Line(previousTop, nextTop); Line(previousBottom, nextBottom);
                    previousTop = nextTop; previousBottom = nextBottom;
                }
            }
        }
        else Box(target.bounds.center, target.bounds.size, Matrix4x4.identity);
    }

    private void OnDisable() { readyToDraw = false; if (drawing != null) drawing.SetActive(false); }
    private void OnDestroy()
    {
        if (drawing != null) Destroy(drawing);
        if (mesh != null) Destroy(mesh);
    }
}
