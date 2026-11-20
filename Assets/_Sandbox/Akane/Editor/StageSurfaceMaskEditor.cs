
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ステージ外周と縁用メッシュをEditorで生成する。
/// 元モデルのRead/Write設定は変更しない。
/// </summary>
[CustomEditor(typeof(StageSurfaceMask))]
public sealed class StageSurfaceMaskEditor : Editor
{
    private const string OUTPUT_FOLDER =
        "Assets/_Sandbox/Akane/Generated";

    private const string GENERATED_MESH_PREFIX =
        "GeneratedStageEdge_";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        StageSurfaceMask mask = (StageSurfaceMask)target;

        if (mask.StageMaterial == null ||
            mask.StageMaterial.shader.name != "Custom/StageSurface")
        {
            EditorGUILayout.HelpBox(
                "Custom/StageSurfaceのMaterialを設定してください。",
                MessageType.Error);
            return;
        }

        if (GUILayout.Button("Rebuild Stage Edge Mesh"))
        {
            Rebuild(mask, false);
        }

        if (GUILayout.Button("Recapture Profiles & Rebuild"))
        {
            Rebuild(mask, true);
        }

        EditorGUILayout.HelpBox(
            "Rebuild: 保存済み形状を使って縁を再生成します。\n" +
            "Recapture: 元モデルから頂点・三角形情報も取得し直します。\n" +
            "地形の配置や回転を変えた場合はRebuildしてください。\n" +
            "元モデル自体を変更した場合はRecaptureを使用してください。",
            MessageType.Info);
    }

    private static void Rebuild(
        StageSurfaceMask mask, bool forceRecapture)
    {
        if (!mask.gameObject.scene.IsValid() ||
            string.IsNullOrEmpty(mask.gameObject.scene.path))
        {
            Debug.LogError(
                "シーンを保存してから実行してください。", mask);
            return;
        }

        List<MeshFilter> filters = mask.CollectStageMeshes();

        if (filters.Count == 0)
        {
            Debug.LogError(
                "地形用MeshFilterがありません。", mask);
            return;
        }

        try
        {
            EnsureFolder(OUTPUT_FOLDER);

            StagePieceProfileLibrary library =
                GetOrCreateLibrary(mask);

            int captureCount = 0;

            foreach (MeshFilter filter in filters)
            {
                Mesh sourceMesh = filter.sharedMesh;

                if (!forceRecapture &&
                    library.TryGetProfile(sourceMesh, out _))
                    continue;

                CaptureMesh(sourceMesh,
                    out Vector3[] vertices,
                    out int[] triangles);

                library.SetProfile(
                    sourceMesh, vertices, triangles);

                captureCount++;
            }

            EditorUtility.SetDirty(library);

            // 形状情報を登録してから外周メッシュを生成する。
            // 生成処理に失敗した場合、既存の生成物は残す。
            List<StageEdgeMeshBuilder.BuildResult> results =
                StageEdgeMeshBuilder.Build(mask, filters);

            if (results.Count == 0)
            {
                Debug.LogError(
                    "縁用メッシュが1つも生成されませんでした。" +
                    "Camera At Negative Zの設定と" +
                    "元メッシュの面の向きを確認してください。",
                    mask);
                return;
            }

            Material edgeMaterial =
                GetOrCreateEdgeMaterial(mask);

            ConfigureMaterials(mask, edgeMaterial);
            ApplyStageMaterial(mask, filters);

            // 新しい生成に成功した後で古い生成物を削除する。
            RemoveGeneratedObjects(filters);
            RemoveGeneratedMeshAssets(library);

            int generatedCount = 0;

            foreach (StageEdgeMeshBuilder.BuildResult result in results)
            {
                MeshFilter source = result.Source;
                MeshRenderer sourceRenderer =
                    source.GetComponent<MeshRenderer>();

                Mesh edgeMesh = result.GeneratedMesh;

                AssetDatabase.AddObjectToAsset(
                    edgeMesh, library);

                GameObject generated = new(
                    StageSurfaceMask.GENERATED_OBJECT_NAME);

                Undo.RegisterCreatedObjectUndo(
                    generated, "Create Stage Edge");

                generated.transform.SetParent(
                    source.transform, false);

                generated.transform.localPosition = Vector3.zero;
                generated.transform.localRotation =
                    Quaternion.identity;
                generated.transform.localScale = Vector3.one;

                generated.layer = source.gameObject.layer;

                MeshFilter meshFilter =
                    generated.AddComponent<MeshFilter>();

                MeshRenderer meshRenderer =
                    generated.AddComponent<MeshRenderer>();

                meshFilter.sharedMesh = edgeMesh;
                meshRenderer.sharedMaterial = edgeMaterial;

                // 縁そのものは新たな影を落とさない。
                meshRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;

                meshRenderer.receiveShadows = true;
                meshRenderer.lightProbeUsage =
                    sourceRenderer.lightProbeUsage;

                // Rendering Layerを元オブジェクトに合わせる。
                // 親のPixelRenderOverrideも継承される。
                meshRenderer.renderingLayerMask =
                    sourceRenderer.renderingLayerMask;

                generatedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                AssetDatabase.GetAssetPath(library));

            EditorUtility.SetDirty(mask);

            PrefabUtility
                .RecordPrefabInstancePropertyModifications(mask);

            EditorSceneManager.MarkSceneDirty(
                mask.gameObject.scene);

            Debug.Log(
                $"StageEdge再生成完了: " +
                $"地形={filters.Count}, " +
                $"形状更新={captureCount}, " +
                $"生成メッシュ={generatedCount}",
                mask);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, mask);
        }
    }

    /// <summary>
    /// Read/Write OFFのMeshをEditor専用APIで読み取る。
    /// </summary>
    private static void CaptureMesh(
        Mesh sourceMesh,
        out Vector3[] vertices,
        out int[] triangles)
    {
        using Mesh.MeshDataArray meshDataArray =
            MeshUtility.AcquireReadOnlyMeshData(sourceMesh);

        Mesh.MeshData meshData = meshDataArray[0];

        using NativeArray<Vector3> nativeVertices =
            new(meshData.vertexCount, Allocator.Temp);

        meshData.GetVertices(nativeVertices);

        vertices = nativeVertices.ToArray();

        List<int> allIndices = new();

        for (int submeshIndex = 0;
             submeshIndex < meshData.subMeshCount;
             submeshIndex++)
        {
            if (sourceMesh.GetTopology(submeshIndex) !=
                MeshTopology.Triangles)
                continue;

            var submesh = meshData.GetSubMesh(submeshIndex);

            using NativeArray<int> nativeIndices =
                new(submesh.indexCount, Allocator.Temp);

            meshData.GetIndices(
                nativeIndices, submeshIndex);

            for (int i = 0; i < nativeIndices.Length; i++)
            {
                allIndices.Add(nativeIndices[i]);
            }
        }

        triangles = allIndices.ToArray();

        if (triangles.Length == 0)
        {
            throw new InvalidOperationException(
                $"{sourceMesh.name}: 三角形がありません。");
        }
    }

    private static StagePieceProfileLibrary GetOrCreateLibrary(
        StageSurfaceMask mask)
    {
        if (mask.ProfileLibrary != null)
            return mask.ProfileLibrary;

        string path = OUTPUT_FOLDER + "/" +
            Sanitize(mask.gameObject.scene.name + "_" +
                     mask.name + "_StageProfiles.asset");

        StagePieceProfileLibrary library =
            AssetDatabase.LoadAssetAtPath
                <StagePieceProfileLibrary>(path);

        if (library == null)
        {
            library = ScriptableObject.CreateInstance
                <StagePieceProfileLibrary>();

            AssetDatabase.CreateAsset(library, path);
        }

        Undo.RecordObject(mask, "Assign Stage Profile Library");
        mask.SetProfileLibrary(library);
        EditorUtility.SetDirty(mask);

        return library;
    }

    private static Material GetOrCreateEdgeMaterial(
        StageSurfaceMask mask)
    {
        if (mask.EdgeMaterial != null)
            return mask.EdgeMaterial;

        string path = OUTPUT_FOLDER + "/" +
            Sanitize(mask.gameObject.scene.name + "_" +
                     mask.name + "_StageEdge.mat");

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(mask.StageMaterial);
            material.name = mask.name + "_StageEdge";
            AssetDatabase.CreateAsset(material, path);
        }

        Undo.RecordObject(mask, "Assign Stage Edge Material");
        mask.SetEdgeMaterial(material);
        EditorUtility.SetDirty(mask);

        return material;
    }

    /// <summary>
    /// 元の地形は内側用、生成メッシュは縁用として設定する。
    /// </summary>
    private static void ConfigureMaterials(
        StageSurfaceMask mask, Material edgeMaterial)
    {
        Material baseMaterial = mask.StageMaterial;

        Undo.RecordObject(
            baseMaterial, "Configure Stage Base Material");

        baseMaterial.SetFloat("_StageEdgeOnly", 0f);
        baseMaterial.SetFloat("_StageZWrite", 1f);
        baseMaterial.SetFloat("_StageDepthBiasFactor", 0f);
        baseMaterial.SetFloat("_StageDepthBiasUnits", 0f);

        EditorUtility.SetDirty(baseMaterial);

        Undo.RecordObject(
            edgeMaterial, "Configure Stage Edge Material");

        edgeMaterial.CopyPropertiesFromMaterial(baseMaterial);

        edgeMaterial.SetFloat("_StageEdgeOnly", 1f);

        // 元の地形を先に描画し、縁は後から描画する。
        edgeMaterial.renderQueue =
            (int)RenderQueue.Geometry + 1;

        // 縁から深度を書き込まず元の地形深度を維持する。
        edgeMaterial.SetFloat("_StageZWrite", 0f);

        // 深度競合を抑えるための描画時オフセット。
        edgeMaterial.SetFloat("_StageDepthBiasFactor", -1f);
        edgeMaterial.SetFloat("_StageDepthBiasUnits", -1f);

        edgeMaterial.SetShaderPassEnabled(
            "ShadowCaster", false);

        EditorUtility.SetDirty(edgeMaterial);
    }

    private static void ApplyStageMaterial(
        StageSurfaceMask mask, List<MeshFilter> filters)
    {
        foreach (MeshFilter filter in filters)
        {
            MeshRenderer renderer =
                filter.GetComponent<MeshRenderer>();

            Material[] materials = renderer.sharedMaterials;

            if (materials.Length == 0)
                continue;

            if (materials[0] == mask.StageMaterial)
                continue;

            Undo.RecordObject(
                renderer, "Apply Stage Surface Material");

            materials[0] = mask.StageMaterial;
            renderer.sharedMaterials = materials;

            EditorUtility.SetDirty(renderer);

            PrefabUtility
                .RecordPrefabInstancePropertyModifications(
                    renderer);
        }
    }

    private static void RemoveGeneratedObjects(
        List<MeshFilter> filters)
    {
        foreach (MeshFilter filter in filters)
        {
            Transform child = filter.transform.Find(
                StageSurfaceMask.GENERATED_OBJECT_NAME);

            if (child == null)
                continue;

            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static void RemoveGeneratedMeshAssets(
        StagePieceProfileLibrary library)
    {
        string path = AssetDatabase.GetAssetPath(library);

        UnityEngine.Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(path);

        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is not Mesh mesh)
                continue;

            if (!mesh.name.StartsWith(
                GENERATED_MESH_PREFIX,
                StringComparison.Ordinal))
                continue;

            UnityEngine.Object.DestroyImmediate(mesh, true);
        }
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string parent = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string current = parent + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(current))
            {
                AssetDatabase.CreateFolder(parent, parts[i]);
            }

            parent = current;
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }

        return name.Replace('/', '_').Replace('\\', '_');
    }
}
