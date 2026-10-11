using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public readonly struct ChainsawTrajectorySample
{
    public readonly Vector3 Root;
    public readonly Vector3 Tip;
    public readonly float Time;
    public ChainsawTrajectorySample(Vector3 root, Vector3 tip, float time)
    { Root = root; Tip = tip; Time = time; }
}

public class ChainsawTrajectoryRecorder : MonoBehaviour
{
    [SerializeField] private Transform root;
    [SerializeField] private Transform tip;
    [SerializeField, Min(2)] private int maxSamples = 180;
    private readonly List<ChainsawTrajectorySample> samples = new List<ChainsawTrajectorySample>();
    private bool recording;
    public void BeginRecording()
    {
        samples.Clear();
        recording = true;
        Capture();
    }
    public void EndRecording() { recording = false; }
    private void LateUpdate() { if (recording && Time.deltaTime > 0f) Capture(); }
    private void Capture()
    {
        if (root == null || tip == null) return;
        if (samples.Count >= Mathf.Max(2, maxSamples)) samples.RemoveAt(0);
        samples.Add(new ChainsawTrajectorySample(root.position, tip.position, Time.time));
    }
    public ReadOnlyCollection<ChainsawTrajectorySample> Snapshot()
    {
        if (recording) Capture();
        // 内部リストを渡さない。次の攻撃で消去しても通知済みデータは変わらない。
        return System.Array.AsReadOnly(samples.ToArray());
    }
    private void OnDisable() { EndRecording(); }
}
