using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EnemyStateMachineのInspector表示を拡張し、
/// 使用するStrategyの選択と実行中Stateの確認ができるようにする。
/// </summary>
/// <remarks>
/// 責務:
/// ・Search / Alert / Attack Strategyの実装型を取得する。
/// ・Inspectorから使用するSearch / Alert / Attack Strategyとその設定を編集できるようにする。
/// ・選択されたStrategyをEnemyStateMachineのSerializeReferenceへ設定する。
/// ・Play中の現在StateをInspectorへ読み取り専用で表示する。
/// ・ChargeEnemySearchStrategy使用時の巡回範囲編集を行う。
/// ・DroneEnemySearchStrategy選択時にPatrolPointA/Bを生成・再利用して参照を設定する。
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

    private const string ALERT_STRATEGY_PROPERTY_NAME =
        "alertStrategy";

    private const string ATTACK_STRATEGY_PROPERTY_NAME =
        "attackStrategy";

    private const string SHOW_PATROL_DEBUG_PROPERTY_NAME =
        "showPatrolDebugVisualization";

    private const string VISUAL_ROOT_PROPERTY_NAME =
        "visualRoot";

    private const string PATROL_DISTANCE_PROPERTY_NAME =
        "patrolDistance";

    private const string DRONE_PATROL_POINT_A_NAME =
        "PatrolPointA";

    private const string DRONE_PATROL_POINT_B_NAME =
        "PatrolPointB";

    private const string DRONE_PATROL_POINT_A_PROPERTY_NAME =
        "patrolPointA";

    private const string DRONE_PATROL_POINT_B_PROPERTY_NAME =
        "patrolPointB";

    private const float DRONE_DEFAULT_PATROL_OFFSET_MINIMUM =
        0.5f;

    private const float DIRECTION_EPSILON =
        0.001f;

    private SerializedProperty searchStrategyProperty;
    private SerializedProperty alertStrategyProperty;
    private SerializedProperty attackStrategyProperty;
    private SerializedProperty showPatrolDebugProperty;
    private SerializedProperty visualRootProperty;

    private void OnEnable()
    {
        searchStrategyProperty =
            serializedObject.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        alertStrategyProperty =
            serializedObject.FindProperty(
                ALERT_STRATEGY_PROPERTY_NAME);

        attackStrategyProperty =
            serializedObject.FindProperty(
                ATTACK_STRATEGY_PROPERTY_NAME);

        showPatrolDebugProperty =
            serializedObject.FindProperty(
                SHOW_PATROL_DEBUG_PROPERTY_NAME);

        visualRootProperty =
            serializedObject.FindProperty(
                VISUAL_ROOT_PROPERTY_NAME);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawCurrentState();

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
                    searchStrategyProperty,
                    "索敵",
                    "Search Strategy");
                continue;
            }

            if (property.propertyPath ==
                ALERT_STRATEGY_PROPERTY_NAME)
            {
                DrawStrategySelector<IEnemyAlertStrategy>(
                    alertStrategyProperty,
                    "攻撃予告",
                    "Alert Strategy");
                continue;
            }

            if (property.propertyPath ==
                ATTACK_STRATEGY_PROPERTY_NAME)
            {
                DrawStrategySelector<IEnemyAttackStrategy>(
                    attackStrategyProperty,
                    "攻撃",
                    "Attack Strategy");
                continue;
            }

            EditorGUILayout.PropertyField(
                property,
                true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Play中の現在StateをInspectorへ表示する。
    /// Stateの実体はEnemyStateMachine.CurrentStateを正とし、
    /// Editor側では表示用の状態を別途保持しない。
    /// </summary>
    private void DrawCurrentState()
    {
        EnemyStateMachine stateMachine =
            (EnemyStateMachine)target;

        string currentStateName =
            !EditorApplication.isPlaying
                ? "Playモードで確認できます"
                : stateMachine.CurrentState?.GetType().Name
                  ?? "未設定";

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "実行状態",
            EditorStyles.boldLabel);

        EditorGUILayout.LabelField(
            "Current State",
            currentStateName);
    }

    public override bool RequiresConstantRepaint()
    {
        // State遷移はSerializePropertyの変更ではないため、
        // Play中はInspectorを再描画して現在Stateを追従表示する。
        return EditorApplication.isPlaying;
    }

    [DrawGizmo(
        GizmoType.NonSelected |
        GizmoType.InSelectionHierarchy)]
    private static void DrawPatrolRangeGizmo(
        EnemyStateMachine stateMachine,
        GizmoType gizmoType)
    {
        if (stateMachine == null)
        {
            return;
        }

        SerializedObject serializedStateMachine =
            new SerializedObject(
                stateMachine);

        serializedStateMachine.Update();

        SerializedProperty showPatrolDebugProperty =
            serializedStateMachine.FindProperty(
                SHOW_PATROL_DEBUG_PROPERTY_NAME);

        if (showPatrolDebugProperty == null ||
            !showPatrolDebugProperty.boolValue)
        {
            return;
        }

        SerializedProperty searchStrategyProperty =
            serializedStateMachine.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        if (searchStrategyProperty?.managedReferenceValue
            is not ChargeEnemySearchStrategy)
        {
            return;
        }

        SerializedProperty visualRootProperty =
            serializedStateMachine.FindProperty(
                VISUAL_ROOT_PROPERTY_NAME);

        Transform visualRoot =
            visualRootProperty?.objectReferenceValue
            as Transform;

        if (visualRoot == null ||
            !TryGetChargeEnemyData(
                stateMachine,
                out ChargeEnemyData chargeEnemyData))
        {
            return;
        }

        Vector3 patrolStartPosition =
            stateMachine.transform.position;

        Vector3 patrolDirection =
            Vector3.right *
            GetInitialHorizontalDirectionSign(
                visualRoot);

        Vector3 patrolEndPosition =
            patrolStartPosition +
            patrolDirection *
            chargeEnemyData.PatrolDistance;

        DrawPatrolRange(
            patrolStartPosition,
            patrolEndPosition);
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        if (showPatrolDebugProperty == null ||
            !showPatrolDebugProperty.boolValue)
        {
            return;
        }

        if (searchStrategyProperty
                .managedReferenceValue
            is not ChargeEnemySearchStrategy)
        {
            return;
        }

        Transform visualRoot =
            visualRootProperty?.objectReferenceValue
            as Transform;

        if (visualRoot == null)
        {
            return;
        }

        EnemyStateMachine stateMachine =
            (EnemyStateMachine)target;

        if (!TryGetChargeEnemyData(
                stateMachine,
                out ChargeEnemyData chargeEnemyData))
        {
            return;
        }

        DrawPatrolDistanceEditor(
            stateMachine,
            visualRoot,
            chargeEnemyData);
    }

    /// <summary>
    /// 選択中のChargeEnemyについて、
    /// 巡回折り返し地点の編集ハンドルをSceneビューへ描画する。
    /// 常時表示する巡回範囲自体はDrawPatrolRangeGizmoが担当する。
    /// </summary>
    private static void DrawPatrolDistanceEditor(
        EnemyStateMachine stateMachine,
        Transform visualRoot,
        ChargeEnemyData chargeEnemyData)
    {
        SerializedObject dataSerializedObject =
            new SerializedObject(
                chargeEnemyData);

        dataSerializedObject.Update();

        SerializedProperty patrolDistanceProperty =
            dataSerializedObject.FindProperty(
                PATROL_DISTANCE_PROPERTY_NAME);

        if (patrolDistanceProperty == null)
        {
            return;
        }

        Vector3 patrolStartPosition =
            stateMachine.transform.position;

        Vector3 patrolDirection =
            Vector3.right *
            GetInitialHorizontalDirectionSign(
                visualRoot);

        Vector3 endPosition =
            patrolStartPosition +
            patrolDirection *
            patrolDistanceProperty.floatValue;

        DrawPatrolDistanceHandle(
            chargeEnemyData,
            dataSerializedObject,
            patrolDistanceProperty,
            patrolStartPosition,
            endPosition,
            patrolDirection);
    }

    /// <summary>
    /// 巡回開始地点から折り返し地点までを常時描画する。
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

        float endHandleSize =
            HandleUtility.GetHandleSize(
                endPosition) *
            0.1f;

        Handles.CubeHandleCap(
            0,
            endPosition,
            Quaternion.identity,
            endHandleSize,
            EventType.Repaint);
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
    /// VisualRootの初期向きからX方向の巡回方向を取得する。
    /// 実際のChargeEnemySearchStrategyと同じ判定規則を使用する。
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

    private static bool TryGetChargeEnemyData(
        EnemyStateMachine stateMachine,
        out ChargeEnemyData chargeEnemyData)
    {
        EnemyDataReference dataReference =
            stateMachine.GetComponent<EnemyDataReference>();

        chargeEnemyData =
            dataReference?.Data
            as ChargeEnemyData;

        return chargeEnemyData != null;
    }

    /// <summary>
    /// 既存の型選択方式をSearch / Alert / Attackで共用する。
    /// </summary>
    private void DrawStrategySelector<TStrategy>(
        SerializedProperty strategyProperty,
        string heading,
        string label)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            heading,
            EditorStyles.boldLabel);

        // 実行中にInspector上のStrategyと初期化済みStateの参照が食い違うのを防ぐ。
        using (
            new EditorGUI.DisabledScope(
                EditorApplication.isPlaying))
        {
            string currentName =
                strategyProperty
                    .managedReferenceValue?
                    .GetType()
                    .Name
                ?? "未設定";

            Rect buttonRect =
                EditorGUI.PrefixLabel(
                    EditorGUILayout.GetControlRect(),
                    new GUIContent(label));

            if (EditorGUI.DropdownButton(
                    buttonRect,
                    new GUIContent(currentName),
                    FocusType.Keyboard))
            {
                ShowStrategyMenu<TStrategy>(
                    strategyProperty,
                    buttonRect);
            }

            if (strategyProperty.managedReferenceValue == null)
            {
                return;
            }

            SerializedProperty child =
                strategyProperty.Copy();

            SerializedProperty end =
                child.GetEndProperty();

            bool hasChild =
                child.NextVisible(true);

            EditorGUI.indentLevel++;

            while (hasChild &&
                   !SerializedProperty.EqualContents(
                       child,
                       end))
            {
                EditorGUILayout.PropertyField(
                    child,
                    true);

                hasChild =
                    child.NextVisible(false);
            }

            EditorGUI.indentLevel--;
        }
    }

    private void ShowStrategyMenu<TStrategy>(
        SerializedProperty strategyProperty,
        Rect buttonRect)
    {
        GenericMenu menu =
            new GenericMenu();

        string propertyPath =
            strategyProperty.propertyPath;

        Type currentType =
            strategyProperty
                .managedReferenceValue?
                .GetType();

        menu.AddItem(
            new GUIContent("未設定"),
            currentType == null,
            () => SetStrategy(
                propertyPath,
                null));

        menu.AddSeparator(
            string.Empty);

        List<Type> strategyTypes =
            GetStrategyTypes<TStrategy>();

        if (strategyTypes.Count == 0)
        {
            menu.AddDisabledItem(
                new GUIContent(
                    "利用可能なStrategyがありません。"));
        }

        foreach (Type strategyType in strategyTypes)
        {
            menu.AddItem(
                new GUIContent(
                    strategyType.Name),
                currentType == strategyType,
                () => SetStrategy(
                    propertyPath,
                    strategyType));
        }

        menu.DropDown(
            buttonRect);
    }

    private static List<Type> GetStrategyTypes<TStrategy>()
    {
        List<Type> strategyTypes =
            new List<Type>();

        foreach (
            Type type
            in TypeCache.GetTypesDerivedFrom<TStrategy>())
        {
            if (!type.IsClass ||
                type.IsAbstract ||
                type.ContainsGenericParameters ||
                !type.IsSerializable ||
                typeof(UnityEngine.Object)
                    .IsAssignableFrom(type) ||
                type.GetConstructor(
                    Type.EmptyTypes) == null)
            {
                continue;
            }

            strategyTypes.Add(type);
        }

        strategyTypes.Sort(
            (left, right) =>
                string.Compare(
                    left.FullName,
                    right.FullName,
                    StringComparison.Ordinal));

        return strategyTypes;
    }

    private void SetStrategy(
        string propertyPath,
        Type strategyType)
    {
        EnemyStateMachine stateMachine =
            (EnemyStateMachine)target;

        Undo.RecordObject(
            stateMachine,
            "Change Enemy Strategy");

        serializedObject.Update();

        SerializedProperty property =
            serializedObject.FindProperty(
                propertyPath);

        property.managedReferenceValue =
            strategyType == null
                ? null
                : Activator.CreateInstance(
                    strategyType);

        serializedObject.ApplyModifiedProperties();

        if (propertyPath ==
                SEARCH_STRATEGY_PROPERTY_NAME &&
            strategyType ==
                typeof(DroneEnemySearchStrategy))
        {
            EnsureDronePatrolPoints(
                stateMachine);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(
            stateMachine);

        EditorUtility.SetDirty(
            stateMachine);
    }

    /// <summary>
    /// Drone SearchのAuthoring用PointをEnemy直下へ用意し、
    /// SerializeReference内のStrategyへ参照を設定する。
    /// 既存Pointは再利用し、Strategyを選び直しても増殖させない。
    /// </summary>
    private void EnsureDronePatrolPoints(
        EnemyStateMachine stateMachine)
    {
        int undoGroup =
            Undo.GetCurrentGroup();

        Undo.SetCurrentGroupName(
            "Configure Drone Patrol Points");

        Transform enemyTransform =
            stateMachine.transform;

        float initialOffset =
            GetDroneInitialPatrolOffset(
                stateMachine);

        Transform pointA =
            FindOrCreateDronePatrolPoint(
                enemyTransform,
                DRONE_PATROL_POINT_A_NAME,
                -initialOffset);

        Transform pointB =
            FindOrCreateDronePatrolPoint(
                enemyTransform,
                DRONE_PATROL_POINT_B_NAME,
                initialOffset);

        serializedObject.Update();

        SerializedProperty droneStrategyProperty =
            serializedObject.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        if (droneStrategyProperty?.managedReferenceValue
            is not DroneEnemySearchStrategy)
        {
            Undo.CollapseUndoOperations(
                undoGroup);

            return;
        }

        SerializedProperty pointAProperty =
            droneStrategyProperty.FindPropertyRelative(
                DRONE_PATROL_POINT_A_PROPERTY_NAME);

        SerializedProperty pointBProperty =
            droneStrategyProperty.FindPropertyRelative(
                DRONE_PATROL_POINT_B_PROPERTY_NAME);

        if (pointAProperty == null ||
            pointBProperty == null)
        {
            Debug.LogError(
                $"{nameof(EnemyStateMachineEditor)}: " +
                $"{nameof(DroneEnemySearchStrategy)}の" +
                "PatrolPoint参照を取得できません。",
                stateMachine);

            Undo.CollapseUndoOperations(
                undoGroup);

            return;
        }

        Undo.RecordObject(
            stateMachine,
            "Assign Drone Patrol Points");

        pointAProperty.objectReferenceValue =
            pointA;

        pointBProperty.objectReferenceValue =
            pointB;

        serializedObject.ApplyModifiedProperties();

        PrefabUtility.RecordPrefabInstancePropertyModifications(
            stateMachine);

        EditorUtility.SetDirty(
            stateMachine);

        Undo.CollapseUndoOperations(
            undoGroup);

        SceneView.RepaintAll();
    }

    private static Transform FindOrCreateDronePatrolPoint(
        Transform enemyTransform,
        string pointName,
        float xOffset)
    {
        Transform existingPoint =
            enemyTransform.Find(
                pointName);

        if (existingPoint != null)
        {
            return existingPoint;
        }

        GameObject pointObject =
            new GameObject(
                pointName);

        Undo.RegisterCreatedObjectUndo(
            pointObject,
            $"Create {pointName}");

        Undo.SetTransformParent(
            pointObject.transform,
            enemyTransform,
            $"Parent {pointName}");

        Undo.RecordObject(
            pointObject.transform,
            $"Initialize {pointName}");

        pointObject.transform.position =
            enemyTransform.position +
            Vector3.right *
            xOffset;

        PrefabUtility.RecordPrefabInstancePropertyModifications(
            pointObject.transform);

        EditorUtility.SetDirty(
            pointObject.transform);

        return pointObject.transform;
    }

    private static float GetDroneInitialPatrolOffset(
        EnemyStateMachine stateMachine)
    {
        EnemyDataReference dataReference =
            stateMachine.GetComponent<EnemyDataReference>();

        float detectionRadius =
            dataReference?.Data?.DetectionRadius
            ?? 0f;

        return detectionRadius >
            DIRECTION_EPSILON
            ? detectionRadius * 0.5f
            : DRONE_DEFAULT_PATROL_OFFSET_MINIMUM;
    }
}
