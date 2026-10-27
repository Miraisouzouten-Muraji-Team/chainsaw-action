using UnityEditor;
using UnityEngine;

/// <summary>
/// GlowArcGraphicのInspector表示をカスタマイズする。
/// </summary>
[CustomEditor(typeof(TachometerGraphic))]
public class GlowArcGraphicEditor : Editor
{
    // GlowArcGraphicのシリアライズされた設定項目
    private SerializedProperty arcMaterial;
    private SerializedProperty startAngle;
    private SerializedProperty endAngle;
    private SerializedProperty thickness;
    private SerializedProperty colorStops;
    private SerializedProperty accelerator;
    private SerializedProperty needle;

    private void OnEnable()
    {
        // GlowArcGraphicの各フィールドを取得する
        arcMaterial = serializedObject.FindProperty("arcMaterial");
        startAngle = serializedObject.FindProperty("startAngle");
        endAngle = serializedObject.FindProperty("endAngle");
        thickness = serializedObject.FindProperty("thickness");
        colorStops = serializedObject.FindProperty("colorStops");
        accelerator = serializedObject.FindProperty("accelerator");
        needle = serializedObject.FindProperty("needle");
    }

    public override void OnInspectorGUI()
    {
        // Inspector上の変更を読み込む
        serializedObject.Update();

        // マテリアル設定
        EditorGUILayout.PropertyField(arcMaterial);

        EditorGUILayout.Space();

        // 円弧設定
        EditorGUILayout.PropertyField(startAngle);
        EditorGUILayout.PropertyField(endAngle);
        EditorGUILayout.PropertyField(thickness);

        EditorGUILayout.Space();

        // 色設定リストを表示する
        EditorGUILayout.PropertyField(
            colorStops,
            new GUIContent("Color Stops"),
            false
        );

        if (colorStops.isExpanded)
        {
            EditorGUI.indentLevel++;

            // Elementの追加・削除を可能にする
            for (int i = 0; i < colorStops.arraySize; i++)
            {
                SerializedProperty element =
                    colorStops.GetArrayElementAtIndex(i);

                SerializedProperty position =
                    element.FindPropertyRelative("position");

                SerializedProperty color =
                    element.FindPropertyRelative("color");

                SerializedProperty gradientFromPrevious =
                    element.FindPropertyRelative("gradientFromPrevious");

                SerializedProperty gradientWidth =
                    element.FindPropertyRelative("gradientWidth");

                EditorGUILayout.BeginVertical(
                    EditorStyles.helpBox
                );

                EditorGUILayout.LabelField(
                    $"Element {i}",
                    EditorStyles.boldLabel
                );

                EditorGUILayout.PropertyField(position);
                EditorGUILayout.PropertyField(color);

                // Element 0にはグラデーション設定を表示しない
                if (i > 0)
                {
                    EditorGUILayout.PropertyField(
                        gradientFromPrevious,
                        new GUIContent("グラデーションを有効化")
                    );

                    // ONの場合だけグラデーション幅を表示する
                    if (gradientFromPrevious.boolValue)
                    {
                        EditorGUILayout.PropertyField(
                            gradientWidth,
                            new GUIContent("グラデーション幅")
                        );
                    }
                }

                // 各Elementを削除する
                if (GUILayout.Button("Elementを削除"))
                {
                    colorStops.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            // Elementを追加する
            if (GUILayout.Button("+ Elementを追加"))
            {
                int newIndex = colorStops.arraySize;
                colorStops.InsertArrayElementAtIndex(newIndex);

                SerializedProperty newElement =
                    colorStops.GetArrayElementAtIndex(newIndex);

                newElement.FindPropertyRelative("position").floatValue = 0.5f;
                newElement.FindPropertyRelative("color").colorValue = Color.white;
                newElement.FindPropertyRelative("gradientFromPrevious").boolValue = false;
                newElement.FindPropertyRelative("gradientWidth").floatValue = 0.03f;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // 針の設定
        EditorGUILayout.PropertyField(accelerator);
        EditorGUILayout.PropertyField(needle);

        // Inspectorの変更を反映する
        serializedObject.ApplyModifiedProperties();
    }
}
