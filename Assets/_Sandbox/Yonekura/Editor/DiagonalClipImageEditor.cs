using UnityEditor;
using UnityEditor.UI;

/// <summary>
/// DiagonalClipImage専用のInspectorを表示するクラス。
/// </summary>
[CustomEditor(typeof(DiagonalClipImage))]
[CanEditMultipleObjects]
public class DiagonalClipImageEditor : ImageEditor
{
    public override void OnInspectorGUI()
    {
        // Unity標準のImage設定を表示する。
        base.OnInspectorGUI();

        serializedObject.Update();

        // DiagonalClipImage独自の設定を表示する。
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("edgeSlope")
        );

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("visibleRatio")
        );

        serializedObject.ApplyModifiedProperties();
    }
}
