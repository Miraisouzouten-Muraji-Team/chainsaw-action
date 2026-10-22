using UnityEditor;
using UnityEngine;

/// <summary>
/// Search Strategyを持つEnemyの索敵範囲をSceneビューへ常時描画する。
/// </summary>
/// <remarks>
/// 索敵判定そのものは担当しない。
/// ChargeEnemyと旧型Droneは個体のSearch Strategyに設定された索敵半径を使用し、
/// 実処理と同じSearchCenterへ索敵範囲を表示する。
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
        if (stateMachine == null)
        {
            return;
        }

        object searchStrategy =
            GetSearchStrategy(
                stateMachine);

        if (searchStrategy == null)
        {
            return;
        }

        float detectionRadius;

        if (searchStrategy is
                ChargeEnemySearchStrategy chargeEnemySearchStrategy)
        {
            detectionRadius =
                chargeEnemySearchStrategy.DetectionRadius;
        }
        else if (searchStrategy is
                     OldDroneEnemySearchStrategy oldDroneEnemySearchStrategy)
        {
            detectionRadius =
                oldDroneEnemySearchStrategy.DetectionRadius;
        }
        else
        {
            EnemyDataReference dataReference =
                stateMachine.GetComponent<EnemyDataReference>();

            if (dataReference == null ||
                dataReference.Data == null)
            {
                return;
            }

            detectionRadius =
                dataReference.Data.DetectionRadius;
        }

        if (detectionRadius <= 0f)
        {
            return;
        }

        if (!TryGetSearchRangeCenter(
                stateMachine,
                searchStrategy,
                out Vector3 searchRangeCenter))
        {
            return;
        }

        Handles.color =
            searchRangeColor;

        // 現行の索敵距離判定はXYゲームプレイ平面上で行うため、
        // Z方向を法線とする円として表示する。
        Handles.DrawWireDisc(
            searchRangeCenter,
            Vector3.forward,
            detectionRadius);
    }

    private static object GetSearchStrategy(
        EnemyStateMachine stateMachine)
    {
        SerializedObject serializedStateMachine =
            new SerializedObject(
                stateMachine);

        serializedStateMachine.Update();

        SerializedProperty searchStrategyProperty =
            serializedStateMachine.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME);

        return
            searchStrategyProperty?
                .managedReferenceValue;
    }

    private static bool TryGetSearchRangeCenter(
        EnemyStateMachine stateMachine,
        object searchStrategy,
        out Vector3 searchRangeCenter)
    {
        if (searchStrategy is
                OldDroneEnemySearchStrategy oldDroneSearchStrategy)
        {
            // Play中はStrategyがSearch開始時に固定した値を使い、
            // Droneが巡回してもGizmo中心だけ追従する不一致を防ぐ。
            if (EditorApplication.isPlaying &&
                oldDroneSearchStrategy.TryGetSearchCenter(
                    out searchRangeCenter))
            {
                return true;
            }

            // Edit時の初回SearchCenterは配置時のDrone本体位置になる。
            searchRangeCenter =
                stateMachine.transform.position;

            return true;
        }

        Collider enemyCollider =
            stateMachine.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            searchRangeCenter = default;
            return false;
        }

        // ChargeEnemy等の既存Strategyは従来どおりCollider中心を使用する。
        searchRangeCenter =
            enemyCollider.bounds.center;

        return true;
    }
}
