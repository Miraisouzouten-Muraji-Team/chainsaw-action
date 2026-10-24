using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 親のStageSurfaceMaskだけでMaterial適用と外周距離画像生成を実行するEditorツール。
/// 地面の追加・移動・回転・Scale変更後には再度Apply & Bakeする。
/// </summary>
[CustomEditor(typeof(StageSurfaceMask))]
public sealed class StageSurfaceMaskBakerEditor : Editor
{
    private const int MAX_TEXTURE_SIZE = 2048;
    private const float INF = 1000000000f;
    private const string OUTPUT_FOLDER = "Assets/_Sandbox/Akane/Generated";

    private sealed class ProjectedPolygon
    {
        public readonly List<Vector2> Points;
        public readonly Rect Bounds;

        public ProjectedPolygon(List<Vector2> points)
        {
            Points = points;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            foreach (Vector2 point in points)
            {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            Bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        StageSurfaceMask mask = (StageSurfaceMask)target;
        if (mask.StageMaterial == null)
        {
            EditorGUILayout.HelpBox("Stage Materialを設定してください。", MessageType.Warning);
        }
        else if (!mask.StageMaterial.HasProperty("_StageEdgeWidth") ||
                 !mask.StageMaterial.HasProperty("_StageSurfaceMap"))
        {
            EditorGUILayout.HelpBox("Stage Materialにはステージ用Surface ShaderのMaterialを指定してください。", MessageType.Error);
        }

        if (GUILayout.Button("Apply Material & Bake"))
        {
            ApplyAndBake(mask);
        }
        EditorGUILayout.HelpBox(
            "子階層のMeshRenderer・MeshFilter・BoxColliderがあるCubeを自動取得します。" +
            "プレイヤー・装飾・透明な壁などは、この親の外に配置してください。" +
            "Cubeの追加・移動・回転・Scale変更後は再実行してください。",
            MessageType.Info);
    }

    /// <summary>
    /// 親の設定だけを使って、Material適用と外周距離画像の生成をまとめて行う。
    /// </summary>
    private static void ApplyAndBake(StageSurfaceMask mask)
    {
        if (mask.StageMaterial == null)
        {
            Debug.LogError("Stage Materialが設定されていません。", mask);
            return;
        }
        if (!mask.StageMaterial.HasProperty("_StageEdgeWidth") ||
            !mask.StageMaterial.HasProperty("_StageSurfaceMap"))
        {
            Debug.LogError("Stage MaterialにはSandbox/Akane/Stage/Surfaceを使用してください。", mask);
            return;
        }
        if (mask.MaxDistance <= mask.EdgeWidth)
        {
            Debug.LogError("Max DistanceはEdge Widthより大きくしてください。", mask);
            return;
        }

        List<BoxCollider> boxes = CollectStageBoxes(mask);
        if (boxes.Count == 0)
        {
            Debug.LogError(
                "子階層に有効なMeshRenderer・MeshFilter・BoxColliderを持つCubeがありません。",
                mask);
            return;
        }

        // 同じMaterialを共有することで、各Cubeへの個別設定を不要にする。
        Undo.RecordObject(mask.StageMaterial, "Update Stage Surface Material");
        mask.StageMaterial.SetFloat("_StageEdgeWidth", mask.EdgeWidth);
        EditorUtility.SetDirty(mask.StageMaterial);
        foreach (BoxCollider box in boxes)
        {
            MeshRenderer meshRenderer = box.GetComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial == mask.StageMaterial)
            {
                continue;
            }
            Undo.RecordObject(meshRenderer, "Apply Stage Surface Material");
            meshRenderer.sharedMaterial = mask.StageMaterial;
            EditorUtility.SetDirty(meshRenderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(meshRenderer);
        }
        // 旧バージョンで各Cubeに付けた目印だけを任意で取り除く。
        // 型を直接参照せず、旧スクリプトを削除した後でもEditorがコンパイルできる構成にする。
        int removedCount = 0;
        if (mask.RemoveLegacyPiecesOnApply)
        {
            foreach (BoxCollider box in boxes)
            {
                MonoBehaviour[] components = box.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour component in components)
                {
                    if (component == null || component.GetType().Name != "StageSurfacePiece")
                    {
                        continue;
                    }
                    Undo.DestroyObjectImmediate(component);
                    removedCount++;
                }
            }
        }
        if (mask.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(mask.gameObject.scene);
        }

        Debug.Log($"StageSurfaceのMaterial一括適用が完了しました。Cube数={boxes.Count}、旧スクリプト削除数={removedCount}", mask);
        Bake(mask, boxes);
    }

    /// <summary>
    /// 個別のStageSurfacePieceに依存せず、子階層のCubeを自動取得する。
    /// </summary>
    private static List<BoxCollider> CollectStageBoxes(StageSurfaceMask mask)
    {
        BoxCollider[] colliders = mask.StageRoot.GetComponentsInChildren<BoxCollider>(mask.IncludeInactive);
        List<BoxCollider> boxes = new List<BoxCollider>(colliders.Length);
        foreach (BoxCollider box in colliders)
        {
            if (!box.enabled || box.isTrigger)
            {
                continue;
            }
            if (!box.TryGetComponent(out MeshRenderer meshRenderer) || !meshRenderer.enabled)
            {
                continue;
            }
            if (!box.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
            {
                continue;
            }
            boxes.Add(box);
        }
        return boxes;
    }

    private static void Bake(StageSurfaceMask mask, List<BoxCollider> boxes)
    {
        try
        {
            List<ProjectedPolygon> polygons = CollectPolygons(boxes);
            if (polygons.Count == 0)
            {
                Debug.LogError("有効なBoxColliderの形状を取得できません。", mask);
                return;
            }

            int pixelsPerUnit = mask.PixelsPerUnit;
            float margin = mask.Padding;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            foreach (ProjectedPolygon polygon in polygons)
            {
                minX = Mathf.Min(minX, polygon.Bounds.xMin);
                minY = Mathf.Min(minY, polygon.Bounds.yMin);
                maxX = Mathf.Max(maxX, polygon.Bounds.xMax);
                maxY = Mathf.Max(maxY, polygon.Bounds.yMax);
            }

            float originX = Mathf.Floor((minX - margin) * pixelsPerUnit) / pixelsPerUnit;
            float originY = Mathf.Floor((minY - margin) * pixelsPerUnit) / pixelsPerUnit;
            float endX = Mathf.Ceil((maxX + margin) * pixelsPerUnit) / pixelsPerUnit;
            float endY = Mathf.Ceil((maxY + margin) * pixelsPerUnit) / pixelsPerUnit;
            int width = Mathf.CeilToInt((endX - originX) * pixelsPerUnit);
            int height = Mathf.CeilToInt((endY - originY) * pixelsPerUnit);

            if (width <= 0 || height <= 0 || width > MAX_TEXTURE_SIZE || height > MAX_TEXTURE_SIZE)
            {
                Debug.LogError(
                    $"距離画像が大きすぎます。サイズ: {width}x{height}。" +
                    "Pixels Per Unitを下げるか、対象を分割してください。", mask);
                return;
            }

            bool[] occupied = new bool[width * height];
            foreach (ProjectedPolygon polygon in polygons)
            {
                FillPolygon(occupied, width, height, originX, originY, pixelsPerUnit, polygon);
            }

            bool[] boundary = FindBoundary(occupied, width, height);
            float[] distanceSquared = ComputeSquaredDistance(boundary, width, height);
            Color32[] pixels = CreateDistancePixels(
                occupied, distanceSquared, pixelsPerUnit, mask.MaxDistance);

            Texture2D generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                generatedTexture.SetPixels32(pixels);
                generatedTexture.Apply(false, false);

                EnsureAssetFolder(OUTPUT_FOLDER);
                string sceneName = mask.gameObject.scene.name;
                string fileName = SanitizeName(sceneName + "_" + mask.name + "_StageEdgeDistance.png");
                string assetPath = OUTPUT_FOLDER + "/" + fileName;
                File.WriteAllBytes(Path.GetFullPath(assetPath), generatedTexture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("生成したPNGのTextureImporterを取得できません。");
                }

                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.isReadable = false;
                importer.SaveAndReimport();

                Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (importedTexture == null)
                {
                    throw new InvalidOperationException("焼き込んだ距離画像を読み込めません。");
                }

                Undo.RecordObject(mask, "Bake Stage Edge Distance");
                Vector4 uvTransform = new Vector4(
                    originX, originY,
                    1f / (width / (float)pixelsPerUnit),
                    1f / (height / (float)pixelsPerUnit));
                mask.SetBakedData(importedTexture, uvTransform, mask.MaxDistance);
                EditorUtility.SetDirty(mask);
                PrefabUtility.RecordPrefabInstancePropertyModifications(mask);
                if (mask.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(mask.gameObject.scene);
                }

                Debug.Log(
                    $"StageSurface外周のBakeが完了しました。Cube数={polygons.Count}、" +
                    $"画像={width}x{height}、保存先={assetPath}", mask);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(generatedTexture);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, mask);
        }
    }

    // BoxColliderの8頂点をX/Yへ投影し、回転Cubeにも対応する凸多角形を作る。
    private static List<ProjectedPolygon> CollectPolygons(List<BoxCollider> boxes)
    {
        List<ProjectedPolygon> polygons = new List<ProjectedPolygon>();
        foreach (BoxCollider box in boxes)
        {
            Vector3 halfSize = box.size * 0.5f;
            List<Vector2> points = new List<Vector2>(8);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 cornerOS = box.center + Vector3.Scale(
                            halfSize, new Vector3(x, y, z));
                        Vector3 cornerWS = box.transform.TransformPoint(cornerOS);
                        points.Add(new Vector2(cornerWS.x, cornerWS.y));
                    }
                }
            }

            List<Vector2> hull = BuildConvexHull(points);
            if (hull.Count >= 3)
            {
                polygons.Add(new ProjectedPolygon(hull));
            }
        }
        return polygons;
    }

    private static List<Vector2> BuildConvexHull(List<Vector2> points)
    {
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        List<Vector2> hull = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                       point - hull[hull.Count - 1]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }

        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            Vector2 point = points[i];
            while (hull.Count > lowerCount && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                       point - hull[hull.Count - 1]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }
        if (hull.Count > 1)
        {
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool IsInsideConvexPolygon(Vector2 point, List<Vector2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            if (Cross(b - a, point - a) < -0.00001f)
            {
                return false;
            }
        }
        return true;
    }

    private static void FillPolygon(bool[] occupied, int width, int height,
        float originX, float originY, int pixelsPerUnit, ProjectedPolygon polygon)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt((polygon.Bounds.xMin - originX) * pixelsPerUnit), 0, width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((polygon.Bounds.yMin - originY) * pixelsPerUnit), 0, height - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((polygon.Bounds.xMax - originX) * pixelsPerUnit), 0, width - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((polygon.Bounds.yMax - originY) * pixelsPerUnit), 0, height - 1);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 point = new Vector2(
                    originX + (x + 0.5f) / pixelsPerUnit,
                    originY + (y + 0.5f) / pixelsPerUnit);
                if (IsInsideConvexPolygon(point, polygon.Points))
                {
                    occupied[y * width + x] = true;
                }
            }
        }
    }

    private static bool[] FindBoundary(bool[] occupied, int width, int height)
    {
        bool[] boundary = new bool[occupied.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!occupied[index])
                {
                    continue;
                }
                bool left = x > 0 && occupied[index - 1];
                bool right = x < width - 1 && occupied[index + 1];
                bool bottom = y > 0 && occupied[index - width];
                bool top = y < height - 1 && occupied[index + width];
                boundary[index] = !(left && right && bottom && top);
            }
        }
        return boundary;
    }

    // 1次元の厳密な二乗ユークリッド距離変換。2軸に適用して画像の距離を求める。
    private static void DistanceTransform1D(float[] source, float[] result, int count)
    {
        int[] candidates = new int[count];
        float[] intersections = new float[count + 1];
        int candidateCount = -1;

        for (int q = 0; q < count; q++)
        {
            if (source[q] >= INF)
            {
                continue;
            }
            if (candidateCount < 0)
            {
                candidateCount = 0;
                candidates[0] = q;
                intersections[0] = float.NegativeInfinity;
                intersections[1] = float.PositiveInfinity;
                continue;
            }

            float intersection = 0f;
            while (candidateCount >= 0)
            {
                int previous = candidates[candidateCount];
                intersection = ((source[q] + q * q) -
                    (source[previous] + previous * previous)) / (2f * (q - previous));
                if (intersection > intersections[candidateCount])
                {
                    break;
                }
                candidateCount--;
            }
            candidateCount++;
            candidates[candidateCount] = q;
            intersections[candidateCount] = candidateCount == 0
                ? float.NegativeInfinity : intersection;
            intersections[candidateCount + 1] = float.PositiveInfinity;
        }

        if (candidateCount < 0)
        {
            for (int q = 0; q < count; q++) result[q] = INF;
            return;
        }

        int k = 0;
        for (int q = 0; q < count; q++)
        {
            while (k < candidateCount && intersections[k + 1] < q) k++;
            int delta = q - candidates[k];
            result[q] = delta * delta + source[candidates[k]];
        }
    }

    private static float[] ComputeSquaredDistance(bool[] boundary, int width, int height)
    {
        float[] rowDistance = new float[width * height];
        float[] finalDistance = new float[width * height];
        int maxLength = Mathf.Max(width, height);
        float[] source = new float[maxLength];
        float[] result = new float[maxLength];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                source[x] = boundary[y * width + x] ? 0f : INF;
            }
            DistanceTransform1D(source, result, width);
            for (int x = 0; x < width; x++) rowDistance[y * width + x] = result[x];
        }
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) source[y] = rowDistance[y * width + x];
            DistanceTransform1D(source, result, height);
            for (int y = 0; y < height; y++) finalDistance[y * width + x] = result[y];
        }
        return finalDistance;
    }

    private static Color32[] CreateDistancePixels(bool[] occupied, float[] distanceSquared,
        int pixelsPerUnit, float maxDistance)
    {
        Color32[] pixels = new Color32[occupied.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            float distanceWS = occupied[i]
                ? (Mathf.Sqrt(distanceSquared[i]) + 0.5f) / pixelsPerUnit
                : 0f;
            byte value = (byte)Mathf.RoundToInt(
                Mathf.Clamp01(distanceWS / maxDistance) * 255f);
            pixels[i] = new Color32(value, value, value, 255);
        }
        return pixels;
    }

    private static void EnsureAssetFolder(string path)
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

    private static string SanitizeName(string input)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(invalidChar, '_');
        }
        return input.Replace('/', '_').Replace('\\', '_');
    }
}
