using System;
using UnityEngine;

/// <summary>
/// ステージ1つ分の表示情報と遷移先を管理する。
/// </summary>
[Serializable]
public class StageAreaData
{
    [Header("カード表示")]
    [Tooltip("カードに表示するステージ名。改行して複数行で表示できます。")]
    [TextArea(1, 4)]
    public string displayName;

    [Tooltip("カードに表示するステージのスクリーンショット。")]
    public Sprite screenshot;

    [Header("遷移後のシーン")]
    [Tooltip("ステージ決定時に読み込む遷移先のシーン名。")]
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
