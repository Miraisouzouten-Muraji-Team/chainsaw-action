using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

/// <summary>
/// 選択したCameraからCubemapを生成するエディタツール。
/// </summary>
public static class CubemapCaptureTool
{
    /// <summary>
    /// Cubemapの1面あたりの解像度。
    /// </summary>
    private const int Resolution = 512;

    /// <summary>
    /// 選択中のCameraからCubemapを生成する。
    /// </summary>
    [MenuItem("Tools/Cubemap/Generate From Selected Camera")]
    private static void GenerateCubemap()
    {
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            Debug.LogError("Cameraを選択してください。");
            return;
        }

        Camera captureCamera = selectedObject.GetComponent<Camera>();

        if (captureCamera == null)
        {
            Debug.LogError("選択したオブジェクトにCameraがありません。");
            return;
        }

        // RenderTextureからCubemapへのコピーに対応しているか確認
        if ((SystemInfo.copyTextureSupport &
             CopyTextureSupport.RTToTexture) == 0)
        {
            Debug.LogError(
                "現在の環境はRenderTextureからCubemapへのコピーに対応していません。"
            );
            return;
        }

        // 保存先を指定
        string path = EditorUtility.SaveFilePanelInProject(
            "Cubemapを保存",
            selectedObject.name + "_Cubemap",
            "cubemap",
            "Cubemapの保存先を選択してください。"
        );

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        path = AssetDatabase.GenerateUniqueAssetPath(path);

        // Cubemap形式のRenderTextureを作成
        RenderTexture renderTexture = new RenderTexture(
            Resolution,
            Resolution,
            24,
            RenderTextureFormat.ARGB32
        );

        renderTexture.dimension = TextureDimension.Cube;
        renderTexture.useMipMap = false;
        renderTexture.antiAliasing = 1;

        renderTexture.Create();

        // 撮影用Cameraから6方向を一括撮影
        bool success = captureCamera.RenderToCubemap(
            renderTexture,
            63
        );

        if (!success)
        {
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);

            Debug.LogError("Cubemapの撮影に失敗しました。");
            return;
        }

        // 保存用Cubemapを作成
        Cubemap cubemap = new Cubemap(
            Resolution,
            TextureFormat.ARGB32,
            false
        );

        cubemap.name = selectedObject.name + "_Cubemap";

        // RenderTextureからCubemapの各面へコピー
        for (int i = 0; i < 6; i++)
        {
            Graphics.CopyTexture(
                renderTexture,
                i,
                0,
                cubemap,
                i,
                0
            );

            Debug.Log("CubemapFace : " + (CubemapFace)i + " コピー完了");
        }

        // Project内に保存
        AssetDatabase.CreateAsset(cubemap, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 一時的なRenderTextureを解放
        renderTexture.Release();
        Object.DestroyImmediate(renderTexture);

        Debug.Log("Cubemap生成完了: " + path);
    }

    /// <summary>
    /// Cameraが選択されている場合のみメニューを有効にする。
    /// </summary>
    [MenuItem("Tools/Cubemap/Generate From Selected Camera", true)]
    private static bool ValidateGenerateCubemap()
    {
        return Selection.activeGameObject != null &&
               Selection.activeGameObject.GetComponent<Camera>() != null;
    }
}
