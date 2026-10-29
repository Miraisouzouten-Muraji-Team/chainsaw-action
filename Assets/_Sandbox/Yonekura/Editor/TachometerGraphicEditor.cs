using UnityEditor;
using UnityEngine;

/// <summary>
/// TachometerGraphicのInspector表示をカスタマイズする。
/// </summary>
[CustomEditor(typeof(TachometerGraphic))]
public class TachometerGraphicEditor : Editor
{
    // TachometerGraphicのシリアライズされた設定項目
    private SerializedProperty arcMaterial;
    private SerializedProperty startAngle;
    private SerializedProperty endAngle;
    private SerializedProperty thickness;
    private SerializedProperty colorStops;
    private SerializedProperty accelerator;
    private SerializedProperty needle;

    private void OnEnable()
    {
        // 各フィールドを取得する
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
        serializedObject.Update();

        // マテリアル設定
        EditorGUILayout.PropertyField(arcMaterial);

        EditorGUILayout.Space();

        // 円弧設定
        EditorGUILayout.PropertyField(startAngle);
        EditorGUILayout.PropertyField(endAngle);
        EditorGUILayout.PropertyField(thickness);

        EditorGUILayout.Space();

        // 色設定
        EditorGUILayout.LabelField("色設定", EditorStyles.boldLabel);

        if (colorStops == null)
        {
            EditorGUILayout.HelpBox(
                "colorStopsを取得できませんでした。フィールド名を確認してください。",
                MessageType.Error
            );
        }
        else
        {
            // 色設定は常に展開して表示する
            colorStops.isExpanded = true;

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

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

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

                // 削除ボタンが押されたら、配列からElementを削除する
                bool deleteElement = GUILayout.Button("Elementを削除");

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();

                if (deleteElement)
                {
                    colorStops.DeleteArrayElementAtIndex(i);
                    break;
                }
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
        }

        EditorGUILayout.Space();

        // 針の設定
        EditorGUILayout.PropertyField(accelerator);
        EditorGUILayout.PropertyField(needle);

        // Inspectorの変更を反映する
        serializedObject.ApplyModifiedProperties();
    }
}
