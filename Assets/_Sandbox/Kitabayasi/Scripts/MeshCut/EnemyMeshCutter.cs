using UnityEngine;

/// <summary>
/// Enemy死亡時に切断対象Meshを取得し、
/// 命中時に確定済みのチェンソー切断情報を使って2つの静的Meshへ分割して表示する。
/// </summary>
/// <remarks>
/// SkinnedMeshRendererの場合は死亡時の現在姿勢をBakeしてから、
/// EnemyMeshCutter自身のTransformを基準とする切断空間へ変換して切断する。
/// MeshFilter + MeshRendererの場合も同じ切断空間へ変換してから切断する。
///
/// これにより、モデル側のローカル軸・階層Rotation・Scaleに依存せず、
/// Cut Range Axisは常にEnemyMeshCutterのローカル軸として扱われる。
///
/// HP計算、死亡判定、攻撃受信、Player側のProfile解決、
/// Triangle分割アルゴリズム、切断後の物理演出は担当しない。
/// </remarks>
public sealed class EnemyMeshCutter : MonoBehaviour
{
    private const float MIN_TRANSFORM_DETERMINANT =
        0.00000001f;

    private const float MIN_DIRECTION_SQR_MAGNITUDE =
        0.00000001f;

    [Header("切断対象")]

    [Tooltip(
        "SkinnedMeshRendererを使用するEnemyの切断対象。"
        + "MeshFilterと同時には設定しない。")]
    [SerializeField]
    private SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip(
        "MeshFilter + MeshRendererを使用するEnemyの切断対象。"
        + "同じGameObjectにMeshRendererが必要。"
        + "SkinnedMeshRendererと同時には設定しない。")]
    [SerializeField]
    private MeshFilter targetMeshFilter;

    [Tooltip("切断面専用SubMeshへ設定するMaterial。")]
    [SerializeField]
    private Material cutSurfaceMaterial;

    [Header("切断Plane設定")]

    [Tooltip(
        "EnemyMeshCutterのローカル空間で、"
        + "切断位置の有効範囲を定義する軸。")]
    [SerializeField]
    private MeshCutRangeAxis cutRangeAxis = MeshCutRangeAxis.Y;

    [Tooltip("Cut Range Axis方向のMesh Boundsに対する有効範囲の下限。0がBounds最小、1がBounds最大。")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float cutRangeMinimumNormalized = 0.2f;

    [Tooltip("Cut Range Axis方向のMesh Boundsに対する有効範囲の上限。0がBounds最小、1がBounds最大。")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float cutRangeMaximumNormalized = 0.8f;

    [Tooltip("切断Plane上へ生成する断面UVのスケール。")]
    [Min(0.0001f)]
    [SerializeField]
    private float cutSurfaceUvScale = 1.0f;

    [Header("Scene表示")]

    [Tooltip("選択中のEnemyに、設定済みの切断位置有効範囲を赤く表示する。")]
    [SerializeField]
    private bool showCutRangeGizmo = true;

    private readonly ChainsawMeshCutProcessor cutProcessor =
        new ChainsawMeshCutProcessor();

    private Mesh firstPieceRuntimeMesh;
    private Mesh secondPieceRuntimeMesh;

    private GameObject firstPieceObject;
    private GameObject secondPieceObject;

    private bool hasCompletedCut;

    /// <summary>
    /// 命中時に確定済みのチェンソー切断情報を使ってEnemy Meshの切断を試みる。
    /// </summary>
    /// <param name="cutKinematics">
    /// Player側で命中時に確定したWorld空間の刃中心・刃方向・移動方向。
    /// </param>
    /// <returns>
    /// 2つのMesh生成と表示切り替えまで成功した場合はtrue。
    /// </returns>
    public bool TryCut(
        ChainsawCutKinematics cutKinematics)
    {
        if (hasCompletedCut)
        {
            Debug.LogWarning(
                $"{nameof(EnemyMeshCutter)}: "
                + "このEnemyは既にMesh分割済みです。",
                this);

            return false;
        }

        if (!ValidateCutRequest(
                cutKinematics,
                out string failureReason))
        {
            LogCutFailure(failureReason);
            return false;
        }

        if (!TryCreateCutSpaceSource(
                out Mesh sourceMesh,
                out Renderer sourceRenderer,
                out failureReason))
        {
            LogCutFailure(failureReason);
            return false;
        }

        try
        {
            if (!ValidateSourceMeshMaterialLayout(
                    sourceMesh,
                    sourceRenderer,
                    out failureReason))
            {
                LogCutFailure(failureReason);
                return false;
            }

            ConvertCutKinematicsToCutSpace(
                cutKinematics,
                out Vector3 bladeCenterLocal,
                out Vector3 bladeDirectionLocal,
                out Vector3 bladeMovementDirectionLocal);

            bool succeeded = cutProcessor.TryCut(
                sourceMesh,
                bladeCenterLocal,
                bladeDirectionLocal,
                bladeMovementDirectionLocal,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutSurfaceUvScale,
                out Mesh firstPieceMesh,
                out Mesh secondPieceMesh,
                out failureReason);

            if (!succeeded)
            {
                LogCutFailure(failureReason);
                return false;
            }

            Material[] pieceMaterials =
                BuildPieceMaterials(sourceRenderer);

            firstPieceRuntimeMesh =
                firstPieceMesh;

            secondPieceRuntimeMesh =
                secondPieceMesh;

            firstPieceObject = CreatePieceObject(
                "FirstPiece",
                firstPieceRuntimeMesh,
                pieceMaterials,
                sourceRenderer);

            secondPieceObject = CreatePieceObject(
                "SecondPiece",
                secondPieceRuntimeMesh,
                pieceMaterials,
                sourceRenderer);

            sourceRenderer.enabled = false;

            hasCompletedCut = true;

            return true;
        }
        finally
        {
            // CutSpaceへ変換した入力MeshはこのComponentが一時生成したRuntime Mesh。
            // 分割後のPiece Meshへ必要な頂点情報は複製済みなのでここで破棄する。
            DestroyRuntimeMesh(sourceMesh);
        }
    }

    /// <summary>
    /// Mesh分割によって生成された2つの切断片を取得する。
    /// </summary>
    /// <returns>
    /// Mesh分割済みで、両方の切断片が存在する場合はtrue。
    /// </returns>
    public bool TryGetCutPieceTransforms(
        out Transform firstPieceTransform,
        out Transform secondPieceTransform)
    {
        firstPieceTransform =
            firstPieceObject != null
                ? firstPieceObject.transform
                : null;

        secondPieceTransform =
            secondPieceObject != null
                ? secondPieceObject.transform
                : null;

        return hasCompletedCut &&
               firstPieceTransform != null &&
               secondPieceTransform != null;
    }

    /// <summary>
    /// 切断要求に必要な設定と入力を検証する。
    /// </summary>
    private bool ValidateCutRequest(
        ChainsawCutKinematics cutKinematics,
        out string failureReason)
    {
        bool hasSkinnedMeshTarget =
            targetSkinnedMeshRenderer != null;

        bool hasMeshFilterTarget =
            targetMeshFilter != null;

        if (!hasSkinnedMeshTarget &&
            !hasMeshFilterTarget)
        {
            failureReason =
                "切断対象が設定されていません。"
                + "Target Skinned Mesh Rendererまたは"
                + "Target Mesh Filterのどちらかを設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget &&
            hasMeshFilterTarget)
        {
            failureReason =
                "Target Skinned Mesh Rendererと"
                + "Target Mesh Filterの両方が設定されています。"
                + "切断対象はどちらか一方だけ設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget)
        {
            if (targetSkinnedMeshRenderer.sharedMesh == null)
            {
                failureReason =
                    "Target Skinned Mesh Rendererに"
                    + "sharedMeshがありません。";

                return false;
            }
        }
        else
        {
            if (targetMeshFilter.sharedMesh == null)
            {
                failureReason =
                    "Target Mesh FilterにsharedMeshがありません。";

                return false;
            }

            MeshRenderer targetMeshRenderer =
                targetMeshFilter.GetComponent<MeshRenderer>();

            if (targetMeshRenderer == null)
            {
                failureReason =
                    "Target Mesh Filterと同じGameObjectに"
                    + "MeshRendererがありません。";

                return false;
            }

            if (!targetMeshFilter.sharedMesh.isReadable)
            {
                failureReason =
                    "Target Mesh FilterのMeshが読み取り不可です。"
                    + "Mesh切断では頂点・Triangle情報を読み取るため、"
                    + "Model Import Settingsの"
                    + "Read/Write Enabledを有効にしてください。";

                return false;
            }
        }

        if (cutRangeMinimumNormalized < 0.0f ||
            cutRangeMinimumNormalized > 1.0f ||
            cutRangeMaximumNormalized < 0.0f ||
            cutRangeMaximumNormalized > 1.0f)
        {
            failureReason =
                "切断位置有効範囲は0～1の範囲で設定してください。";

            return false;
        }

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            failureReason =
                "Cut Range MinimumはCut Range Maximumより小さい値にしてください。";

            return false;
        }

        if (cutSurfaceMaterial == null)
        {
            failureReason =
                "Cut Surface Materialが設定されていません。";

            return false;
        }

        if (!cutKinematics.HasValidDirections)
        {
            failureReason =
                "ChainsawCutKinematicsに有効な"
                + "刃方向または移動方向がありません。";

            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 設定されたRendererの見た目をRuntime Meshへ固定し、
    /// EnemyMeshCutterのローカル空間へ変換した切断入力Meshを生成する。
    /// </summary>
    /// <remarks>
    /// SkinnedMeshRendererではBakeMeshによって死亡時点のスキニング結果を取得する。
    /// Bake結果はRendererローカル空間なので、そのまま切断には使わず、
    /// MeshFilterの場合と同様にEnemyMeshCutter基準の空間へ統一する。
    /// </remarks>
    private bool TryCreateCutSpaceSource(
        out Mesh sourceMesh,
        out Renderer sourceRenderer,
        out string failureReason)
    {
        sourceMesh = null;
        sourceRenderer = null;

        if (targetSkinnedMeshRenderer != null)
        {
            var bakedMesh = new Mesh
            {
                name =
                    $"{targetSkinnedMeshRenderer.name}_DeathBake"
            };

            // useScale=trueではSkinnedMeshRendererのScaleを補償した
            // Rendererローカル座標のMeshが得られる。
            // この後localToWorldMatrixを使ってCutSpaceへ変換するため、
            // Transform Scaleを頂点へ二重適用しないことが重要。
            targetSkinnedMeshRenderer.BakeMesh(
                bakedMesh,
                true);

            if (!TryTransformMeshToCutSpace(
                    bakedMesh,
                    targetSkinnedMeshRenderer.transform,
                    out failureReason))
            {
                DestroyRuntimeMesh(bakedMesh);
                return false;
            }

            sourceMesh = bakedMesh;
            sourceRenderer = targetSkinnedMeshRenderer;

            failureReason = null;
            return true;
        }

        if (targetMeshFilter != null)
        {
            MeshRenderer meshRenderer =
                targetMeshFilter.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
            {
                failureReason =
                    "Target Mesh Filterと同じGameObjectに"
                    + "MeshRendererがありません。";

                return false;
            }

            Mesh sharedMesh =
                targetMeshFilter.sharedMesh;

            if (sharedMesh == null)
            {
                failureReason =
                    "Target Mesh FilterにsharedMeshがありません。";

                return false;
            }

            var runtimeMesh =
                Instantiate(sharedMesh);

            runtimeMesh.name =
                $"{sharedMesh.name}_CutSource";

            if (!TryTransformMeshToCutSpace(
                    runtimeMesh,
                    targetMeshFilter.transform,
                    out failureReason))
            {
                DestroyRuntimeMesh(runtimeMesh);
                return false;
            }

            sourceMesh = runtimeMesh;
            sourceRenderer = meshRenderer;

            failureReason = null;
            return true;
        }

        failureReason =
            "切断対象からMeshを取得できませんでした。";

        return false;
    }

    /// <summary>
    /// RendererローカルMeshをEnemyMeshCutterローカル空間へ変換する。
    /// </summary>
    /// <remarks>
    /// PositionだけでなくNormal / Tangentも同じ空間へ揃える。
    /// 負Scaleを含むTransformで座標系のHandednessが反転する場合は、
    /// 表裏が逆転しないようTriangle windingとTangent handednessも反転する。
    /// </remarks>
    private bool TryTransformMeshToCutSpace(
        Mesh mesh,
        Transform sourceTransform,
        out string failureReason)
    {
        if (mesh == null)
        {
            failureReason =
                "CutSpaceへ変換するMeshがnullです。";

            return false;
        }

        if (sourceTransform == null)
        {
            failureReason =
                "切断対象RendererのTransformがnullです。";

            return false;
        }

        Matrix4x4 cutSpaceFromSourceLocal =
            transform.worldToLocalMatrix *
            sourceTransform.localToWorldMatrix;

        float determinant =
            cutSpaceFromSourceLocal.determinant;

        if (Mathf.Abs(determinant) <=
            MIN_TRANSFORM_DETERMINANT)
        {
            failureReason =
                "切断対象またはEnemyMeshCutterのTransformに"
                + "0に近いScaleが含まれているため、"
                + "安全にMesh座標を変換できません。";

            return false;
        }

        Vector3[] vertices =
            mesh.vertices;

        for (int i = 0;
             i < vertices.Length;
             i++)
        {
            vertices[i] =
                cutSpaceFromSourceLocal.MultiplyPoint3x4(
                    vertices[i]);
        }

        mesh.vertices =
            vertices;

        Vector3[] normals =
            mesh.normals;

        bool hasNormals =
            normals != null &&
            normals.Length == vertices.Length;

        Matrix4x4 normalMatrix =
            cutSpaceFromSourceLocal.inverse.transpose;

        if (hasNormals)
        {
            for (int i = 0;
                 i < normals.Length;
                 i++)
            {
                Vector3 transformedNormal =
                    normalMatrix.MultiplyVector(
                        normals[i]);

                if (transformedNormal.sqrMagnitude >
                    MIN_DIRECTION_SQR_MAGNITUDE)
                {
                    transformedNormal.Normalize();
                }

                normals[i] =
                    transformedNormal;
            }

            mesh.normals =
                normals;
        }

        Vector4[] tangents =
            mesh.tangents;

        bool hasTangents =
            tangents != null &&
            tangents.Length == vertices.Length;

        bool reversesHandedness =
            determinant < 0.0f;

        if (hasTangents)
        {
            for (int i = 0;
                 i < tangents.Length;
                 i++)
            {
                Vector4 sourceTangent =
                    tangents[i];

                Vector3 tangentDirection =
                    cutSpaceFromSourceLocal.MultiplyVector(
                        new Vector3(
                            sourceTangent.x,
                            sourceTangent.y,
                            sourceTangent.z));

                if (hasNormals)
                {
                    // 非一様Scaleを含む変換後もNormalと直交するよう、
                    // Tangentを変換済みNormalへ再直交化する。
                    tangentDirection -=
                        normals[i] *
                        Vector3.Dot(
                            normals[i],
                            tangentDirection);
                }

                if (tangentDirection.sqrMagnitude >
                    MIN_DIRECTION_SQR_MAGNITUDE)
                {
                    tangentDirection.Normalize();
                }

                tangents[i] =
                    new Vector4(
                        tangentDirection.x,
                        tangentDirection.y,
                        tangentDirection.z,
                        reversesHandedness
                            ? -sourceTangent.w
                            : sourceTangent.w);
            }

            mesh.tangents =
                tangents;
        }

        if (reversesHandedness)
        {
            ReverseTriangleWinding(mesh);
        }

        mesh.RecalculateBounds();

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 負Scaleによる座標系反転後も表面の向きを維持するため、
    /// Triangle topologyのSubMeshだけ頂点順を反転する。
    /// </summary>
    private static void ReverseTriangleWinding(
        Mesh mesh)
    {
        for (int subMeshIndex = 0;
             subMeshIndex < mesh.subMeshCount;
             subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) !=
                MeshTopology.Triangles)
            {
                continue;
            }

            int[] triangles =
                mesh.GetTriangles(subMeshIndex);

            for (int triangleIndex = 0;
                 triangleIndex + 2 < triangles.Length;
                 triangleIndex += 3)
            {
                int temporary =
                    triangles[triangleIndex + 1];

                triangles[triangleIndex + 1] =
                    triangles[triangleIndex + 2];

                triangles[triangleIndex + 2] =
                    temporary;
            }

            mesh.SetTriangles(
                triangles,
                subMeshIndex,
                false);
        }
    }

    /// <summary>
    /// 元MeshのSubMesh数とRendererのMaterial数が一致しているか確認する。
    /// </summary>
    private bool ValidateSourceMeshMaterialLayout(
        Mesh sourceMesh,
        Renderer sourceRenderer,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason =
                "切断対象Meshがnullです。";

            return false;
        }

        if (sourceRenderer == null)
        {
            failureReason =
                "切断対象Rendererがnullです。";

            return false;
        }

        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        if (sourceMaterials == null ||
            sourceMaterials.Length == 0)
        {
            failureReason =
                "切断対象RendererにMaterialがありません。";

            return false;
        }

        if (sourceMaterials.Length !=
            sourceMesh.subMeshCount)
        {
            failureReason =
                "元Material数とMeshのSubMesh数が"
                + "一致していません。"
                + "切断後のMaterial対応を安全に"
                + "維持できないため処理を中止します。";

            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// Player側で確定したWorld空間のCut情報を、
    /// EnemyMeshCutterのローカル切断空間へ変換する。
    /// </summary>
    /// <remarks>
    /// 刃中心は位置なのでInverseTransformPoint、
    /// 刃方向と移動方向はベクトルなのでInverseTransformVectorを使用する。
    /// Mesh自体も同じCut Spaceへ変換済みのため、Plane生成時に空間を混在させない。
    /// </remarks>
    private void ConvertCutKinematicsToCutSpace(
        ChainsawCutKinematics cutKinematics,
        out Vector3 bladeCenterLocal,
        out Vector3 bladeDirectionLocal,
        out Vector3 bladeMovementDirectionLocal)
    {
        bladeCenterLocal =
            transform.InverseTransformPoint(
                cutKinematics.BladeCenterWorld);

        bladeDirectionLocal =
            transform.InverseTransformVector(
                cutKinematics.BladeDirectionWorld);

        bladeMovementDirectionLocal =
            transform.InverseTransformVector(
                cutKinematics.BladeMovementDirectionWorld);
    }

    /// <summary>
    /// 元RendererのMaterialに切断面Materialを追加する。
    /// </summary>
    private Material[] BuildPieceMaterials(
        Renderer sourceRenderer)
    {
        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        var pieceMaterials =
            new Material[sourceMaterials.Length + 1];

        for (int i = 0;
             i < sourceMaterials.Length;
             i++)
        {
            pieceMaterials[i] =
                sourceMaterials[i];
        }

        pieceMaterials[pieceMaterials.Length - 1] =
            cutSurfaceMaterial;

        return pieceMaterials;
    }

    /// <summary>
    /// 切断後Meshを表示するGameObjectを生成する。
    /// </summary>
    private GameObject CreatePieceObject(
        string suffix,
        Mesh mesh,
        Material[] materials,
        Renderer sourceRenderer)
    {
        var pieceObject =
            new GameObject(
                $"{sourceRenderer.name}_Cut_{suffix}");

        pieceObject.layer =
            sourceRenderer.gameObject.layer;

        Transform pieceTransform =
            pieceObject.transform;

        // Piece Meshの頂点はEnemyMeshCutterローカルへ変換済みなので、
        // 元Renderer配下ではなくCutSpaceそのものへ配置する。
        pieceTransform.SetParent(
            transform,
            false);

        pieceTransform.localPosition =
            Vector3.zero;

        pieceTransform.localRotation =
            Quaternion.identity;

        pieceTransform.localScale =
            Vector3.one;

        MeshFilter meshFilter =
            pieceObject.AddComponent<MeshFilter>();

        meshFilter.sharedMesh =
            mesh;

        MeshRenderer meshRenderer =
            pieceObject.AddComponent<MeshRenderer>();

        CopyRendererSettings(
            sourceRenderer,
            meshRenderer);

        meshRenderer.sharedMaterials =
            materials;

        return pieceObject;
    }

    /// <summary>
    /// 元Rendererから切断後Rendererへ、
    /// 表示に必要な共通設定をコピーする。
    /// </summary>
    private static void CopyRendererSettings(
        Renderer source,
        MeshRenderer destination)
    {
        destination.shadowCastingMode =
            source.shadowCastingMode;

        destination.receiveShadows =
            source.receiveShadows;

        destination.lightProbeUsage =
            source.lightProbeUsage;

        destination.reflectionProbeUsage =
            source.reflectionProbeUsage;

        destination.probeAnchor =
            source.probeAnchor;

        destination.sortingLayerID =
            source.sortingLayerID;

        destination.sortingOrder =
            source.sortingOrder;
    }

    /// <summary>
    /// Mesh切断失敗理由をConsoleへ出力する。
    /// </summary>
    private void LogCutFailure(
        string failureReason)
    {
        Debug.LogWarning(
            $"{nameof(EnemyMeshCutter)}: "
            + "Mesh分割に失敗しました。"
            + $" 理由: {failureReason}",
            this);
    }

    private void OnDestroy()
    {
        DestroyRuntimeMesh(
            firstPieceRuntimeMesh);

        DestroyRuntimeMesh(
            secondPieceRuntimeMesh);

        firstPieceRuntimeMesh = null;
        secondPieceRuntimeMesh = null;
    }

    /// <summary>
    /// EnemyMeshCutterが所有するRuntime Meshを破棄する。
    /// </summary>
    private static void DestroyRuntimeMesh(
        Mesh mesh)
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
        cutRangeMinimumNormalized =
            Mathf.Clamp01(
                cutRangeMinimumNormalized);

        cutRangeMaximumNormalized =
            Mathf.Clamp01(
                cutRangeMaximumNormalized);

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            const float minimumNormalizedRangeWidth = 0.01f;

            if (cutRangeMinimumNormalized >= 1.0f)
            {
                cutRangeMinimumNormalized =
                    1.0f - minimumNormalizedRangeWidth;
            }

            cutRangeMaximumNormalized =
                Mathf.Min(
                    1.0f,
                    cutRangeMinimumNormalized
                    + minimumNormalizedRangeWidth);
        }

        cutSurfaceUvScale =
            Mathf.Max(
                0.0001f,
                cutSurfaceUvScale);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!showCutRangeGizmo)
        {
            return;
        }

        if (!TryGetPreviewBounds(
                out Bounds sourceBounds))
        {
            return;
        }

        if (!ChainsawMeshCutProcessor.TryCalculateCutRangeBounds(
                sourceBounds,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                out Bounds cutRangeBounds))
        {
            return;
        }

        Matrix4x4 previousMatrix =
            Gizmos.matrix;

        Color previousColor =
            Gizmos.color;

        // Runtime切断と同じEnemyMeshCutterローカル空間で表示するため、
        // モデル子階層のRotationやFBX由来の軸には依存しない。
        Gizmos.matrix =
            transform.localToWorldMatrix;

        Gizmos.color =
            new Color(
                1.0f,
                0.0f,
                0.0f,
                0.18f);

        Gizmos.DrawCube(
            cutRangeBounds.center,
            cutRangeBounds.size);

        Gizmos.color =
            new Color(
                1.0f,
                0.0f,
                0.0f,
                1.0f);

        Gizmos.DrawWireCube(
            cutRangeBounds.center,
            cutRangeBounds.size);

        Gizmos.matrix =
            previousMatrix;

        Gizmos.color =
            previousColor;
    }

    /// <summary>
    /// Sceneビューの表示もRuntime切断と同じ条件にするため、
    /// 一時Meshを生成してEnemyMeshCutterローカルBoundsを取得する。
    /// </summary>
    private bool TryGetPreviewBounds(
        out Bounds sourceBounds)
    {
        sourceBounds = default;

        Mesh previewMesh = null;

        if (targetSkinnedMeshRenderer != null &&
            targetSkinnedMeshRenderer.sharedMesh != null)
        {
            previewMesh =
                new Mesh
                {
                    name =
                        $"{targetSkinnedMeshRenderer.name}_CutPreview"
                };

            // Runtimeと同じBake条件を使い、現在姿勢の頂点から
            // Cut Range GizmoのBoundsを求める。
            targetSkinnedMeshRenderer.BakeMesh(
                previewMesh,
                true);

            if (!TryTransformMeshToCutSpace(
                    previewMesh,
                    targetSkinnedMeshRenderer.transform,
                    out _))
            {
                DestroyRuntimeMesh(previewMesh);
                return false;
            }
        }
        else if (targetMeshFilter != null &&
                 targetMeshFilter.sharedMesh != null &&
                 targetMeshFilter.sharedMesh.isReadable)
        {
            previewMesh =
                Instantiate(
                    targetMeshFilter.sharedMesh);

            previewMesh.name =
                $"{targetMeshFilter.sharedMesh.name}_CutPreview";

            if (!TryTransformMeshToCutSpace(
                    previewMesh,
                    targetMeshFilter.transform,
                    out _))
            {
                DestroyRuntimeMesh(previewMesh);
                return false;
            }
        }

        if (previewMesh == null)
        {
            return false;
        }

        sourceBounds =
            previewMesh.bounds;

        DestroyRuntimeMesh(previewMesh);

        return true;
    }
}
