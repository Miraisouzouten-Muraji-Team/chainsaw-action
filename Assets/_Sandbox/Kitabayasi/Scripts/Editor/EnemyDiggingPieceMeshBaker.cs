using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class EnemyDiggingPieceMeshBaker : EditorWindow
{
    private const int PieceCount = EnemyDiggingMeshDeathEffect.BakedPieceCount;
    private const string MeshArrayField = "bakedPieceMeshes";
    private const string BakeScaleField = "bakedSkinnedSourceScale";
    private static readonly string[] PreviewModes = { "Original", "Assembled", "Exploded", "Single Piece" };
    private static readonly string[] PieceLabels = { "01", "02", "03", "04", "05", "06", "07", "08" };

    [SerializeField] private GameObject prefab;
    private PreviewRenderUtility preview;
    private Mesh sourceMesh;
    private Mesh[] pieces;
    private bool ownsSourceMesh;
    private Material[] sourceMaterials;
    private Material cutMaterial;
    private Matrix4x4 sourceMatrix;
    private Matrix4x4 pieceMatrix;
    private Bounds sourceBounds;
    private string sourceName;
    private string status;
    private MessageType statusType;
    private int previewMode = 1;
    private int pieceIndex;
    private Vector2 orbit = new Vector2(20f, 25f);
    private float zoom = 1f;

    [MenuItem("Tools/Enemy/Digging Piece Mesh Baker")]
    private static void Open()
    {
        var window = GetWindow<EnemyDiggingPieceMeshBaker>("Digging Piece Mesh Baker");
        window.minSize = new Vector2(420f, 320f);
        if (GetComponentFromPrefab(Selection.activeGameObject) != null)
        {
            window.prefab = Selection.activeGameObject;
        }

        window.ReloadPreview();
    }

    private void OnEnable() => ReloadPreview();

    private void OnDisable()
    {
        ClearSource();
        preview?.Cleanup();
        preview = null;
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        var selectedPrefab = (GameObject)EditorGUILayout.ObjectField("Enemy Prefab", prefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            prefab = selectedPrefab;
            ReloadPreview();
        }

        using (new EditorGUI.DisabledScope(prefab == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(sourceMesh == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Bake 8 Meshes and Save to Prefab"))
                {
                    BakeSelectedPrefab();
                }
            }

            if (GUILayout.Button("Reload", GUILayout.Width(80f)))
            {
                ReloadPreview();
            }
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.HelpBox(status, statusType);
        }

        if (sourceMesh == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Split Source", sourceName);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Cut Surface Material", cutMaterial, typeof(Material), false);
        }

        if (pieces != null)
        {
            previewMode = GUILayout.Toolbar(previewMode, PreviewModes);
            if (previewMode == 3)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    pieceIndex = EditorGUILayout.Popup("Piece", pieceIndex, PieceLabels);
                    if (GUILayout.Button("Ping Mesh", GUILayout.Width(100f)))
                    {
                        EditorGUIUtility.PingObject(pieces[pieceIndex]);
                    }
                }
            }
        }

        EditorGUILayout.LabelField("Drag to rotate · Scroll to zoom", EditorStyles.miniLabel);
        DrawPreview(GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)));
    }

    private void BakeSelectedPrefab()
    {
        try
        {
            string outputFolder = Bake(prefab);
            ReloadPreview();
            SetStatus($"Saved {PieceCount} meshes and verified the prefab references.\n{outputFolder}", MessageType.Info);
            Debug.Log(status, prefab);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, MessageType.Error);
            Debug.LogException(exception, prefab);
        }
    }

    private static string Bake(GameObject prefabAsset)
    {
        string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
        if (GetComponentFromPrefab(prefabAsset) == null)
        {
            throw new InvalidOperationException("Select a prefab asset with EnemyDiggingMeshDeathEffect on its root.");
        }

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if ((stage != null && stage.assetPath == prefabPath && stage.scene.isDirty) ||
            EditorUtility.IsDirty(prefabAsset) || !AssetDatabase.IsOpenForEdit(prefabPath))
        {
            throw new InvalidOperationException("Save the target prefab's pending edits and make it writable before baking.");
        }

        string name = Path.GetFileNameWithoutExtension(prefabPath);
        string parentFolder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
        string outputFolder = $"{parentFolder}/{name}/DiggingPieces";
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        Mesh input = null;
        bool ownsInput = false;
        List<Mesh> generated = null;
        try
        {
            var effect = root.GetComponent<EnemyDiggingMeshDeathEffect>();
            if (!effect.TryGetEditorBakeSource(out input, out Renderer renderer, out _, out ownsInput, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (!new MeshTargetCountSplitter().TrySplit(input, PieceCount, effect.EditorCutSurfaceUvScale,
                    out generated, out reason))
            {
                throw new InvalidOperationException($"Split failed for {name}. {reason}");
            }

            if (generated.Count != PieceCount)
            {
                throw new InvalidOperationException($"The splitter returned {generated.Count} meshes. Expected {PieceCount}.");
            }

            Vector3 bakeScale = ownsInput ? renderer.transform.lossyScale : Vector3.zero;
            SavePieces(root, effect, prefabPath, outputFolder, name, generated, bakeScale);
            return outputFolder;
        }
        finally
        {
            if (generated != null)
            {
                foreach (Mesh mesh in generated)
                {
                    if (mesh != null && !EditorUtility.IsPersistent(mesh))
                    {
                        DestroyImmediate(mesh);
                    }
                }
            }

            PrefabUtility.UnloadPrefabContents(root);
            if (ownsInput)
            {
                DestroyImmediate(input);
            }
        }
    }

    private static void SavePieces(GameObject root, EnemyDiggingMeshDeathEffect effect, string prefabPath,
        string outputFolder, string prefabName, IReadOnlyList<Mesh> generated, Vector3 bakeScale)
    {
        using var serialized = new SerializedObject(effect);
        SerializedProperty references = serialized.FindProperty(MeshArrayField);
        SerializedProperty scale = serialized.FindProperty(BakeScaleField);
        var paths = new string[PieceCount];
        var saved = new Mesh[PieceCount];
        for (int index = 0; index < PieceCount; index++)
        {
            generated[index].name = $"{prefabName}_DiggingPiece_{index + 1:D2}";
            paths[index] = $"{outputFolder}/{generated[index].name}.asset";
            Object existing = AssetDatabase.LoadMainAssetAtPath(paths[index]);
            Object referenced = index < references.arraySize
                ? references.GetArrayElementAtIndex(index).objectReferenceValue
                : null;

            // Only overwrite meshes already assigned to this prefab in the matching slot.
            if (existing != referenced ||
                (existing != null && (existing is not Mesh || EditorUtility.IsDirty(existing))) ||
                (existing == null && (File.Exists(paths[index]) || File.Exists(paths[index] + ".meta"))) ||
                !AssetDatabase.IsOpenForEdit(paths[index]))
            {
                throw new InvalidOperationException($"Resolve the existing asset or prefab reference before baking.\n{paths[index]}");
            }

            saved[index] = existing as Mesh;
        }

        EnsureFolder(Path.GetDirectoryName(outputFolder).Replace('\\', '/'));
        EnsureFolder(outputFolder);

        var previousFiles = new Dictionary<string, byte[]>();
        try
        {
            for (int index = 0; index < PieceCount; index++)
            {
                string path = paths[index];
                Mesh mesh = saved[index];
                if (mesh == null)
                {
                    previousFiles.Add(path, null);
                    AssetDatabase.CreateAsset(generated[index], path);
                }
                else
                {
                    // Preserve the existing GUID and local file ID.
                    previousFiles.Add(path, File.ReadAllBytes(path));
                    EditorUtility.CopySerialized(generated[index], mesh);
                    EditorUtility.SetDirty(mesh);
                    AssetDatabase.SaveAssetIfDirty(mesh);
                }

                saved[index] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved[index] == null || !File.Exists(path) || EditorUtility.IsDirty(saved[index]))
                {
                    throw new IOException($"Could not save the mesh at {path}.");
                }
            }

            if (!ReferencesMatch(references, saved) || !scale.vector3Value.Equals(bakeScale))
            {
                previousFiles.Add(prefabPath, File.ReadAllBytes(prefabPath));
                references.arraySize = PieceCount;
                for (int index = 0; index < PieceCount; index++)
                {
                    references.GetArrayElementAtIndex(index).objectReferenceValue = saved[index];
                }
                scale.vector3Value = bakeScale;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool savedPrefab);
                if (!savedPrefab)
                {
                    throw new IOException($"Could not save the prefab at {prefabPath}.");
                }
            }

            GameObject reopened = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var reloadedEffect = reopened.GetComponent<EnemyDiggingMeshDeathEffect>();
                using var reloaded = new SerializedObject(reloadedEffect);
                if (!ReferencesMatch(reloaded.FindProperty(MeshArrayField), saved) ||
                    !reloaded.FindProperty(BakeScaleField).vector3Value.Equals(bakeScale))
                {
                    throw new IOException("The saved prefab did not retain the meshes or bake scale.");
                }
                if (!reloadedEffect.TryValidateEditorBakedPieces(out string reason))
                {
                    throw new IOException(reason);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(reopened);
            }
        }
        catch
        {
            RestoreFiles(previousFiles, prefabPath);
            throw;
        }
    }

    private static void RestoreFiles(Dictionary<string, byte[]> previousFiles, string prefabPath)
    {
        // Restore the prefab first so it does not temporarily reference meshes being removed.
        if (previousFiles.Remove(prefabPath, out byte[] previousPrefab))
        {
            RestoreFile(prefabPath, previousPrefab);
        }

        foreach (var entry in previousFiles)
        {
            RestoreFile(entry.Key, entry.Value);
        }
    }

    private static void RestoreFile(string path, byte[] previousBytes)
    {
        try
        {
            if (previousBytes != null)
            {
                File.WriteAllBytes(path, previousBytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            else if (File.Exists(path) && !AssetDatabase.DeleteAsset(path))
            {
                throw new IOException($"Could not remove the incomplete asset at {path}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"Rollback failed for {path}. {exception}");
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        if (Directory.Exists(path) || File.Exists(path) || File.Exists(path + ".meta"))
        {
            throw new IOException($"The output folder is not available to AssetDatabase.\n{path}");
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        if (AssetDatabase.GUIDToAssetPath(guid) != path)
        {
            throw new IOException($"Could not create the output folder at {path}.");
        }
    }

    private static bool ReferencesMatch(SerializedProperty references, IReadOnlyList<Mesh> meshes)
    {
        if (references.arraySize != meshes.Count)
        {
            return false;
        }

        for (int index = 0; index < meshes.Count; index++)
        {
            if (references.GetArrayElementAtIndex(index).objectReferenceValue != meshes[index])
            {
                return false;
            }
        }

        return true;
    }

    private void ReloadPreview()
    {
        ClearSource();
        SetStatus("Select an enemy prefab from the Project window.", MessageType.Info);
        if (prefab == null)
        {
            return;
        }

        if (GetComponentFromPrefab(prefab) == null)
        {
            SetStatus("Select a prefab asset with EnemyDiggingMeshDeathEffect on its root.", MessageType.Error);
            return;
        }

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            var effect = root.GetComponent<EnemyDiggingMeshDeathEffect>();
            if (!effect.TryGetEditorBakeSource(out sourceMesh, out Renderer renderer, out sourceMaterials,
                    out ownsSourceMesh, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            Transform source = renderer.transform;
            bool skinned = renderer is SkinnedMeshRenderer;
            sourceName = skinned ? $"{renderer.name} (saved prefab pose)" : renderer.name;
            sourceMatrix = skinned
                ? Matrix4x4.TRS(source.position, source.rotation, Vector3.one)
                : source.localToWorldMatrix;
            pieceMatrix = sourceMatrix;
            sourceBounds = TransformBounds(sourceMesh.bounds, sourceMatrix);
            cutMaterial = effect.EditorCutSurfaceMaterial;

            using var serialized = new SerializedObject(effect);
            SerializedProperty references = serialized.FindProperty(MeshArrayField);
            if (!effect.TryValidateEditorBakedPieces(out string configurationIssue))
            {
                SetStatus(configurationIssue, MessageType.Warning);
                return;
            }

            var reloaded = new Mesh[PieceCount];
            for (int index = 0; index < PieceCount; index++)
            {
                reloaded[index] = references.GetArrayElementAtIndex(index).objectReferenceValue as Mesh;
            }
            if (!Array.TrueForAll(reloaded, mesh => EditorUtility.IsPersistent(mesh)))
            {
                SetStatus("Save all eight mesh references as project assets.", MessageType.Warning);
                return;
            }

            pieces = reloaded;
            if (skinned)
            {
                pieceMatrix = Matrix4x4.TRS(source.position, source.rotation, effect.EditorGetBakedSkinnedPieceScale(source.lossyScale));
            }
            SetStatus($"Loaded {PieceCount} meshes from the prefab.", MessageType.Info);
        }
        catch (Exception exception)
        {
            ClearSource();
            SetStatus(exception.Message, MessageType.Error);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static EnemyDiggingMeshDeathEffect GetComponentFromPrefab(GameObject asset)
    {
        return asset != null && AssetDatabase.IsMainAsset(asset) &&
            AssetDatabase.GetAssetPath(asset).EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            ? asset.GetComponent<EnemyDiggingMeshDeathEffect>()
            : null;
    }

    private void DrawPreview(Rect rect)
    {
        Event input = Event.current;
        if (rect.Contains(input.mousePosition))
        {
            if (input.type == EventType.MouseDrag && input.button == 0)
            {
                orbit.x = Mathf.Clamp(orbit.x + input.delta.y, -89f, 89f);
                orbit.y += input.delta.x;
                input.Use();
                Repaint();
            }
            else if (input.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.08f, input.delta.y), 0.3f, 3f);
                input.Use();
                Repaint();
            }
        }

        if (input.type != EventType.Repaint || rect.width < 2f || rect.height < 2f)
        {
            return;
        }

        preview ??= new PreviewRenderUtility();
        bool single = pieces != null && previewMode == 3;
        bool exploded = pieces != null && previewMode == 2;
        Bounds bounds = single ? TransformBounds(pieces[pieceIndex].bounds, pieceMatrix) : sourceBounds;
        float radius = Mathf.Max(0.001f, bounds.extents.magnitude);
        float distance = radius * 5f;
        Quaternion rotation = Quaternion.Euler(orbit.x, orbit.y, 0f);
        Camera camera = preview.camera;
        camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
        camera.orthographic = true;
        camera.orthographicSize = radius * (exploded ? 1.45f : 1.08f) * zoom * Mathf.Max(1f, rect.height / rect.width);
        camera.nearClipPlane = distance * 0.1f;
        camera.farClipPlane = distance * 2f;
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = new Color(0.19f, 0.19f, 0.19f);
        preview.lights[0].transform.rotation = rotation * Quaternion.Euler(35f, -40f, 0f);
        preview.lights[1].transform.rotation = rotation * Quaternion.Euler(340f, 140f, 0f);
        preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);

        preview.BeginPreview(rect, GUIStyle.none);
        Texture rendered;
        try
        {
            if (pieces == null || previewMode == 0)
            {
                DrawMesh(sourceMesh, sourceMatrix);
            }
            else if (single)
            {
                DrawMesh(pieces[pieceIndex], pieceMatrix);
            }
            else
            {
                foreach (Mesh mesh in pieces)
                {
                    Vector3 displacement = exploded
                        ? (pieceMatrix.MultiplyPoint3x4(mesh.bounds.center) - sourceBounds.center).normalized * radius * 0.36f
                        : Vector3.zero;
                    DrawMesh(mesh, Matrix4x4.Translate(displacement) * pieceMatrix);
                }
            }

            preview.Render(allowScriptableRenderPipeline: true, updatefov: false);
        }
        finally
        {
            rendered = preview.EndPreview();
        }

        GUI.DrawTexture(rect, rendered, ScaleMode.StretchToFill, false);
    }

    private void DrawMesh(Mesh mesh, Matrix4x4 matrix)
    {
        for (int index = 0; index < mesh.subMeshCount; index++)
        {
            Material material = index < sourceMaterials.Length ? sourceMaterials[index] : cutMaterial;
            if (material != null && mesh.GetIndexCount(index) > 0)
            {
                preview.DrawMesh(mesh, matrix, material, index, null, null, false);
            }
        }
    }

    private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 x = matrix.MultiplyVector(Vector3.right * bounds.extents.x);
        Vector3 y = matrix.MultiplyVector(Vector3.up * bounds.extents.y);
        Vector3 z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
        var size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f;
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center), size);
    }

    private void ClearSource()
    {
        if (ownsSourceMesh && sourceMesh != null)
        {
            DestroyImmediate(sourceMesh);
        }

        ownsSourceMesh = false;
        sourceMesh = null;
        pieces = null;
    }

    private void SetStatus(string message, MessageType type)
    {
        status = message;
        statusType = type;
    }
}
