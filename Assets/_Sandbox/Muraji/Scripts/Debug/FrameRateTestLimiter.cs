using UnityEngine;

public sealed class FrameRateTestLimiter : MonoBehaviour
{
    [SerializeField, Min(1)] private int targetFps = 15;

    private int previousTargetFps;
    private int previousVSyncCount;

    private void OnEnable()
    {
        previousTargetFps = Application.targetFrameRate;
        previousVSyncCount = QualitySettings.vSyncCount;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(1, targetFps);
    }

    private void OnDisable()
    {
        Application.targetFrameRate = previousTargetFps;
        QualitySettings.vSyncCount = previousVSyncCount;
    }
}
