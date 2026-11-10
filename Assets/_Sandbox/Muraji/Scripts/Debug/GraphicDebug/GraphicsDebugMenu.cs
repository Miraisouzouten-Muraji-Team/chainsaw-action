using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Unity 6 / URP. RuntimeDebugKitへの依存なし。
[DisallowMultipleComponent]
public sealed class GraphicsDebugMenu : MonoBehaviour
{
    [Header("Controls")]
    public bool allowReleaseBuild = true;
    public bool showStats = true;
    public bool showPreviews = true;
    [Range(0.2f, 1f)] public float interfaceScale = 1f;
    [Header("Post-processing / shadows: assign every rendering camera")]
    public Camera[] renderingCameras = new Camera[0];
    [Header("Afterimage: assign only generators, not player controllers")]
    public ToggleTargets afterimages = new ToggleTargets();
    [Header("Pixel groups: assign actual rendering / composition components")]
    public ToggleTargets near = new ToggleTargets();
    public ToggleTargets middle = new ToggleTargets();
    public ToggleTargets far = new ToggleTargets();
    [Header("RT previews: current camera target or explicit texture")]
    public TexturePreview[] previews = new TexturePreview[0];

    [Serializable]
    public sealed class ToggleTargets
    {
        public Behaviour[] components = new Behaviour[0];
        public Renderer[] renderers = new Renderer[0];
        [Tooltip("カメラ停止時に残る画像を透明にする対象。共有RTは指定しない。")]
        public RenderTexture[] clearWhenDisabled = new RenderTexture[0];
        [Tooltip("Camera.targetTextureを停止中クリア。専用RTの場合だけON。")]
        public bool clearCameraTargets;
        [NonSerialized] internal bool allowed = true;
    }

    [Serializable]
    public sealed class TexturePreview
    {
        public string label = "RenderTexture";
        [Tooltip("指定すると毎フレーム現在のtargetTextureを参照する。")]
        public Camera sourceCamera;
        public RenderTexture texture;
        public bool visible = true;
        public bool flipY;
        public Texture Current => sourceCamera != null ? sourceCamera.targetTexture : texture;
    }

    private sealed class CameraState
    {
        public Camera camera;
        public UniversalAdditionalCameraData data;
        public bool post, shadows;
    }
    private readonly List<CameraState> cameras = new List<CameraState>();
    private readonly Dictionary<Behaviour, bool> componentStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Renderer, bool> rendererStates = new Dictionary<Renderer, bool>();
    private GraphicsDebugStats statistics;
    private Vector2 statsScroll;
    private ToggleTargets[] groups;
    private RenderPipelineAsset previousQualityPipeline;
    private UniversalRenderPipelineAsset runtimePipeline;
    private float initialScale, elapsed, fps, ms, refresh;
    private int frames;
    private bool opened, postAllowed = true, shadowsAllowed = true, captured;
    private bool initialStats, initialPreviews;
    private bool[] initialPreviewVisibility;
    private Vector2 scroll;
    private string statsText = "Collecting...";
    private CursorLockMode previousLock;
    private bool previousCursor;

    private void OnEnable()
    {
        if (!Application.isEditor && !Debug.isDebugBuild && !allowReleaseBuild)
        { enabled = false; return; }
        Capture();
        statistics = new GraphicsDebugStats();
    }

    private void Capture()
    {
        groups = new[] { afterimages, near, middle, far };
        foreach (var group in groups)
        {
            if (group == null) continue;
            group.allowed = true;
            if (group.components != null)
                foreach (var item in group.components)
                    if (item != null && item != this && !componentStates.ContainsKey(item))
                        componentStates.Add(item, item.enabled);
            if (group.renderers != null)
                foreach (var item in group.renderers)
                    if (item != null && !rendererStates.ContainsKey(item))
                        rendererStates.Add(item, item.enabled);
        }
        if (renderingCameras != null)
            foreach (var camera in renderingCameras)
            {
                if (camera == null) continue;
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                { Debug.LogWarning("Graphics Debug: camera has no URP Additional Camera Data: " + camera.name, this); continue; }
                cameras.Add(new CameraState
                {
                    camera = camera,
                    data = data,
                    post = data.renderPostProcessing,
                    shadows = data.renderShadows
                });
            }
        previousQualityPipeline = QualitySettings.renderPipeline;
        var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (source != null)
        {
            runtimePipeline = Instantiate(source);
            runtimePipeline.name = source.name + " (Graphics Debug Runtime)";
            runtimePipeline.hideFlags = HideFlags.DontSave;
            initialScale = runtimePipeline.renderScale;
            // 設定アセット自体を変更せず、実行中のコピーを使用する。
            QualitySettings.renderPipeline = runtimePipeline;
        }
        initialStats = showStats; initialPreviews = showPreviews;
        initialPreviewVisibility = new bool[previews == null ? 0 : previews.Length];
        for (int i = 0; i < initialPreviewVisibility.Length; i++)
            initialPreviewVisibility[i] = previews[i] != null && previews[i].visible;
        postAllowed = shadowsAllowed = true;
        elapsed = 0; frames = 0; refresh = 0;
        captured = true;
    }

    private void Update()
    {
        bool toggle = false;
#if ENABLE_INPUT_SYSTEM
        toggle = Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        toggle = Input.GetKeyDown(KeyCode.F2);
#endif
        if (toggle) SetOpen(!opened);
        elapsed += Time.unscaledDeltaTime;
        frames++;
        refresh += Time.unscaledDeltaTime;
        if (refresh >= 0.5f)
        {
            fps = frames / Mathf.Max(0.0001f, elapsed);
            ms = elapsed * 1000f / Mathf.Max(1, frames);
            frames = 0; elapsed = refresh = 0;
            statsText = statistics.Build(fps, ms);
        }
    }

    private void LateUpdate()
    {
        // 登録した複数グループが同じ対象を参照しても、OFFを優先する。
        foreach (var pair in componentStates)
            if (pair.Key != null) pair.Key.enabled = pair.Value && ComponentAllowed(pair.Key);
        foreach (var pair in rendererStates)
            if (pair.Key != null) pair.Key.enabled = pair.Value && RendererAllowed(pair.Key);
        foreach (var state in cameras)
            if (state.data != null)
            {
                state.data.renderPostProcessing = state.post && postAllowed;
                state.data.renderShadows = state.shadows && shadowsAllowed;
            }
        foreach (var group in groups)
        {
            if (group == null || group.allowed) continue;
            if (group.clearWhenDisabled != null)
                foreach (var rt in group.clearWhenDisabled) Clear(rt);
            if (group.clearCameraTargets && group.components != null)
                foreach (var item in group.components)
                    if (item is Camera camera) Clear(camera.targetTexture);
        }
    }

    private bool ComponentAllowed(Behaviour target)
    {
        foreach (var group in groups)
            if (group != null && !group.allowed && group.components != null &&
                Array.IndexOf(group.components, target) >= 0) return false;
        return true;
    }
    private bool RendererAllowed(Renderer target)
    {
        foreach (var group in groups)
            if (group != null && !group.allowed && group.renderers != null &&
                Array.IndexOf(group.renderers, target) >= 0) return false;
        return true;
    }
    private static void Clear(RenderTexture rt)
    {
        if (rt == null || !rt.IsCreated()) return;
        var previous = RenderTexture.active;
        try { RenderTexture.active = rt; GL.Clear(true, true, Color.clear); }
        finally { RenderTexture.active = previous; }
    }

    public void ResetToInitial()
    {
        if (!captured) return;
        foreach (var group in groups) if (group != null) group.allowed = true;
        foreach (var pair in componentStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in rendererStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var state in cameras)
            if (state.data != null)
            { state.data.renderPostProcessing = state.post; state.data.renderShadows = state.shadows; }
        postAllowed = shadowsAllowed = true;
        if (runtimePipeline != null) runtimePipeline.renderScale = initialScale;
        showStats = initialStats; showPreviews = initialPreviews;
        for (int i = 0; previews != null && i < initialPreviewVisibility.Length && i < previews.Length; i++)
            if (previews[i] != null) previews[i].visible = initialPreviewVisibility[i];
    }

    private void SetOpen(bool value)
    {
        if (opened == value) return;
        opened = value;
        if (opened)
        {
            previousLock = Cursor.lockState; previousCursor = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
    }

    private void OnGUI()
    {
        var previousMatrix = GUI.matrix;
        float scale = Mathf.Clamp(interfaceScale, 0.2f, 1f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale, height = Screen.height / scale;
        try
        {
            if (GUI.Button(new Rect(width - 202, 12, 190, 30), "Graphics Debug [F2]")) SetOpen(!opened);
            float statsHeight = Mathf.Min(450, height * 0.52f);
            if (showStats)
            {
                GUILayout.BeginArea(new Rect(Mathf.Max(12, width - 492), 48,
                    Mathf.Min(480, width - 24), statsHeight), GUI.skin.box);
                statsScroll = GUILayout.BeginScrollView(statsScroll);
                GUILayout.Label(statsText);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            if (showPreviews) DrawPreviews(width, height);
            if (!opened) return;
            float menuTop = showStats ? 56 + statsHeight : 48;
            GUILayout.BeginArea(new Rect(Mathf.Max(12, width - 442), menuTop, 430,
                Mathf.Max(80, height - menuTop - 12)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Graphics Debug (separate from gameplay debug)");
            showStats = GUILayout.Toggle(showStats, "Show rendering stats");
            showPreviews = GUILayout.Toggle(showPreviews, "Show RT previews");
            bool owns = runtimePipeline != null && QualitySettings.renderPipeline == runtimePipeline;
            GUI.enabled = owns;
            GUILayout.Label(owns ? $"URP render scale: {runtimePipeline.renderScale:F2}" : "Render scale unavailable / pipeline changed");
            if (owns)
            {
                float next = GUILayout.HorizontalSlider(runtimePipeline.renderScale, 0.25f, 2f);
                if (!Mathf.Approximately(next, runtimePipeline.renderScale)) runtimePipeline.renderScale = next;
            }
            GUI.enabled = true;
            GUILayout.Label("Fixed-size Pixel RTs are not resized by URP scale.");
            postAllowed = GUILayout.Toggle(postAllowed, "Post-processing (allow initial cameras)");
            shadowsAllowed = GUILayout.Toggle(shadowsAllowed, "Shadows (allow initial cameras)");
            ToggleGroup("Afterimages", afterimages);
            ToggleGroup("Near", near); ToggleGroup("Middle", middle); ToggleGroup("Far", far);
            if (previews != null)
                foreach (var preview in previews)
                    if (preview != null) preview.visible = GUILayout.Toggle(preview.visible, "Preview: " + preview.label);
            if (GUILayout.Button("Reset to initial values")) ResetToInitial();
            GUILayout.Label("N/A = unavailable counter. Game input remains active.");
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        finally { GUI.enabled = true; GUI.matrix = previousMatrix; }
    }

    private void ToggleGroup(string label, ToggleTargets group)
    {
        if (group == null) { GUILayout.Label(label + ": not configured"); return; }
        int count = 0;
        if (group.components != null) foreach (var item in group.components) if (item != null && item != this) count++;
        if (group.renderers != null) foreach (var item in group.renderers) if (item != null) count++;
        GUI.enabled = count > 0;
        group.allowed = GUILayout.Toggle(group.allowed, label + (count == 0 ? " (not configured)" : " (allow initial targets)"));
        GUI.enabled = true;
    }

    private void DrawPreviews(float width, float height)
    {
        if (previews == null) return;
        int row = 0;
        foreach (var preview in previews)
        {
            if (preview == null || !preview.visible) continue;
            float w = Mathf.Min(260, width * 0.28f);
            float h = Mathf.Min(150, height * 0.2f);
            float y = height - 12 - (++row) * (h + 30);
            if (y < 12) break;
            Rect box = new Rect(12, y, w, h + 26);
            GUI.Box(box, "");
            Texture texture = preview.Current;
            GUI.Label(new Rect(18, y + 2, w - 12, 24), preview.label +
                (texture != null ? $" {texture.width}x{texture.height}" : " (not assigned)"));
            if (texture == null) continue;
            Rect area = new Rect(18, y + 26, w - 12, h - 6);
            // RTは参照するだけ。サイズ/FilterMode/所有権は変更しない。
            var matrix = GUI.matrix;
            if (preview.flipY) GUIUtility.ScaleAroundPivot(new Vector2(1, -1), area.center);
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit, true);
            GUI.matrix = matrix;
        }
    }

    private void OnDisable()
    {
        SetOpen(false);
        ResetToInitial();
        statistics?.Dispose();
        statistics = null;
        if (runtimePipeline != null)
        {
            if (QualitySettings.renderPipeline == runtimePipeline)
                QualitySettings.renderPipeline = previousQualityPipeline;
            // 外部のQuality変更などで参照中のコピーは破棄しない。
            if (GraphicsSettings.currentRenderPipeline != runtimePipeline)
                Destroy(runtimePipeline);
        }
        runtimePipeline = null;
        componentStates.Clear(); rendererStates.Clear(); cameras.Clear();
        captured = false;
    }
}
