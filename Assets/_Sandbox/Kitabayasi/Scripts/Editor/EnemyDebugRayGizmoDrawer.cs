using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Enemyの各処理が提供する実Ray情報をSceneビューへ描画する。
/// </summary>
/// <remarks>
/// Rayの始点・方向・距離は再計算せず、
/// SerializeReferenceで保持されているStrategyから
/// IEnemyDebugRayProviderの実体を取得して描画する。
/// </remarks>
public static class EnemyDebugRayGizmoDrawer
{
    private const string SEARCH_STRATEGY_PROPERTY_NAME =
        "searchStrategy";

    private const string ATTACK_STRATEGY_PROPERTY_NAME =
        "attackStrategy";

    private static readonly Color lineOfSightSuccessColor =
        Color.green;

    private static readonly Color lineOfSightBlockedColor =
        Color.red;

    private static readonly Color lineOfSightUnknownColor =
        Color.cyan;

    private static readonly Color groundCheckRayColor =
        new Color(
            1f,
            0.9f,
            0.2f,
            1f);

    private static readonly List<EnemyDebugRay> debugRayBuffer =
        new List<EnemyDebugRay>(4);

    [DrawGizmo(
        GizmoType.NonSelected |
        GizmoType.InSelectionHierarchy)]
    private static void DrawDebugRays(
        EnemyStateMachine stateMachine,
        GizmoType gizmoType)
    {
        if (!EditorApplication.isPlaying ||
            stateMachine == null)
        {
            return;
        }

        debugRayBuffer.Clear();

        SerializedObject serializedStateMachine =
            new SerializedObject(
                stateMachine);

        serializedStateMachine.Update();

        CollectDebugRaysFromStrategy(
            serializedStateMachine.FindProperty(
                SEARCH_STRATEGY_PROPERTY_NAME));

        CollectDebugRaysFromStrategy(
            serializedStateMachine.FindProperty(
                ATTACK_STRATEGY_PROPERTY_NAME));

        if (debugRayBuffer.Count == 0)
        {
            return;
        }

        for (int i = 0;
             i < debugRayBuffer.Count;
             i++)
        {
            EnemyDebugRay debugRay =
                debugRayBuffer[i];

            Gizmos.color =
                GetDebugRayColor(
                    debugRay);

            Vector3 endPosition =
                debugRay.Origin +
                debugRay.Direction *
                debugRay.Distance;

            Gizmos.DrawLine(
                debugRay.Origin,
                endPosition);
        }
    }

    private static void CollectDebugRaysFromStrategy(
        SerializedProperty strategyProperty)
    {
        if (strategyProperty?.managedReferenceValue
            is not IEnemyDebugRayProvider debugRayProvider)
        {
            return;
        }

        debugRayProvider.CollectDebugRays(
            debugRayBuffer);
    }

    private static Color GetDebugRayColor(
        EnemyDebugRay debugRay)
    {
        if (debugRay.Kind ==
            EnemyDebugRayKind.LineOfSight)
        {
            return debugRay.Result switch
            {
                EnemyDebugRayResult.Success =>
                    lineOfSightSuccessColor,

                EnemyDebugRayResult.Blocked =>
                    lineOfSightBlockedColor,

                _ =>
                    lineOfSightUnknownColor
            };
        }

        if (debugRay.Kind ==
            EnemyDebugRayKind.GroundCheck)
        {
            return groundCheckRayColor;
        }

        return Color.magenta;
    }
}
