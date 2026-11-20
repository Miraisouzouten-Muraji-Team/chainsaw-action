using UnityEditor;
using UnityEngine;

namespace Project.InGame.Boss.Editor
{
    /// <summary>
    /// 攻撃種類に応じた設定だけをInspectorへ表示する。
    /// </summary>
    [CustomEditor(typeof(BossAttackDefinition))]
    public sealed class BossAttackDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty typeProperty;
        private SerializedProperty rangeProperty;
        private SerializedProperty targetDistanceRatioProperty;
        private SerializedProperty recoveryTimeProperty;
        private SerializedProperty animatorTriggerProperty;
        private SerializedProperty parametersProperty;

        private void OnEnable()
        {
            typeProperty = serializedObject.FindProperty("type");
            rangeProperty = serializedObject.FindProperty("range");
            targetDistanceRatioProperty =
                serializedObject.FindProperty("targetDistanceRatio");
            recoveryTimeProperty =
                serializedObject.FindProperty("recoveryTime");
            animatorTriggerProperty =
                serializedObject.FindProperty("animatorTrigger");
            parametersProperty =
                serializedObject.FindProperty("parameters");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                typeProperty, new GUIContent("攻撃種類"));
            bool typeChanged = EditorGUI.EndChangeCheck();

            BossAttackType selectedType =
                (BossAttackType)typeProperty.enumValueIndex;

            // 攻撃種類を切り替えたら、専用データも切り替える。
            if (typeChanged)
            {
                parametersProperty.managedReferenceValue =
                    BossAttackParameters.Create(selectedType);
            }
            else if (parametersProperty.managedReferenceValue
                     is not BossAttackParameters current ||
                     current.Type != selectedType)
            {
                EditorGUILayout.HelpBox(
                    "攻撃固有の設定が未作成、または種類と一致していません。",
                    MessageType.Warning);

                if (GUILayout.Button("現在の種類の設定を作成"))
                {
                    parametersProperty.managedReferenceValue =
                        BossAttackParameters.Create(selectedType);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "共通設定", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(rangeProperty);
            EditorGUILayout.PropertyField(targetDistanceRatioProperty);
            EditorGUILayout.PropertyField(recoveryTimeProperty);
            EditorGUILayout.PropertyField(animatorTriggerProperty);

            if (parametersProperty.managedReferenceValue != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "攻撃固有の設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(
                    parametersProperty, GUIContent.none, true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
