using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// エリアの進行状態。
/// </summary>
public enum StageAreaStatus
{
    Unplayed,
    Cleared,
    Locked
}

/// <summary>
/// ステージのクリア状況と開放状態を管理する。
/// </summary>
public static class StageProgressManager
{
    private const string ProgressKey = "StageProgress_ClearedAreas";
    private const string UnlockAllKey = "StageProgress_TestUnlockAll";

    [Serializable]
    private class ProgressData
    {
        public List<string> clearedSceneNames = new List<string>();
    }

    /// <summary>
    /// 指定したシーンがクリア済みか確認する。
    /// </summary>
    public static bool IsAreaCleared(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        ProgressData data = LoadProgress();
        return data.clearedSceneNames.Contains(sceneName);
    }

    /// <summary>
    /// 指定したシーンをクリア済みにする。
    /// </summary>
    public static void MarkAreaCleared(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        ProgressData data = LoadProgress();

        if (!data.clearedSceneNames.Contains(sceneName))
            data.clearedSceneNames.Add(sceneName);

        SaveProgress(data);
    }

    /// <summary>
    /// 指定したエリアが開放されているか確認する。
    /// </summary>
    public static bool IsAreaUnlocked(
        int index,
        string[] sceneNames)
    {
        if (sceneNames == null ||
            index < 0 ||
            index >= sceneNames.Length)
        {
            return false;
        }

        // テスト用の全開放。
        if (PlayerPrefs.GetInt(UnlockAllKey, 0) == 1)
            return true;

        // 最初のエリアは常に開放する。
        if (index == 0)
            return true;

        // ひとつ前のエリアをクリアすると開放する。
        return IsAreaCleared(sceneNames[index - 1]);
    }

    /// <summary>
    /// 指定したエリアの状態を取得する。
    /// </summary>
    public static StageAreaStatus GetAreaStatus(
        int index,
        string[] sceneNames)
    {
        if (!IsAreaUnlocked(index, sceneNames))
            return StageAreaStatus.Locked;

        if (IsAreaCleared(sceneNames[index]))
            return StageAreaStatus.Cleared;

        return StageAreaStatus.Unplayed;
    }

    /// <summary>
    /// テスト用にすべてのエリアを開放する。
    /// </summary>
    public static void SetTestUnlockAll(bool enabled)
    {
        PlayerPrefs.SetInt(UnlockAllKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 保存された進行状況を初期化する。
    /// </summary>
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(ProgressKey);
        PlayerPrefs.DeleteKey(UnlockAllKey);
        PlayerPrefs.Save();
    }

    private static ProgressData LoadProgress()
    {
        string json = PlayerPrefs.GetString(ProgressKey, "");

        if (string.IsNullOrEmpty(json))
            return new ProgressData();

        ProgressData data = JsonUtility.FromJson<ProgressData>(json);
        return data ?? new ProgressData();
    }

    private static void SaveProgress(ProgressData data)
    {
        string json = JsonUtility.ToJson(data);

        PlayerPrefs.SetString(ProgressKey, json);
        PlayerPrefs.Save();
    }
}
