using UnityEditor;
using UnityEngine;

/// <summary>
/// HPバーと回復タイマーバーのInspector表示を整理する
/// 色設定をひとつのグループにまとめ、角度設定をその下に表示する
/// </summary>
[CustomEditor(typeof(HPBarGraphic))]
public class HPBarGraphicEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 色設定と角度設定は、後でまとめて表示する
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "backgroundColor",
            "hpColor",
            "damageColor",
            "angleA",
            "angleB");

        EditorGUILayout.Space(8);

        // HPバーの3色を縦に並べて表示する
        EditorGUILayout.LabelField("色設定", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("backgroundColor"),
            new GUIContent("Background Color"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("hpColor"),
            new GUIContent("HP Color"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("damageColor"),
            new GUIContent("Damage Color"));

        EditorGUILayout.Space(8);

        // 平行四辺形の角度設定を色設定の下にまとめる
        EditorGUILayout.LabelField(
            "角度設定",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("angleA"),
            new GUIContent("Angle A"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("angleB"),
            new GUIContent("Angle B"));

        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>
/// 自動回復タイマーバーのInspector表示を整理する
/// </summary>
[CustomEditor(typeof(AutoRecoveryBarGraphic))]
public class AutoRecoveryBarGraphicEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 色設定と角度設定は、後でまとめて表示する
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "backgroundColor",
            "recoveryColor",
            "angleA",
            "angleB");

        EditorGUILayout.Space(8);

        // 回復タイマーバーの色を縦に並べて表示する
        EditorGUILayout.LabelField(
            "色設定",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("backgroundColor"),
            new GUIContent("Background Color"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("recoveryColor"),
            new GUIContent("Recovery Color"));

        EditorGUILayout.Space(8);

        // 平行四辺形の角度設定を色設定の下にまとめる
        EditorGUILayout.LabelField(
            "角度設定",
            EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("angleA"),
            new GUIContent("Angle A"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("angleB"),
            new GUIContent("Angle B"));

        serializedObject.ApplyModifiedProperties();
    }
}
