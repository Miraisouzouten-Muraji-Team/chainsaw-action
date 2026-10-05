using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChangeSceneButton))]
public class ChangeSceneButtonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 通常のInspectorを表示
        DrawDefaultInspector();

        // Target Sceneがシーンファイルか確認
        ChangeSceneButton button = (ChangeSceneButton)target;

        SerializedProperty targetScene =
            serializedObject.FindProperty("targetScene");

        // Target Sceneが設定されているか確認
        if (targetScene.objectReferenceValue != null)
        {
            string path = AssetDatabase.GetAssetPath(
                targetScene.objectReferenceValue
            );

            // シーンファイルか確認
            if (!path.EndsWith(".unity"))
            {
                EditorGUILayout.HelpBox(
                    "Target Sceneにはシーンファイル（.unity）のみ設定できます。",
                    MessageType.Error
                );

                targetScene.objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}


