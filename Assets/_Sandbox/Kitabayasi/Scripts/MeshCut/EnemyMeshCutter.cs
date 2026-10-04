using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy死亡時に現在姿勢のSkinnedMeshをBakeし、
/// チェンソー軌跡を使って2つの静的Meshへ分割して表示する。
/// </summary>
/// <remarks>
/// HP計算、死亡判定、攻撃受信、チェンソー軌跡記録、
/// Triangle分割アルゴリズム、切断後の物理演出は担当しない。
/// </remarks>
public sealed class EnemyMeshCutter : MonoBehaviour
{
    [Header("切断対象")]
    [Tooltip("死亡時に現在姿勢をBakeして切断するSkinnedMeshRenderer。")]
    [SerializeField]
    private SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip("切断面専用SubMeshへ設定するMaterial。")]
    [SerializeField]
    private Material cutSurfaceMaterial;

    [Header("切断Plane設定")]
    [Tooltip("命中直前から代表刃方向・代表移動方向の算出に使用するサンプル数。")]
    [Min(2)]
    [SerializeField]
    private int cutDirectionSampleCount = 4;

    [Tooltip("Plane Normal方向のMesh厚みに対して、両端から切断禁止にする割合。")]
    [Range(0.0f, 0.49f)]
    [SerializeField]
    private float safeCutEdgeMarginNormalized = 0.2f;

    [Tooltip("切断Plane上へ生成する断面UVのスケール。")]
    [Min(0.0001f)]
    [SerializeField]
    private float cutSurfaceUvScale = 1.0f;

    [Header("Sceneデバッグ表示")]
    [SerializeField]
    private bool showCutDebugGizmos = true;

    [SerializeField]
    private bool showSafeCutRange = true;

    [SerializeField]
    private bool showCutPlanes = true;

    [SerializeField]
    private bool showCutMeshBounds = true;

    private readonly ChainsawMeshCutProcessor cutProcessor =
        new ChainsawMeshCutProcessor();

    private Mesh firstPieceRuntimeMesh;
    private Mesh secondPieceRuntimeMesh;
    private GameObject firstPieceObject;
    private GameObject secondPieceObject;
    private bool hasCompletedCut;

    private bool hasCutDebugData;
    private Plane debugOriginalCutPlane;
    private Plane debugCorrectedCutPlane;
    private float debugSafeMinimumProjection;
    private float debugSafeMaximumProjection;
    private Bounds debugSourceBounds;
    private Matrix4x4 debugLocalToWorldMatrix;

    /// <summary>
    /// 確定済みチェンソー軌跡を使ってEnemy Meshの切断を試みる。
    /// </summary>
    /// <param name="trajectory">
    /// 死亡させたチェンソー攻撃が保持している確定済み軌跡。
    /// </param>
    /// <returns>
    /// 2つのMesh生成と表示切り替えまで成功した場合はtrue。
    /// </returns>
    public bool TryCut(ChainsawAttackTrajectory trajectory)
    {
        if (hasCompletedCut)
        {
            Debug.LogWarning(
                $"{nameof(EnemyMeshCutter)}: " +
                "このEnemyは既にMesh分割済みです。",
                this);
            return false;
        }

        if (!ValidateCutRequest(trajectory, out string failureReason))
        {
            LogCutFailure(failureReason);
            return false;
        }

        var bakedMesh = new Mesh
        {
            name = $"{targetSkinnedMeshRenderer.name}_DeathBake"
        };

        // 現在のSkinnedMesh姿勢を、Renderer Transform基準の静的Meshへ保存する。
        // useScale=trueでRenderer TransformのScaleを補償したMeshを取得する。
        targetSkinnedMeshRenderer.BakeMesh(bakedMesh, true);
        bakedMesh.RecalculateBounds();

        if (!ValidateBakedMeshMaterialLayout(
                bakedMesh,
                out failureReason))
        {
            DestroyRuntimeMesh(bakedMesh);
            LogCutFailure(failureReason);
            return false;
        }

        debugLocalToWorldMatrix =
            targetSkinnedMeshRenderer.transform.localToWorldMatrix;

        ConvertTrajectoryToRendererLocalSpace(
            trajectory,
            out List<Vector3> localTipPositions,
            out List<Vector3> localRootPositions);

        bool succeeded = cutProcessor.TryCut(
            bakedMesh,
            localTipPositions,
            localRootPositions,
            cutDirectionSampleCount,
            safeCutEdgeMarginNormalized,
            cutSurfaceUvScale,
            out MeshCutResult cutResult,
            out failureReason);

        CaptureDebugData();
        DestroyRuntimeMesh(bakedMesh);

        if (!succeeded)
        {
            LogCutFailure(failureReason);
            return false;
        }

        Material[] pieceMaterials = BuildPieceMaterials();

        firstPieceRuntimeMesh = cutResult.FirstPieceMesh;
        secondPieceRuntimeMesh = cutResult.SecondPieceMesh;

        firstPieceObject = CreatePieceObject(
            "FirstPiece",
            firstPieceRuntimeMesh,
            pieceMaterials);
        secondPieceObject = CreatePieceObject(
            "SecondPiece",
            secondPieceRuntimeMesh,
            pieceMaterials);

        targetSkinnedMeshRenderer.enabled = false;
        hasCompletedCut = true;
        return true;
    }

    private bool ValidateCutRequest(
        ChainsawAttackTrajectory trajectory,
        out string failureReason)
    {
        if (targetSkinnedMeshRenderer == null)
        {
            failureReason =
                "Target Skinned Mesh Rendererが設定されていません。";
            return false;
        }

        if (targetSkinnedMeshRenderer.sharedMesh == null)
        {
            failureReason =
                "Target Skinned Mesh RendererにsharedMeshがありません。";
            return false;
        }

        if (cutSurfaceMaterial == null)
        {
            failureReason = "Cut Surface Materialが設定されていません。";
            return false;
        }

        if (trajectory == null)
        {
            failureReason = "ChainsawAttackTrajectoryがnullです。";
            return false;
        }

        if (trajectory.SampleCount < 2)
        {
            failureReason =
                "Mesh切断には2サンプル以上のチェンソー軌跡が必要です。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private bool ValidateBakedMeshMaterialLayout(
        Mesh bakedMesh,
        out string failureReason)
    {
        Material[] sourceMaterials =
            targetSkinnedMeshRenderer.sharedMaterials;

        if (sourceMaterials == null || sourceMaterials.Length == 0)
        {
            failureReason = "切断対象RendererにMaterialがありません。";
            return false;
        }

        if (sourceMaterials.Length != bakedMesh.subMeshCount)
        {
            failureReason =
                "元Material数とBake MeshのSubMesh数が一致していません。" +
                "切断後のMaterial対応を安全に維持できないため処理を中止します。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private void ConvertTrajectoryToRendererLocalSpace(
        ChainsawAttackTrajectory trajectory,
        out List<Vector3> localTipPositions,
        out List<Vector3> localRootPositions)
    {
        int sampleCount = trajectory.SampleCount;
        localTipPositions = new List<Vector3>(sampleCount);
        localRootPositions = new List<Vector3>(sampleCount);

        Transform rendererTransform = targetSkinnedMeshRenderer.transform;

        for (int i = 0; i < sampleCount; i++)
        {
            ChainsawBladeTrajectorySample sample = trajectory.Samples[i];

            localTipPositions.Add(
                rendererTransform.InverseTransformPoint(sample.TipPosition));
            localRootPositions.Add(
                rendererTransform.InverseTransformPoint(sample.RootPosition));
        }
    }

    private void CaptureDebugData()
    {
        hasCutDebugData = cutProcessor.HasDebugData;

        if (!hasCutDebugData)
        {
            return;
        }

        debugOriginalCutPlane = cutProcessor.LastOriginalCutPlane;
        debugCorrectedCutPlane = cutProcessor.LastCorrectedCutPlane;
        debugSafeMinimumProjection =
            cutProcessor.LastSafeMinimumProjection;
        debugSafeMaximumProjection =
            cutProcessor.LastSafeMaximumProjection;
        debugSourceBounds = cutProcessor.LastSourceBounds;
    }

    private Material[] BuildPieceMaterials()
    {
        Material[] sourceMaterials =
            targetSkinnedMeshRenderer.sharedMaterials;
        var pieceMaterials = new Material[sourceMaterials.Length + 1];

        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            pieceMaterials[i] = sourceMaterials[i];
        }

        pieceMaterials[pieceMaterials.Length - 1] = cutSurfaceMaterial;
        return pieceMaterials;
    }

    private GameObject CreatePieceObject(
        string suffix,
        Mesh mesh,
        Material[] materials)
    {
        var pieceObject = new GameObject(
            $"{targetSkinnedMeshRenderer.name}_Cut_{suffix}");

        pieceObject.layer = targetSkinnedMeshRenderer.gameObject.layer;

        Transform pieceTransform = pieceObject.transform;
        pieceTransform.SetParent(
            targetSkinnedMeshRenderer.transform,
            false);
        pieceTransform.localPosition = Vector3.zero;
        pieceTransform.localRotation = Quaternion.identity;
        pieceTransform.localScale = Vector3.one;

        MeshFilter meshFilter = pieceObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = pieceObject.AddComponent<MeshRenderer>();
        CopyRendererSettings(
            targetSkinnedMeshRenderer,
            meshRenderer);
        meshRenderer.sharedMaterials = materials;

        return pieceObject;
    }

    private static void CopyRendererSettings(
        Renderer source,
        MeshRenderer destination)
    {
        destination.shadowCastingMode = source.shadowCastingMode;
        destination.receiveShadows = source.receiveShadows;
        destination.lightProbeUsage = source.lightProbeUsage;
        destination.reflectionProbeUsage = source.reflectionProbeUsage;
        destination.probeAnchor = source.probeAnchor;
        destination.sortingLayerID = source.sortingLayerID;
        destination.sortingOrder = source.sortingOrder;
    }

    private void LogCutFailure(string failureReason)
    {
        Debug.LogWarning(
            $"{nameof(EnemyMeshCutter)}: Mesh分割に失敗しました。" +
            $" 理由: {failureReason}",
            this);
    }

    private void OnDestroy()
    {
        DestroyRuntimeMesh(firstPieceRuntimeMesh);
        DestroyRuntimeMesh(secondPieceRuntimeMesh);

        firstPieceRuntimeMesh = null;
        secondPieceRuntimeMesh = null;
    }

    private static void DestroyRuntimeMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        cutDirectionSampleCount = Mathf.Max(2, cutDirectionSampleCount);
        safeCutEdgeMarginNormalized = Mathf.Clamp(
            safeCutEdgeMarginNormalized,
            0.0f,
            0.49f);
        cutSurfaceUvScale = Mathf.Max(
            0.0001f,
            cutSurfaceUvScale);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!showCutDebugGizmos || !hasCutDebugData)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        if (showSafeCutRange)
        {
            DrawSafeCutRange();
        }

        Gizmos.matrix = debugLocalToWorldMatrix;

        if (showCutMeshBounds)
        {
            Gizmos.color = new Color(0.0f, 0.85f, 1.0f, 1.0f);
            Gizmos.DrawWireCube(
                debugSourceBounds.center,
                debugSourceBounds.size);
        }

        if (showCutPlanes)
        {
            DrawPlaneWire(
                debugOriginalCutPlane,
                debugSourceBounds,
                new Color(1.0f, 0.85f, 0.0f, 1.0f));
            DrawPlaneWire(
                debugCorrectedCutPlane,
                debugSourceBounds,
                new Color(0.0f, 1.0f, 0.25f, 1.0f));
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private void DrawSafeCutRange()
    {
        Vector3 normal = debugCorrectedCutPlane.normal.normalized;
        CreatePlaneBasis(
            normal,
            out Vector3 axisU,
            out Vector3 axisV);

        CalculateBoundsProjectionRange(
            debugSourceBounds,
            axisU,
            out float minimumU,
            out float maximumU);
        CalculateBoundsProjectionRange(
            debugSourceBounds,
            axisV,
            out float minimumV,
            out float maximumV);

        float centerU = (minimumU + maximumU) * 0.5f;
        float centerV = (minimumV + maximumV) * 0.5f;
        float centerNormal =
            (debugSafeMinimumProjection +
             debugSafeMaximumProjection) * 0.5f;

        Vector3 localCenter =
            axisU * centerU +
            axisV * centerV +
            normal * centerNormal;

        float sizeU = maximumU - minimumU;
        float sizeV = maximumV - minimumV;
        float sizeNormal =
            debugSafeMaximumProjection -
            debugSafeMinimumProjection;

        Quaternion localRotation = Quaternion.LookRotation(
            normal,
            axisV);

        Gizmos.matrix =
            debugLocalToWorldMatrix *
            Matrix4x4.TRS(
                localCenter,
                localRotation,
                Vector3.one);

        Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.14f);
        Gizmos.DrawCube(
            Vector3.zero,
            new Vector3(sizeU, sizeV, sizeNormal));

        Gizmos.matrix = debugLocalToWorldMatrix;

        DrawProjectionPlaneWire(
            normal,
            axisU,
            axisV,
            debugSafeMinimumProjection,
            minimumU,
            maximumU,
            minimumV,
            maximumV,
            new Color(1.0f, 0.0f, 0.0f, 1.0f));

        DrawProjectionPlaneWire(
            normal,
            axisU,
            axisV,
            debugSafeMaximumProjection,
            minimumU,
            maximumU,
            minimumV,
            maximumV,
            new Color(1.0f, 0.0f, 0.0f, 1.0f));
    }

    private static void DrawPlaneWire(
        Plane plane,
        Bounds bounds,
        Color color)
    {
        Vector3 normal = plane.normal.normalized;
        CreatePlaneBasis(
            normal,
            out Vector3 axisU,
            out Vector3 axisV);

        CalculateBoundsProjectionRange(
            bounds,
            axisU,
            out float minimumU,
            out float maximumU);
        CalculateBoundsProjectionRange(
            bounds,
            axisV,
            out float minimumV,
            out float maximumV);

        float planeProjection = -plane.distance;

        DrawProjectionPlaneWire(
            normal,
            axisU,
            axisV,
            planeProjection,
            minimumU,
            maximumU,
            minimumV,
            maximumV,
            color);
    }

    private static void DrawProjectionPlaneWire(
        Vector3 normal,
        Vector3 axisU,
        Vector3 axisV,
        float planeProjection,
        float minimumU,
        float maximumU,
        float minimumV,
        float maximumV,
        Color color)
    {
        Vector3 first =
            axisU * minimumU +
            axisV * minimumV +
            normal * planeProjection;

        Vector3 second =
            axisU * maximumU +
            axisV * minimumV +
            normal * planeProjection;

        Vector3 third =
            axisU * maximumU +
            axisV * maximumV +
            normal * planeProjection;

        Vector3 fourth =
            axisU * minimumU +
            axisV * maximumV +
            normal * planeProjection;

        Gizmos.color = color;
        Gizmos.DrawLine(first, second);
        Gizmos.DrawLine(second, third);
        Gizmos.DrawLine(third, fourth);
        Gizmos.DrawLine(fourth, first);
    }

    private static void CreatePlaneBasis(
        Vector3 planeNormal,
        out Vector3 axisU,
        out Vector3 axisV)
    {
        Vector3 referenceAxis =
            Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right;

        axisU = Vector3.Cross(
            referenceAxis,
            planeNormal).normalized;

        if (axisU.sqrMagnitude <= 0.00000001f)
        {
            axisU = Vector3.Cross(
                Vector3.forward,
                planeNormal).normalized;
        }

        axisV = Vector3.Cross(
            planeNormal,
            axisU).normalized;
    }

    private static void CalculateBoundsProjectionRange(
        Bounds bounds,
        Vector3 axis,
        out float minimumProjection,
        out float maximumProjection)
    {
        Vector3[] corners = GetBoundsCorners(bounds);
        minimumProjection = float.PositiveInfinity;
        maximumProjection = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            float projection = Vector3.Dot(corners[i], axis);
            minimumProjection = Mathf.Min(
                minimumProjection,
                projection);
            maximumProjection = Mathf.Max(
                maximumProjection,
                projection);
        }
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };
    }
}
