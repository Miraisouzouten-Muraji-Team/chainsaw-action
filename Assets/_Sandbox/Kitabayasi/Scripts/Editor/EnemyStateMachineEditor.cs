using System;
using System.Collections.Generic;
using System.Reflection;
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
/// ・ChargeEnemySearchStrategy使用時の巡回範囲と符号付きオフセットの編集を行う。
/// ・OldDroneEnemySearchStrategy選択時にPatrolPointA/Bを生成・再利用して参照を設定する。
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


    private const string PATROL_OFFSET_PROPERTY_NAME =
        "patrolOffset";

    private const string OLD_DRONE_PATROL_POINT_A_NAME =
        "PatrolPointA";

    private const string OLD_DRONE_PATROL_POINT_B_NAME =
        "PatrolPointB";

    private const string OLD_DRONE_PATROL_POINT_A_PROPERTY_NAME =
        "patrolPointA";

    private const string OLD_DRONE_PATROL_POINT_B_PROPERTY_NAME =
        "patrolPointB";

    private const float OLD_DRONE_DEFAULT_PATROL_OFFSET_MINIMUM =
        0.5f;

    private const float OLD_DRONE_DETECTION_RADIUS_EPSILON =
        0.001f;

    private static readonly FieldInfo SEARCH_STRATEGY_FIELD =
        typeof(EnemyStateMachine).GetField(
            SEARCH_STRATEGY_PROPERTY_NAME,
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SHOW_PATROL_DEBUG_FIELD =
        typeof(EnemyStateMachine).GetField(
            SHOW_PATROL_DEBUG_PROPERTY_NAME,
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo CHARGE_PATROL_OFFSET_FIELD =
        typeof(ChargeEnemySearchStrategy).GetField(
            PATROL_OFFSET_PROPERTY_NAME,
            BindingFlags.Instance | BindingFlags.NonPublic);


    private SerializedProperty searchStrategyProperty;
    private SerializedProperty alertStrategyProperty;
    private SerializedProperty attackStrategyProperty;
    private SerializedProperty showPatrolDebugProperty;

    private void OnEnable()
    {
        // Prefab再構築直後など、Editor生成時点でtargetが失効しているケースに備え、
        // OnEnableではSerializedObjectへ触れない。必要なPropertyはInspector描画時に取得する。
        ClearSerializedProperties();
    }

    private void OnDisable()
    {
        // Prefabの再構築や削除時に古いSerializedPropertyを保持し続けない。
        ClearSerializedProperties();
    }

    public override void OnInspectorGUI()
    {
        if (!TryGetStateMachineTarget(
                out EnemyStateMachine stateMachine))
        {
            return;
        }

        if (!AreSerializedPropertiesReady())
        {
            CacheSerializedProperties();
        }

        serializedObject.Update();

        DrawCurrentState(
            stateMachine);

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
    private static void DrawCurrentState(
        EnemyStateMachine stateMachine)
    {
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
        return EditorApplication.isPlaying &&
               target != null;
    }

    [DrawGizmo(
        GizmoType.NonSelected |
        GizmoType.InSelectionHierarchy)]
    private static void DrawPatrolRangeGizmo(
        EnemyStateMachine stateMachine,
        GizmoType gizmoType)
    {
        if (stateMachine == null ||
            !TryGetShowPatrolDebugVisualization(
                stateMachine,
                out bool showPatrolDebugVisualization) ||
            !showPatrolDebugVisualization ||
            !TryGetChargeEnemySearchStrategy(
                stateMachine,
                out ChargeEnemySearchStrategy chargeEnemySearchStrategy))
        {
            return;
        }

        Vector3 patrolStartPosition =
            stateMachine.transform.position;

        Vector3 patrolEndPosition =
            patrolStartPosition +
            Vector3.right *
            chargeEnemySearchStrategy.PatrolOffset;

        DrawPatrolRange(
            patrolStartPosition,
            patrolEndPosition);
    }

    private void OnSceneGUI()
    {
        // Unity公式仕様上、OnSceneGUIではEditor.serializedObjectを使用せず、
        // 有効なtargetを直接参照する。Prefab再構築直後のnull targetに対して
        // SerializedObjectを生成してしまうEditor例外もここで回避する。
        if (!TryGetStateMachineTarget(
                out EnemyStateMachine stateMachine) ||
            !TryGetShowPatrolDebugVisualization(
                stateMachine,
                out bool showPatrolDebugVisualization) ||
            !showPatrolDebugVisualization ||
            !TryGetChargeEnemySearchStrategy(
                stateMachine,
                out ChargeEnemySearchStrategy chargeEnemySearchStrategy))
        {
            return;
        }

        DrawPatrolOffsetEditor(
            stateMachine,
            chargeEnemySearchStrategy);
    }

    /// <summary>
    /// 選択中のChargeEnemyについて、
    /// 巡回折り返し地点の符号付きオフセット編集ハンドルをSceneビューへ描画する。
    /// 常時表示する巡回範囲自体はDrawPatrolRangeGizmoが担当する。
    /// </summary>
    private static void DrawPatrolOffsetEditor(
        EnemyStateMachine stateMachine,
        ChargeEnemySearchStrategy chargeEnemySearchStrategy)
    {
        Vector3 patrolStartPosition =
            stateMachine.transform.position;

        Vector3 patrolDirection =
            Vector3.right;

        Vector3 endPosition =
            patrolStartPosition +
            patrolDirection *
            chargeEnemySearchStrategy.PatrolOffset;

        DrawPatrolOffsetHandle(
            stateMachine,
            chargeEnemySearchStrategy,
            patrolStartPosition,
            endPosition,
            patrolDirection);
    }

    /// <summary>
    /// 巡回開始地点から符号付きオフセットで決まる折り返し地点までを常時描画する。
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
    /// 巡回折り返し地点の符号付きオフセットをSceneビューから編集する。
    /// </summary>
    private static void DrawPatrolOffsetHandle(
        EnemyStateMachine stateMachine,
        ChargeEnemySearchStrategy chargeEnemySearchStrategy,
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
                $"Patrol Offset: " +
                $"{chargeEnemySearchStrategy.PatrolOffset:0.##}");

            return;
        }

        float newOffset =
            Vector3.Dot(
                movedPosition -
                startPosition,
                patrolDirection);

        if (CHARGE_PATROL_OFFSET_FIELD == null)
        {
            return;
        }

        Undo.RecordObject(
            stateMachine,
            "Change Patrol Offset");

        CHARGE_PATROL_OFFSET_FIELD.SetValue(
            chargeEnemySearchStrategy,
            newOffset);

        // SerializedObjectを使わず直接変更しているため、
        // Prefab Instanceでは明示的にOverrideを記録する。
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            stateMachine);

        SceneView.RepaintAll();
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
        // GenericMenuを開いている間にPrefabが閉じられた場合、
        // callback実行時にはtargetが破棄済みになり得る。
        if (!TryGetStateMachineTarget(
                out EnemyStateMachine stateMachine))
        {
            return;
        }

        Undo.RecordObject(
            stateMachine,
            "Change Enemy Strategy");

        serializedObject.Update();

        SerializedProperty property =
            serializedObject.FindProperty(
                propertyPath);

        if (property == null)
        {
            return;
        }

        property.managedReferenceValue =
            strategyType == null
                ? null
                : Activator.CreateInstance(
                    strategyType);

        serializedObject.ApplyModifiedProperties();

        if (propertyPath ==
                SEARCH_STRATEGY_PROPERTY_NAME &&
            strategyType ==
                typeof(OldDroneEnemySearchStrategy))
        {
            EnsureOldDronePatrolPoints(
                stateMachine);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(
            stateMachine);

        EditorUtility.SetDirty(
            stateMachine);
    }

    /// <summary>
    /// 旧型Drone SearchのAuthoring用PointをEnemy直下へ用意し、
    /// SerializeReference内のStrategyへ参照を設定する。
    /// 既存Pointは再利用し、Strategyを選び直しても増殖させない。
    /// </summary>
    private void EnsureOldDronePatrolPoints(
        EnemyStateMachine stateMachine)
    {
        if (stateMachine == null)
        {
            return;
        }

        int undoGroup =
            Undo.GetCurrentGroup();

        Undo.SetCurrentGroupName(
            "Configure Old Drone Patrol Points");

        Transform enemyTransform =
            stateMachine.transform;

        float initialOffset =
            GetOldDroneInitialPatrolOffset(
                stateMachine);

        Transform pointA =
            FindOrCreateOldDronePatrolPoint(
                enemyTransform,
                OLD_DRONE_PATROL_POINT_A_NAME,
                -initialOffset);

        Transform pointB =
            FindOrCreateOldDronePatrolPoint(
                enemyTransform,
                OLD_DRONE_PATROL_POINT_B_NAME,
                initialOffset);

        serializedObject.Update();

        SerializedProperty oldDroneStrategyProperty =
            serializedObject.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        if (oldDroneStrategyProperty?.managedReferenceValue
            is not OldDroneEnemySearchStrategy)
        {
            Undo.CollapseUndoOperations(
                undoGroup);

            return;
        }

        SerializedProperty pointAProperty =
            oldDroneStrategyProperty.FindPropertyRelative(
                OLD_DRONE_PATROL_POINT_A_PROPERTY_NAME);

        SerializedProperty pointBProperty =
            oldDroneStrategyProperty.FindPropertyRelative(
                OLD_DRONE_PATROL_POINT_B_PROPERTY_NAME);

        if (pointAProperty == null ||
            pointBProperty == null)
        {
            Debug.LogError(
                $"{nameof(EnemyStateMachineEditor)}: " +
                $"{nameof(OldDroneEnemySearchStrategy)}の" +
                "PatrolPoint参照を取得できません。",
                stateMachine);

            Undo.CollapseUndoOperations(
                undoGroup);

            return;
        }

        Undo.RecordObject(
            stateMachine,
            "Assign Old Drone Patrol Points");

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

    private static Transform FindOrCreateOldDronePatrolPoint(
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

    private void CacheSerializedProperties()
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
    }

    private void ClearSerializedProperties()
    {
        searchStrategyProperty = null;
        alertStrategyProperty = null;
        attackStrategyProperty = null;
        showPatrolDebugProperty = null;
    }

    private bool AreSerializedPropertiesReady()
    {
        return searchStrategyProperty != null &&
               alertStrategyProperty != null &&
               attackStrategyProperty != null &&
               showPatrolDebugProperty != null;
    }

    private bool TryGetStateMachineTarget(
        out EnemyStateMachine stateMachine)
    {
        stateMachine =
            target as EnemyStateMachine;

        return stateMachine != null;
    }

    private static bool TryGetChargeEnemySearchStrategy(
        EnemyStateMachine stateMachine,
        out ChargeEnemySearchStrategy chargeEnemySearchStrategy)
    {
        chargeEnemySearchStrategy = null;

        if (stateMachine == null ||
            SEARCH_STRATEGY_FIELD == null)
        {
            return false;
        }

        chargeEnemySearchStrategy =
            SEARCH_STRATEGY_FIELD.GetValue(
                stateMachine) as ChargeEnemySearchStrategy;

        return chargeEnemySearchStrategy != null;
    }

    private static bool TryGetShowPatrolDebugVisualization(
        EnemyStateMachine stateMachine,
        out bool showPatrolDebugVisualization)
    {
        showPatrolDebugVisualization = false;

        if (stateMachine == null ||
            SHOW_PATROL_DEBUG_FIELD == null)
        {
            return false;
        }

        object fieldValue =
            SHOW_PATROL_DEBUG_FIELD.GetValue(
                stateMachine);

        if (fieldValue is not bool value)
        {
            return false;
        }

        showPatrolDebugVisualization = value;
        return true;
    }

    private static float GetOldDroneInitialPatrolOffset(
        EnemyStateMachine stateMachine)
    {
        float detectionRadius =
            SEARCH_STRATEGY_FIELD?.GetValue(
                stateMachine)
            is OldDroneEnemySearchStrategy oldDroneSearchStrategy
                ? oldDroneSearchStrategy.DetectionRadius
                : 0f;

        return detectionRadius >
            OLD_DRONE_DETECTION_RADIUS_EPSILON
            ? detectionRadius * 0.5f
            : OLD_DRONE_DEFAULT_PATROL_OFFSET_MINIMUM;
    }
}
