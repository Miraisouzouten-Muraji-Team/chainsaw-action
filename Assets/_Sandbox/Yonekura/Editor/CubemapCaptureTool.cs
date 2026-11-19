//using UnityEngine;
//using UnityEditor;

///// <summary>
///// 選択したCameraからCubemapを生成するエディタツール。
///// </summary>
//public static class CubemapCaptureTool
//{
//    /// <summary>
//    /// Cubemapの1面あたりの解像度。
//    /// </summary>
//    private const int Resolution = 1024;

//    /// <summary>
//    /// 選択中のCameraからCubemapを生成する。
//    /// </summary>
//    [MenuItem("Tools/Cubemap/Generate From Selected Camera")]
//    private static void GenerateCubemap()
//    {
//        GameObject selectedObject = Selection.activeGameObject;

//        if (selectedObject == null)
//        {
//            Debug.LogError("撮影用Cameraを選択してください。");
//            return;
//        }

//        Camera captureCamera = selectedObject.GetComponent<Camera>();

//        if (captureCamera == null)
//        {
//            Debug.LogError("選択したオブジェクトにCameraがありません。");
//            return;
//        }

//        // 保存先を選択
//        string path = EditorUtility.SaveFilePanelInProject(
//            "Cubemapを保存",
//            selectedObject.name + "_Cubemap",
//            "cubemap",
//            "保存先を選択してください。"
//        );

//        if (string.IsNullOrEmpty(path))
//            return;

//        path = AssetDatabase.GenerateUniqueAssetPath(path);

//        // Cubemapを作成
//        Cubemap cubemap = new Cubemap(
//            Resolution,
//            TextureFormat.RGBA32,
//            true
//        );

//        cubemap.name = selectedObject.name + "_Cubemap";

//        // 撮影用Cameraを通常描画から切り離す
//        bool previousEnabled = captureCamera.enabled;
//        captureCamera.enabled = false;

//        // 6方向を一括撮影
//        bool success = captureCamera.RenderToCubemap(
//            cubemap,
//            63
//        );

//        // Cameraの状態を戻す
//        captureCamera.enabled = previousEnabled;

//        if (!success)
//        {
//            Object.DestroyImmediate(cubemap);
//            Debug.LogError("Cubemapの撮影に失敗しました。");
//            return;
//        }

//        // Project内に保存
//        AssetDatabase.CreateAsset(cubemap, path);
//        AssetDatabase.SaveAssets();
//        AssetDatabase.Refresh();

//        Debug.Log("Cubemap生成完了: " + path);
//    }

//    /// <summary>
//    /// Cameraが選択されている場合のみメニューを有効にする。
//    /// </summary>
//    [MenuItem("Tools/Cubemap/Generate From Selected Camera", true)]
//    private static bool ValidateGenerateCubemap()
//    {
//        return Selection.activeGameObject != null &&
//               Selection.activeGameObject.GetComponent<Camera>() != null;
//    }
//}


using UnityEngine;
using UnityEditor;

/// <summary>
/// 選択したCameraからCubemapを生成するエディターツール。
/// </summary>
//public static class CubemapCaptureTool
//{
//    // Cubemapの1面あたりの解像度。
//    private const int Resolution = 1024;

//    /// <summary>
//    /// 選択中のCameraからCubemapを生成する。
//    /// </summary>
//    [MenuItem("Tools/Cubemap/Generate From Selected Camera")]
//    private static void GenerateCubemap()
//    {
//        GameObject selectedObject = Selection.activeGameObject;
//        Camera captureCamera = selectedObject != null ? selectedObject.GetComponent<Camera>() : null;

//        if (captureCamera == null)
//        {
//            Debug.LogError("撮影用Cameraを選択してください。");
//            return;
//        }

//        string path = EditorUtility.SaveFilePanelInProject(
//            "Cubemapを保存",
//            selectedObject.name + "_Cubemap",
//            "cubemap",
//            "保存先を選択してください。"
//        );

//        if (string.IsNullOrEmpty(path)) return;
//        path = AssetDatabase.GenerateUniqueAssetPath(path);

//        // 元のカメラ設定を退避
//        Quaternion origRot = captureCamera.transform.rotation;
//        float origFov = captureCamera.fieldOfView;
//        float origAspect = captureCamera.aspect;
//        RenderTexture origTarget = captureCamera.targetTexture;
//        bool origEnabled = captureCamera.enabled;

//        // 1面ずつ撮影するためのカメラ設定（FOV90度、正方形）
//        captureCamera.enabled = false;
//        captureCamera.fieldOfView = 90f;
//        captureCamera.aspect = 1f;

//        RenderTexture rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
//        captureCamera.targetTexture = rt;

//        Cubemap cubemap = new Cubemap(Resolution, TextureFormat.RGBA32, false);
//        Texture2D tempTex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);

//        // Cubemapの6面に対応する回転方向（UnityのCubemap座標系準拠）
//        Quaternion[] faceRotations = new Quaternion[]
//        {
//            Quaternion.LookRotation(Vector3.right, Vector3.down),    // PositiveX (右)
//            Quaternion.LookRotation(Vector3.left, Vector3.down),     // NegativeX (左)
//            Quaternion.LookRotation(Vector3.up, Vector3.forward),    // PositiveY (上)
//            Quaternion.LookRotation(Vector3.down, Vector3.back),     // NegativeY (下)
//            Quaternion.LookRotation(Vector3.forward, Vector3.down),  // PositiveZ (前)
//            Quaternion.LookRotation(Vector3.back, Vector3.down)      // NegativeZ (後)
//        };

//        RenderTexture prevActive = RenderTexture.active;

//        for (int i = 0; i < 6; i++)
//        {
//            // カメラの向きを各面に合わせる
//            captureCamera.transform.rotation = faceRotations[i];
//            captureCamera.Render();

//            // RenderTextureからピクセルを読み取る
//            RenderTexture.active = rt;
//            tempTex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
//            tempTex.Apply();

//            cubemap.SetPixels(tempTex.GetPixels(), (CubemapFace)i);
//        }

//        cubemap.Apply();

//        // カメラ設定とリソースを元に戻す
//        RenderTexture.active = prevActive;
//        captureCamera.targetTexture = origTarget;
//        captureCamera.transform.rotation = origRot;
//        captureCamera.fieldOfView = origFov;
//        captureCamera.aspect = origAspect;
//        captureCamera.enabled = origEnabled;

//        rt.Release();
//        Object.DestroyImmediate(rt);
//        Object.DestroyImmediate(tempTex);

//        // 保存
//        AssetDatabase.CreateAsset(cubemap, path);
//        AssetDatabase.SaveAssets();
//        AssetDatabase.Refresh();

//        Debug.Log("6面個別のCubemap生成完了: " + path);
//        EditorGUIUtility.PingObject(cubemap);
//    }

//    [MenuItem("Tools/Cubemap/Generate From Selected Camera", true)]
//    private static bool ValidateGenerateCubemap()
//    {
//        return Selection.activeGameObject != null &&
//               Selection.activeGameObject.GetComponent<Camera>() != null;
//    }
//}


/// <summary>
/// URP完全対応：一時カメラを生成し、1フレームずつ6方向を撮影してCubemapを作成するツール。
/// </summary>
public static class CubemapCaptureTool
{
    private const int Resolution = 1024;

    private static readonly Vector3[] Directions =
    {
        Vector3.right,   // +X
        Vector3.left,    // -X
        Vector3.up,      // +Y
        Vector3.down,    // -Y
        Vector3.forward, // +Z
        Vector3.back     // -Z
    };

    private static readonly Vector3[] Ups =
    {
        Vector3.up,
        Vector3.up,
        Vector3.back,
        Vector3.forward,
        Vector3.up,
        Vector3.up
    };

    [MenuItem("Tools/Cubemap/Generate Cubemap")]
    private static void GenerateCubemap()
    {
        Camera sourceCamera =
            Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Camera>()
                : null;

        if (sourceCamera == null)
        {
            Debug.LogError("Cameraを選択してください。");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Cubemap",
            "GeneratedCubemap",
            "cubemap",
            "Cubemapの保存先を選択してください。"
        );

        if (string.IsNullOrEmpty(path)) return;

        // 干渉を防ぐため、コンポーネントが付いていない撮影専用カメラを一時生成
        GameObject tempCamObj = new GameObject("TempCubemapCamera");
        tempCamObj.hideFlags = HideFlags.HideAndDontSave;
        tempCamObj.transform.position = sourceCamera.transform.position;

        Camera tempCam = tempCamObj.AddComponent<Camera>();
        tempCam.CopyFrom(sourceCamera);
        tempCam.fieldOfView = 90f;
        tempCam.aspect = 1.0f;

        RenderTexture rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        tempCam.targetTexture = rt;

        Texture2D tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
        Cubemap cubemap = new Cubemap(Resolution, TextureFormat.RGBA32, true);

        int faceIndex = 0;

        // 1フレームに1面ずつ確実にレンダリングを進める
        void CaptureStep()
        {
            if (faceIndex < 6)
            {
                tempCamObj.transform.rotation = Quaternion.LookRotation(Directions[faceIndex], Ups[faceIndex]);
                tempCam.Render();

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                Color[] pixels = FlipVertical(tex.GetPixels(), Resolution, Resolution);
                cubemap.SetPixels(pixels, (CubemapFace)faceIndex);

                faceIndex++;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            else
            {
                EditorApplication.update -= CaptureStep;

                cubemap.Apply();
                AssetDatabase.CreateAsset(cubemap, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 後始末
                tempCam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(tempCamObj);

                Debug.Log($"Cubemapを生成しました: {path}");
                EditorGUIUtility.PingObject(cubemap);
            }
        }

        EditorApplication.update += CaptureStep;
    }

    private static Color[] FlipVertical(Color[] src, int width, int height)
    {
        Color[] dst = new Color[src.Length];
        for (int y = 0; y < height; y++)
        {
            int srcRow = y * width;
            int dstRow = (height - 1 - y) * width;
            System.Array.Copy(src, srcRow, dst, dstRow, width);
        }
        return dst;
    }
}
