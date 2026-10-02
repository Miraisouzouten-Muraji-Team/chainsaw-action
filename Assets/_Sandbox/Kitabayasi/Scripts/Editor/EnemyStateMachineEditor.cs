using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EnemyStateMachineのInspector表示を拡張し、
/// 使用するStrategyを選択できるようにする。
/// </summary>
/// <remarks>
/// 責務:
/// ・Search / Alert Strategyの実装型を取得する。
/// ・Inspectorから使用するSearch / Alert Strategyとその設定を編集できるようにする。
/// ・選択されたStrategyをEnemyStateMachineのSerializeReferenceへ設定する。
/// ・ChargeEnemySearchStrategy使用時の索敵デバッグ表示を行う。
///
/// 担当しない責務:
/// ・Strategyの実行。
/// ・Stateの生成やState遷移。
/// ・Enemyのゲームロジック。
/// </remarks>
[CustomEditor(typeof(EnemyStateMachine))]
public sealed class EnemyStateMachineEditor : Editor
{
    private const string SEARCH_STRATEGY_PROPERTY_NAME =
        "searchStrategy";

    private const string ALERT_STRATEGY_PROPERTY_NAME = "alertStrategy";

    private const string SHOW_SEARCH_DEBUG_PROPERTY_NAME =
        "showSearchDebugVisualization";

    private const string PATROL_DISTANCE_PROPERTY_NAME =
        "patrolDistance";

    private const string DETECTION_RADIUS_PROPERTY_NAME =
        "detectionRadius";

    private const string PLAYER_TAG =
        "Player";

    private const float DIRECTION_EPSILON =
        0.001f;

    private SerializedProperty searchStrategyProperty;
    private SerializedProperty alertStrategyProperty;
    private SerializedProperty showSearchDebugProperty;

    private void OnEnable()
    {
        searchStrategyProperty =
            serializedObject.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        alertStrategyProperty = serializedObject.FindProperty(ALERT_STRATEGY_PROPERTY_NAME);

        showSearchDebugProperty =
            serializedObject.FindProperty(
                SHOW_SEARCH_DEBUG_PROPERTY_NAME);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty property =
            serializedObject.GetIterator();

        bool enterChildren = true;

        while (property.NextVisible(
                   enterChildren))
        {
            enterChildren = false;

            if (property.propertyPath ==
                "m_Script")
            {
                using (
                    new EditorGUI.DisabledScope(
                        true))
                {
                    EditorGUILayout.PropertyField(
                        property);
                }

                continue;
            }

            if (property.propertyPath ==
                SEARCH_STRATEGY_PROPERTY_NAME)
            {
                DrawStrategySelector<IEnemySearchStrategy>(
                    searchStrategyProperty, "索敵", "Search Strategy");
                continue;
            }

            if (property.propertyPath == ALERT_STRATEGY_PROPERTY_NAME)
            {
                DrawStrategySelector<IEnemyAlertStrategy>(
                    alertStrategyProperty, "攻撃予告", "Alert Strategy");
                continue;
            }

            EditorGUILayout.PropertyField(
                property,
                true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        if (showSearchDebugProperty == null ||
            !showSearchDebugProperty.boolValue)
        {
            return;
        }

        if (searchStrategyProperty
                .managedReferenceValue
            is not ChargeEnemySearchStrategy)
        {
            return;
        }

        EnemyStateMachine stateMachine =
            (EnemyStateMachine)target;

        EnemyDataReference dataReference =
            stateMachine.GetComponent<EnemyDataReference>();

        if (dataReference == null)
        {
            return;
        }

        if (dataReference.Data
            is not ChargeEnemyData chargeEnemyData)
        {
            return;
        }

        DrawSearchVisualization(
            stateMachine,
            chargeEnemyData);
    }

    /// <summary>
    /// ChargeEnemySearchStrategy用の
    /// Sceneビュー表示を描画する。
    /// </summary>
    private static void DrawSearchVisualization(
        EnemyStateMachine stateMachine,
        ChargeEnemyData chargeEnemyData)
    {
        SerializedObject dataSerializedObject =
            new SerializedObject(
                chargeEnemyData);

        dataSerializedObject.Update();

        SerializedProperty patrolDistanceProperty =
            dataSerializedObject.FindProperty(
                PATROL_DISTANCE_PROPERTY_NAME);

        SerializedProperty detectionRadiusProperty =
            dataSerializedObject.FindProperty(
                DETECTION_RADIUS_PROPERTY_NAME);

        if (patrolDistanceProperty == null ||
            detectionRadiusProperty == null)
        {
            return;
        }

        Transform enemyTransform =
            stateMachine.transform;

        Vector3 startPosition =
            enemyTransform.position;

        float directionSign =
            GetInitialHorizontalDirectionSign(
                enemyTransform);

        Vector3 patrolDirection =
            Vector3.right *
            directionSign;

        float patrolDistance =
            patrolDistanceProperty.floatValue;

        float detectionRadius =
            detectionRadiusProperty.floatValue;

        Vector3 endPosition =
            startPosition +
            patrolDirection *
            patrolDistance;

        DrawDetectionRadius(
            startPosition,
            detectionRadius);

        DrawPatrolRange(
            startPosition,
            endPosition);

        DrawPlayerRay(
            startPosition,
            detectionRadius);

        DrawPatrolDistanceHandle(
            chargeEnemyData,
            dataSerializedObject,
            patrolDistanceProperty,
            startPosition,
            endPosition,
            patrolDirection);
    }

    /// <summary>
    /// XY平面上の検知範囲を描画する。
    /// </summary>
    private static void DrawDetectionRadius(
        Vector3 center,
        float radius)
    {
        Handles.color =
            new Color(
                1f,
                0.35f,
                0.35f,
                1f);

        // XY平面の円なので、
        // 円の法線はZ方向。
        Handles.DrawWireDisc(
            center,
            Vector3.forward,
            radius);

        Handles.Label(
            center +
            Vector3.up *
            radius,
            $"Detection Radius: {radius:0.##}");
    }

    /// <summary>
    /// 巡回開始地点から折り返し地点までを描画する。
    /// </summary>
    private static void DrawPatrolRange(
        Vector3 startPosition,
        Vector3 endPosition)
    {
        Handles.color =
            new Color(
                0.2f,
                0.8f,
                1f,
                1f);

        Handles.DrawLine(
            startPosition,
            endPosition);

        float startHandleSize =
            HandleUtility.GetHandleSize(
                startPosition) *
            0.08f;

        Handles.CubeHandleCap(
            0,
            startPosition,
            Quaternion.identity,
            startHandleSize,
            EventType.Repaint);
    }

    /// <summary>
    /// Playerが検知範囲内にいる場合、
    /// EnemyからPlayer方向へのRayをSceneビューへ描画する。
    /// </summary>
    private static void DrawPlayerRay(
        Vector3 startPosition,
        float detectionRadius)
    {
        GameObject playerObject =
            GameObject.FindGameObjectWithTag(
                PLAYER_TAG);

        if (playerObject == null)
        {
            return;
        }

        Vector3 playerPosition =
            playerObject.transform.position;

        float deltaX =
            playerPosition.x -
            startPosition.x;

        float deltaY =
            playerPosition.y -
            startPosition.y;

        float squaredDistance =
            deltaX * deltaX +
            deltaY * deltaY;

        if (squaredDistance >
            detectionRadius *
            detectionRadius)
        {
            return;
        }

        Handles.color =
            new Color(
                1f,
                0.9f,
                0.2f,
                1f);

        Handles.DrawDottedLine(
            startPosition,
            playerPosition,
            4f);
    }

    /// <summary>
    /// 巡回折り返し地点をSceneビューから編集する。
    /// </summary>
    private static void DrawPatrolDistanceHandle(
        ChargeEnemyData chargeEnemyData,
        SerializedObject dataSerializedObject,
        SerializedProperty patrolDistanceProperty,
        Vector3 startPosition,
        Vector3 endPosition,
        Vector3 patrolDirection)
    {
        float handleSize =
            HandleUtility.GetHandleSize(
                endPosition) *
            0.12f;

        Handles.color =
            new Color(
                0.2f,
                0.8f,
                1f,
                1f);

        EditorGUI.BeginChangeCheck();

        Vector3 movedPosition =
            Handles.Slider(
                endPosition,
                patrolDirection,
                handleSize,
                Handles.CubeHandleCap,
                0f);

        if (!EditorGUI.EndChangeCheck())
        {
            Handles.Label(
                endPosition +
                Vector3.up *
                handleSize,
                $"Patrol Distance: " +
                $"{patrolDistanceProperty.floatValue:0.##}");

            return;
        }

        float newDistance =
            Vector3.Dot(
                movedPosition -
                startPosition,
                patrolDirection);

        newDistance =
            Mathf.Max(
                0f,
                newDistance);

        Undo.RecordObject(
            chargeEnemyData,
            "Change Patrol Distance");

        patrolDistanceProperty.floatValue =
            newDistance;

        dataSerializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(
            chargeEnemyData);

        SceneView.RepaintAll();
    }

    /// <summary>
    /// Enemyの初期向きからX方向の巡回方向を取得する。
    /// </summary>
    private static float GetInitialHorizontalDirectionSign(
        Transform targetTransform)
    {
        float forwardX =
            targetTransform.forward.x;

        if (Mathf.Abs(forwardX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                forwardX);
        }

        float rightX =
            targetTransform.right.x;

        if (Mathf.Abs(rightX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                rightX);
        }

        return 1f;
    }

    /// <summary>
    /// 既存の型選択方式をSearch / Alertで共用する。
    /// </summary>
    private void DrawStrategySelector<TStrategy>(
        SerializedProperty strategyProperty,
        string heading,
        string label)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);

        // 実行中にInspector上のStrategyと初期化済みStateの参照が食い違うのを防ぐ。
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            string currentName = strategyProperty.managedReferenceValue?.GetType().Name
                ?? "未設定";
            Rect buttonRect = EditorGUI.PrefixLabel(
                EditorGUILayout.GetControlRect(), new GUIContent(label));

            if (EditorGUI.DropdownButton(
                    buttonRect, new GUIContent(currentName), FocusType.Keyboard))
            {
                ShowStrategyMenu<TStrategy>(strategyProperty, buttonRect);
            }

            if (strategyProperty.managedReferenceValue == null)
            {
                return;
            }

            SerializedProperty child = strategyProperty.Copy();
            SerializedProperty end = child.GetEndProperty();
            bool hasChild = child.NextVisible(true);
            EditorGUI.indentLevel++;
            while (hasChild && !SerializedProperty.EqualContents(child, end))
            {
                EditorGUILayout.PropertyField(child, true);
                hasChild = child.NextVisible(false);
            }

            EditorGUI.indentLevel--;
        }
    }

    private void ShowStrategyMenu<TStrategy>(
        SerializedProperty strategyProperty,
        Rect buttonRect)
    {
        GenericMenu menu = new GenericMenu();
        string propertyPath = strategyProperty.propertyPath;
        Type currentType = strategyProperty.managedReferenceValue?.GetType();

        menu.AddItem(
            new GUIContent("未設定"), currentType == null,
            () => SetStrategy(propertyPath, null));
        menu.AddSeparator(string.Empty);

        List<Type> strategyTypes = GetStrategyTypes<TStrategy>();
        if (strategyTypes.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("利用可能なStrategyがありません。"));
        }

        foreach (Type strategyType in strategyTypes)
        {
            menu.AddItem(
                new GUIContent(strategyType.Name),
                currentType == strategyType,
                () => SetStrategy(propertyPath, strategyType));
        }

        menu.DropDown(buttonRect);
    }

    private static List<Type> GetStrategyTypes<TStrategy>()
    {
        List<Type> strategyTypes = new List<Type>();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<TStrategy>())
        {
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters ||
                !type.IsSerializable ||
                typeof(UnityEngine.Object).IsAssignableFrom(type) ||
                type.GetConstructor(Type.EmptyTypes) == null)
            {
                continue;
            }

            strategyTypes.Add(type);
        }

        strategyTypes.Sort((left, right) =>
            string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
        return strategyTypes;
    }

    private void SetStrategy(string propertyPath, Type strategyType)
    {
        serializedObject.Update();
        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        property.managedReferenceValue = strategyType == null
            ? null
            : Activator.CreateInstance(strategyType);
        serializedObject.ApplyModifiedProperties();
    }
}
