using UnityEditor;

/// <summary>
/// ChangeSceneButtonのInspector表示をカスタマイズする。
/// </summary>
[CustomEditor(typeof(ChangeSceneButton))]
public class ChangeSceneButtonEditor : Editor
{
    /// <summary>
    /// Inspectorの表示内容を描画する。
    /// </summary>
    public override void OnInspectorGUI()
    {
        // 通常のInspectorを表示
        DrawDefaultInspector();

        // ボタンの動作を取得
        SerializedProperty action =
            serializedObject.FindProperty("action");

        // Target Sceneの設定内容を取得
        SerializedProperty targetScene =
            serializedObject.FindProperty("targetSceneName");

        // シーン遷移ボタンの場合のみ確認
        if (action.enumValueIndex ==
            (int)ChangeSceneButton.ButtonAction.ChangeScene)
        {
            // シーン名が設定されていない場合
            if (string.IsNullOrEmpty(targetScene.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "遷移先のシーン名を設定してください。",
                    MessageType.Warning
                );
            }
        }
    }
}
