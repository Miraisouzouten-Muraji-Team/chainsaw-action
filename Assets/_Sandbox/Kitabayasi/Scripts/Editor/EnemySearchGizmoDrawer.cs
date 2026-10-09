using UnityEditor;
using UnityEngine;

/// <summary>
/// Search Strategyを持つEnemyの索敵範囲をSceneビューへ常時描画する。
/// </summary>
/// <remarks>
/// 索敵判定そのものは担当しない。
/// 現行の共通索敵基準であるCollider.bounds.centerと、
/// EnemyDataのDetectionRadiusを使用して表示する。
/// </remarks>
public static class EnemySearchGizmoDrawer
{
    private const string SEARCH_STRATEGY_PROPERTY_NAME =
        "searchStrategy";

    private static readonly Color searchRangeColor =
        new Color(
            1f,
            0.35f,
            0.35f,
            1f);

    [DrawGizmo(
        GizmoType.NonSelected |
        GizmoType.InSelectionHierarchy)]
    private static void DrawSearchRange(
        EnemyStateMachine stateMachine,
        GizmoType gizmoType)
    {
        if (stateMachine == null ||
            !HasSearchStrategy(stateMachine))
        {
            return;
        }

        EnemyDataReference dataReference =
            stateMachine.GetComponent<EnemyDataReference>();

        if (dataReference == null ||
            dataReference.Data == null)
        {
            return;
        }

        Collider enemyCollider =
            stateMachine.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            return;
        }

        float detectionRadius =
            dataReference.Data.DetectionRadius;

        if (detectionRadius <= 0f)
        {
            return;
        }

        Handles.color =
            searchRangeColor;

        // 現行の索敵判定はXY平面上で距離を判定しているため、
        // Z方向を法線とする円として表示する。
        Handles.DrawWireDisc(
            enemyCollider.bounds.center,
            Vector3.forward,
            detectionRadius);
    }

    private static bool HasSearchStrategy(
        EnemyStateMachine stateMachine)
    {
        SerializedObject serializedStateMachine =
            new SerializedObject(
                stateMachine);

        serializedStateMachine.Update();

        SerializedProperty searchStrategyProperty =
            serializedStateMachine.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        return searchStrategyProperty?.managedReferenceValue != null;
    }
}
