using UnityEditor;
using UnityEngine;

namespace Project.InGame.Boss.Editor
{
    /// <summary>
    /// 攻撃種類に応じた設定をInspectorへ表示する。
    /// </summary>
    [CustomEditor(typeof(BossAttackDefinition))]
    public sealed class BossAttackDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty typeProperty;
        private SerializedProperty rangeProperty;
        private SerializedProperty targetDistanceRatioProperty;
        private SerializedProperty recoveryTimeProperty;

        private SerializedProperty animatorStateProperty;
        private SerializedProperty noShieldAnimatorStateProperty;

        private SerializedProperty parametersProperty;

        private void OnEnable()
        {
            typeProperty = serializedObject.FindProperty("type");

            rangeProperty = serializedObject.FindProperty("range");

            targetDistanceRatioProperty = serializedObject.FindProperty("targetDistanceRatio");

            recoveryTimeProperty = serializedObject.FindProperty("recoveryTime");

            animatorStateProperty = serializedObject.FindProperty("animatorState");

            noShieldAnimatorStateProperty = serializedObject.FindProperty("noShieldAnimatorState");

            parametersProperty = serializedObject.FindProperty("parameters");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(
                typeProperty,
                new GUIContent("攻撃種類"));

            bool typeChanged = EditorGUI.EndChangeCheck();

            BossAttackType selectedType = (BossAttackType)typeProperty.enumValueIndex;

            // 攻撃種類と専用設定を一致させる。
            if (typeChanged)
            {
                parametersProperty.managedReferenceValue = BossAttackParameters.Create(selectedType);
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
                "共通設定",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(rangeProperty);
            EditorGUILayout.PropertyField(targetDistanceRatioProperty);
            EditorGUILayout.PropertyField(recoveryTimeProperty);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField(
                "アニメーション設定",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                animatorStateProperty,
                new GUIContent("通常ステート"));

            bool requiresShield =
                selectedType == BossAttackType.ShieldBash ||
                selectedType == BossAttackType.ShieldThrow;

            if (!requiresShield)
            {
                EditorGUILayout.PropertyField(
                    noShieldAnimatorStateProperty,
                    new GUIContent("盾なしステート"));
            }

            EditorGUILayout.HelpBox(
                "通常ステートが空欄の場合は仮モーションを使用します。\n" +
                "盾なしステートが空欄の場合は通常ステートを使用します。",
                MessageType.Info);

            if (parametersProperty.managedReferenceValue != null)
            {
                EditorGUILayout.Space();

                EditorGUILayout.LabelField(
                    "攻撃固有の設定",
                    EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(
                    parametersProperty,
                    GUIContent.none,
                    true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
