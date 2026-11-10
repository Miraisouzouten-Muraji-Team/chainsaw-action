using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
#endif

// GameView Statisticsの全項目を表示。Editor専用APIはビルドから除外。
internal sealed class GraphicsDebugStats : IDisposable
{
    private readonly Dictionary<string, ProfilerRecorder> recorders =
        new Dictionary<string, ProfilerRecorder>();
    private readonly StringBuilder output = new StringBuilder(1024);
#if UNITY_EDITOR
    private readonly Dictionary<string, PropertyInfo> editorProperties =
        new Dictionary<string, PropertyInfo>();
#endif

    internal GraphicsDebugStats()
    {
        Start("Batches Count");
        Start("Draw Calls Count");
        Start("SetPass Calls Count");
        Start("Triangles Count");
        Start("Vertices Count");
        Start("Dynamic Batched Draw Calls Count");
        Start("Static Batched Draw Calls Count");
        Start("Dynamic Batches Count");
        Start("Static Batches Count");
        Start("Shadow Casters Count");
        Start("CPU Main Thread Frame Time", ProfilerCategory.Internal);
        Start("CPU Render Thread Frame Time", ProfilerCategory.Internal);
    }

    private void Start(string name) { Start(name, ProfilerCategory.Render); }
    private void Start(string name, ProfilerCategory category)
    {
        try { recorders[name] = ProfilerRecorder.StartNew(category, name, 1); }
        catch (Exception) { /* 存在しない/使用不能なカウンターはN/Aとして扱う。 */ }
    }

    private double? Sample(string name)
    {
        if (!recorders.TryGetValue(name, out var recorder) ||
            !recorder.Valid || recorder.Count == 0) return null;
        return recorder.LastValue;
    }

#if UNITY_EDITOR
    private object EditorValue(string name)
    {
        if (!editorProperties.TryGetValue(name, out var property))
        {
            property = typeof(UnityEditor.UnityStats).GetProperty(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            editorProperties.Add(name, property);
        }
        if (property == null) return null;
        try { return property.GetValue(null); }
        catch (Exception) { return null; }
    }
    private double? EditorNumber(string name)
    {
        object value = EditorValue(name);
        if (value == null) return null;
        try { return Convert.ToDouble(value); }
        catch (Exception) { return null; }
    }
#endif

    private double? Metric(string editorName, string counter)
    {
#if UNITY_EDITOR
        double? value = EditorNumber(editorName);
        if (value.HasValue) return value;
#endif
        return Sample(counter);
    }

    private static string Number(double? value, string format = "N0", string unit = "")
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return "N/A";
        return value.Value.ToString(format) + unit;
    }

    internal string Build(float fps, float frameMs)
    {
        output.Clear();
        output.AppendLine("Statistics");
#if UNITY_EDITOR
        object suspended = EditorValue("audioOutputSuspended");
        output.AppendLine("Audio" + (suspended is bool state ? (state ? " (suspended):" : " (active):") : " (state N/A):"));
        double? amplitude = EditorNumber("audioLevel");
        double? db = amplitude.HasValue ? 20d * Math.Log10(Math.Max(1e-8, amplitude.Value)) : (double?)null;
        output.AppendLine("  Level: " + Number(db, "F1", " dB"));
        output.AppendLine("  Clipping: " + Number(EditorNumber("audioClippingAmount") * 100d, "F1", "%"));
        output.AppendLine("  DSP load: " + Number(EditorNumber("audioDSPLoad") * 100d, "F1", "%"));
        output.AppendLine("  Stream load: " + Number(EditorNumber("audioStreamLoad") * 100d, "F1", "%"));
#else
        // AudioListener.pauseはオーディオ出力のsuspended状態と同じではない。
        output.AppendLine("Audio (state N/A):");
        output.AppendLine("  Level: N/A    Clipping: N/A");
        output.AppendLine("  DSP load: N/A    Stream load: N/A");
#endif
        output.AppendLine();
        output.AppendLine("Graphics:");
        output.AppendLine($"  FPS: {fps:F1}    Frame: {frameMs:F2} ms (wall)");
        double? mainMs = Sample("CPU Main Thread Frame Time") / 1000000d;
        double? renderMs = Sample("CPU Render Thread Frame Time") / 1000000d;
#if UNITY_EDITOR
        // UnityStatsの旧時間プロパティは秒。公開API差異はEditor内の参照に閉じる。
        mainMs = EditorNumber("frameTime") * 1000d ?? mainMs;
        renderMs = EditorNumber("renderTime") * 1000d ?? renderMs;
#endif
        output.AppendLine("  CPU main: " + Number(mainMs, "F2", " ms") +
            "    render thread: " + Number(renderMs, "F2", " ms"));
        output.AppendLine("  Batches: " + Number(Metric("batches", "Batches Count")));
        double? saved;
#if UNITY_EDITOR
        saved = EditorNumber("dynamicBatchedDrawCalls") + EditorNumber("staticBatchedDrawCalls")
            - EditorNumber("dynamicBatches") - EditorNumber("staticBatches");
#else
        saved = Sample("Dynamic Batched Draw Calls Count") + Sample("Static Batched Draw Calls Count")
            - Sample("Dynamic Batches Count") - Sample("Static Batches Count");
#endif
        output.AppendLine("  Saved by batching: " + Number(saved));
        output.AppendLine("  Tris: " + Number(Metric("triangles", "Triangles Count")) +
            "    Verts: " + Number(Metric("vertices", "Vertices Count")));
#if UNITY_EDITOR
        string resolution = EditorValue("screenRes") as string;
        output.AppendLine("  Screen: " + (resolution ?? $"{Screen.width}x{Screen.height}") +
            " - " + Number(EditorNumber("screenBytes") / (1024d * 1024d), "F1", " MB"));
#else
        output.AppendLine($"  Screen: {Screen.width}x{Screen.height} - N/A MB");
#endif
        output.AppendLine("  SetPass calls: " + Number(Metric("setPassCalls", "SetPass Calls Count")));
        output.AppendLine("  Shadow casters: " + Number(Metric("shadowCasters", "Shadow Casters Count")));
#if UNITY_EDITOR
        output.AppendLine("  Visible skinned meshes: " + Number(EditorNumber("visibleSkinnedMeshes")));
        output.AppendLine("  Animation components playing: " + Number(EditorNumber("animationComponentsPlaying")));
        output.AppendLine("  Animator components playing: " + Number(EditorNumber("animatorComponentsPlaying")));
        output.AppendLine("Source: UnityStats / ProfilerRecorder");
#else
        output.AppendLine("  Visible skinned meshes: N/A");
        output.AppendLine("  Animation components playing: N/A");
        output.AppendLine("  Animator components playing: N/A");
        output.AppendLine("Source: runtime ProfilerRecorder (all cameras)");
#endif
        output.AppendLine("Draw calls: " + Number(Metric("drawCalls", "Draw Calls Count")));
        output.Append("N/A = unavailable, not zero. Updated every 0.5s.");
        return output.ToString();
    }

    public void Dispose()
    {
        foreach (var pair in recorders) pair.Value.Dispose();
        recorders.Clear();
    }
}
