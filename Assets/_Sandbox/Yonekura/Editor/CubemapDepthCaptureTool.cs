using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 選択したCameraから深度Cubemapを生成するエディタツール。
/// </summary>
public static class CubemapDepthCaptureTool
{
    /// <summary>
    /// Cubemapの1面あたりの解像度。
    /// </summary>
    private const int Resolution = 1024;

    private const string DepthShaderName =
        "Hidden/CubemapDepthCapture";

    /// <summary>
    /// 選択中のCameraから深度Cubemapを生成する。
    /// </summary>
    [MenuItem("Tools/Cubemap/Generate Depth From Selected Camera")]
    private static void GenerateDepthCubemap()
    {
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            Debug.LogError("撮影用Cameraを選択してください。");
            return;
        }

        Camera captureCamera =
            selectedObject.GetComponent<Camera>();

        if (captureCamera == null)
        {
            Debug.LogError("選択したオブジェクトにCameraがありません。");
            return;
        }

        if (!SystemInfo.SupportsTextureFormat(TextureFormat.RFloat) ||
            !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
        {
            Debug.LogError("RFloat形式のCubemap生成に対応していません。");
            return;
        }

        Shader depthShader = Shader.Find(DepthShaderName);

        if (depthShader == null)
        {
            Debug.LogError("深度計測用Shaderが見つかりません。");
            return;
        }

        // 保存先を選択
        string path = EditorUtility.SaveFilePanelInProject(
            "深度Cubemapを保存",
            selectedObject.name + "_DepthCubemap",
            "cubemap",
            "保存先を選択してください。"
        );

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        path = AssetDatabase.GenerateUniqueAssetPath(path);

        // 深度Cubemapを作成
        Cubemap depthCubemap = new Cubemap(
            Resolution,
            TextureFormat.RFloat,
            false
        );

        depthCubemap.name =
            selectedObject.name + "_DepthCubemap";

        // 深度計測用Materialを作成
        Material depthMaterial = new Material(depthShader);

        depthMaterial.SetFloat(
            "_DepthFar",
            captureCamera.farClipPlane
        );

        // シーン内のRendererを取得
        Renderer[] renderers =
            Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        // 元のMaterialを保存
        List<Material[]> originalMaterials =
            new List<Material[]>();

        List<Renderer> targetRenderers =
            new List<Renderer>();

        bool previousEnabled = captureCamera.enabled;
        CameraClearFlags previousClearFlags =
            captureCamera.clearFlags;

        Color previousBackgroundColor =
            captureCamera.backgroundColor;

        try
        {
            // 撮影中だけMaterialを深度計測用に変更
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                Material[] materials =
                    targetRenderer.sharedMaterials;

                if (materials.Length == 0)
                {
                    continue;
                }

                originalMaterials.Add(materials);
                targetRenderers.Add(targetRenderer);

                Material[] replacementMaterials =
                    new Material[materials.Length];

                for (int i = 0; i < replacementMaterials.Length; i++)
                {
                    replacementMaterials[i] = depthMaterial;
                }

                targetRenderer.sharedMaterials =
                    replacementMaterials;
            }

            // 背景を最大深度として扱う
            captureCamera.enabled = false;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.white;

            // 6方向を一括撮影
            bool success = captureCamera.RenderToCubemap(
                depthCubemap,
                63
            );

            if (!success)
            {
                Debug.LogError("深度Cubemapの撮影に失敗しました。");
                return;
            }

            // Project内に保存
            AssetDatabase.CreateAsset(depthCubemap, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("深度Cubemap生成完了: " + path);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            // 元のMaterialに戻す
            for (int i = 0; i < targetRenderers.Count; i++)
            {
                if (targetRenderers[i] != null)
                {
                    targetRenderers[i].sharedMaterials =
                        originalMaterials[i];
                }
            }

            // Cameraの状態を戻す
            captureCamera.enabled = previousEnabled;
            captureCamera.clearFlags = previousClearFlags;
            captureCamera.backgroundColor = previousBackgroundColor;

            // 一時Materialを破棄
            Object.DestroyImmediate(depthMaterial);

            // 保存に失敗した場合はCubemapを破棄
            if (AssetDatabase.LoadAssetAtPath<Cubemap>(path) == null)
            {
                Object.DestroyImmediate(depthCubemap);
            }
        }
    }

    /// <summary>
    /// Cameraが選択されている場合のみメニューを有効にする。
    /// </summary>
    [MenuItem(
        "Tools/Cubemap/Generate Depth From Selected Camera",
        true
    )]
    private static bool ValidateGenerateDepthCubemap()
    {
        return Selection.activeGameObject != null &&
               Selection.activeGameObject.GetComponent<Camera>() != null;
    }
}
