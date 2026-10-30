using System;
using UnityEngine;

/// <summary>
/// ステージ1つ分の表示情報と遷移先を管理する。
/// </summary>
[Serializable]
public class StageAreaData
{
    [Tooltip("ステージ名")]
    public string stageName;

    [Tooltip("エリア番号")]
    public string areaNumber;

    [Tooltip("ステージのスクリーンショット")]
    public Sprite screenshot;

    [Tooltip("遷移先のシーン名")]
    public string sceneName;
}

/// <summary>
/// ステージ情報をまとめて管理するデータベース。
/// </summary>
[CreateAssetMenu(
    fileName = "StageDatabase",
    menuName = "Game/Stage Database")]
public class StageDatabase : ScriptableObject
{
    [Header("ステージ一覧")]
    [SerializeField] private StageAreaData[] areas;

    /// <summary>
    /// 登録されているステージ情報を取得する。
    /// </summary>
    public StageAreaData[] Areas => areas;
}
