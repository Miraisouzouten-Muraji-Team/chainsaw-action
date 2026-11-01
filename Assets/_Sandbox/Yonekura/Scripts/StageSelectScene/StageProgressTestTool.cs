using UnityEngine;

/// <summary>
/// ステージ進行状況をテストするための操作を提供する。
/// </summary>
public class StageProgressTestTool : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private StageSelectController stageSelectController;

    /// <summary>
    /// テスト用にすべてのエリアを開放する。
    /// </summary>
    [ContextMenu("テスト/すべてのエリアを開放")]
    private void TestUnlockAll()
    {
        if (!Application.isPlaying)
            return;

        StageProgressManager.SetTestUnlockAll(true);
        RefreshDisplay();

        Debug.Log("テスト：すべてのエリアを開放しました。");
    }

    /// <summary>
    /// テスト用にすべてのエリアをロックする。
    /// </summary>
    [ContextMenu("テスト/すべてのエリアをロック")]
    private void TestLockAll()
    {
        if (!Application.isPlaying)
            return;

        StageProgressManager.ResetProgress();
        RefreshDisplay();

        Debug.Log("テスト：すべてのエリアをロックしました。");
    }

    /// <summary>
    /// 保存された進行状況を初期状態に戻す。
    /// </summary>
    [ContextMenu("テスト/進行状況を初期化")]
    private void TestResetProgress()
    {
        if (!Application.isPlaying)
            return;

        StageProgressManager.ResetProgress();
        RefreshDisplay();

        Debug.Log("テスト：進行状況を初期化しました。");
    }

    private void RefreshDisplay()
    {
        if (stageSelectController != null)
            stageSelectController.RefreshProgressDisplay();
    }
}
