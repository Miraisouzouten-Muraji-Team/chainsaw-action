#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RiftPrototypeA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 元のStageSelectController/Scene/StageDatabase/Stage1を変更せず、方式Aの検証用コピーを生成する。
/// 必ずbetaとalpha(scenes)/create-stage-select-sceneの統合後に実行する。
/// </summary>
public static class RiftAPrototypeInstaller
{
    private const string SOURCE_CONTROLLER = "Assets/_Sandbox/Yonekura/Scripts/StageSelectScene/StageSelectController.cs";
    private const string SOURCE_SELECT_SCENE = "Assets/_Sandbox/Yonekura/Scenes/StageSelectScene1.unity";
    private const string SOURCE_DATABASE = "Assets/_Sandbox/Yonekura/ScriptableObject/StageSelect/StageDatabase.asset";
    private const string SOURCE_STAGE_SCENE = "Assets/_Project/Scenes/Stage1.unity";
    private const string ROOT = "Assets/_Sandbox/Akane/RiftPrototype";
    private const string GENERATED_CONTROLLER = ROOT + "/Scripts/RiftAStageSelectController.cs";
    private const string TARGET_DATABASE = ROOT + "/Data/RiftA_StageDatabase.asset";
    private const string TARGET_SELECT_SCENE = ROOT + "/Scenes/RiftA_StageSelect.unity";
    private const string TARGET_STAGE_SCENE = ROOT + "/Scenes/RiftA_Stage1.unity";
    private const string RIFT_SHADER = ROOT + "/Shaders/RiftAScreenSpace.shader";

    private const string CONFIRM_METHOD = @"
    /// <summary>方式A専用。カードを拡大せず、3D世界へ入る。</summary>
    public void ConfirmSelection()
    {
        if (stageDatabase == null || stageDatabase.Areas == null ||
            stageDatabase.Areas.Length == 0 || isAnimating || isLoadingScene)
            return;

        if (!StageProgressManager.IsAreaUnlocked(currentIndex, GetSceneNames()))
        {
            if (lockMessageText != null)
            {
                lockMessageText.text = ""前のエリアをクリアすると開放されます"";
                lockMessageText.gameObject.SetActive(true);
            }
            return;
        }

        if (riftATransitionController == null || currentCard == null ||
            currentCard.ScreenshotImage == null)
        {
            Debug.LogError(""方式AのRiftATransitionControllerまたはカード画像が未設定です。"", this);
            return;
        }

        string sceneName = stageDatabase.Areas[currentIndex].sceneName;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError(""遷移先シーン名が未設定です。"", this);
            return;
        }

        if (lockMessageText != null) lockMessageText.gameObject.SetActive(false);
        isAnimating = true;
        isLoadingScene = true;
        riftATransitionController.PlayTransition(
            sceneName,
            currentCard.ScreenshotImage.rectTransform,
            () =>
            {
                isAnimating = false;
                isLoadingScene = false;
            });
    }
";

    [MenuItem("Tools/Rift Prototype A/1. Generate Controller Copy")]
    public static void GenerateControllerCopy()
    {
        if (!File.Exists(SOURCE_CONTROLLER))
        {
            Debug.LogError("元のStageSelectController.csが見つかりません。2ブランチを統合してください。");
            return;
        }
        if (File.Exists(GENERATED_CONTROLLER))
        {
            Debug.LogWarning("方式Aのコントローラーは作成済みです。上書きせず終了します。");
            return;
        }

        string code = File.ReadAllText(SOURCE_CONTROLLER);
        code = code.Replace("public class StageSelectController : MonoBehaviour",
            "public class RiftAStageSelectController : MonoBehaviour");
        if (!code.Contains("public class RiftAStageSelectController : MonoBehaviour"))
            throw new InvalidOperationException("元コントローラーのクラス宣言が想定外です。");

        string oldField = "private StageWarpController stageWarpController;";
        if (!code.Contains(oldField))
            throw new InvalidOperationException("元コントローラーのStageWarpControllerフィールドが見つかりません。");
        code = code.Replace(oldField, "private RiftATransitionController riftATransitionController;");
        code = "using RiftPrototypeA;\n" + code;
        code = RemoveLeadingDoc(code, "private IEnumerator LoadStageScene(");
        code = RemoveMethod(code, "private IEnumerator LoadStageScene(");
        code = RemoveLeadingDoc(code, "public void ConfirmSelection()");
        code = ReplaceMethod(code, "public void ConfirmSelection()", CONFIRM_METHOD);

        Directory.CreateDirectory(Path.GetDirectoryName(GENERATED_CONTROLLER));
        File.WriteAllText(GENERATED_CONTROLLER, code, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(GENERATED_CONTROLLER, ImportAssetOptions.ForceUpdate);
        Debug.Log($"方式A専用のControllerを生成しました: {GENERATED_CONTROLLER}。コンパイル完了後、手順2を実行してください。");
    }

    /// <summary>複製後に古いメソッド説明が残らないよう、直前のXMLコメントを取り除く。</summary>
    private static string RemoveLeadingDoc(string code, string signature)
    {
        int start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException($"メソッドが見つかりません: {signature}");
        int lineStart = code.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int cursor = lineStart;
        while (cursor > 0)
        {
            int previousEnd = cursor - 1;
            int previousStart = code.LastIndexOf('\n', Math.Max(0, previousEnd - 1)) + 1;
            string line = code.Substring(previousStart, previousEnd - previousStart).Trim();
            if (!line.StartsWith("///", StringComparison.Ordinal)) break;
            cursor = previousStart;
        }
        return code.Remove(cursor, lineStart - cursor);
    }

    /// <summary>メソッド先頭から対応する閉じ括弧までを取得する。対象2メソッド専用。</summary>
    private static (int Start, int End) FindMethodRange(string code, string signature)
    {
        int start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException($"メソッドが見つかりません: {signature}");
        int brace = code.IndexOf('{', start);
        if (brace < 0) throw new InvalidOperationException($"メソッドの開始括弧がありません: {signature}");
        int depth = 0;
        for (int i = brace; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            if (code[i] == '}') depth--;
            if (depth == 0) return (start, i + 1);
        }
        throw new InvalidOperationException($"メソッドの閉じ括弧がありません: {signature}");
    }

    private static string RemoveMethod(string code, string signature)
    {
        var range = FindMethodRange(code, signature);
        return code.Remove(range.Start, range.End - range.Start);
    }

    private static string ReplaceMethod(string code, string signature, string replacement)
    {
        var range = FindMethodRange(code, signature);
        return code.Substring(0, range.Start) + replacement + code.Substring(range.End);
    }

    [MenuItem("Tools/Rift Prototype A/2. Create Isolated Scenes")]
    public static void CreateIsolatedScenes()
    {
        string[] required = { SOURCE_SELECT_SCENE, SOURCE_DATABASE, SOURCE_STAGE_SCENE, GENERATED_CONTROLLER };
        foreach (string path in required)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"必要ファイルがありません: {path}。手順1を実行し、Unityのコンパイル完了を待ってください。");
                return;
            }
        }

        Directory.CreateDirectory(ROOT + "/Scenes");
        Directory.CreateDirectory(ROOT + "/Data");
        string oldControllerGuid = AssetDatabase.AssetPathToGUID(SOURCE_CONTROLLER);
        string newControllerGuid = AssetDatabase.AssetPathToGUID(GENERATED_CONTROLLER);
        string oldDatabaseGuid = AssetDatabase.AssetPathToGUID(SOURCE_DATABASE);
        if (string.IsNullOrEmpty(newControllerGuid))
            throw new InvalidOperationException("RiftAStageSelectControllerのGUID取得に失敗しました。");

        CopyAssetIfMissing(SOURCE_DATABASE, TARGET_DATABASE);
        CopyAssetIfMissing(SOURCE_STAGE_SCENE, TARGET_STAGE_SCENE);
        string newDatabaseGuid = AssetDatabase.AssetPathToGUID(TARGET_DATABASE);

        if (!File.Exists(TARGET_SELECT_SCENE))
        {
            if (!AssetDatabase.CopyAsset(SOURCE_SELECT_SCENE, TARGET_SELECT_SCENE))
                throw new InvalidOperationException("ステージセレクトの複製に失敗しました。");
            // 複製シーン内だけ、MonoScriptとDatabaseの参照を方式Aへ書き換える。
            string yaml = File.ReadAllText(TARGET_SELECT_SCENE);
            yaml = yaml.Replace("guid: " + oldControllerGuid + ",", "guid: " + newControllerGuid + ",");
            yaml = yaml.Replace("guid: " + oldDatabaseGuid + ",", "guid: " + newDatabaseGuid + ",");
            yaml = yaml.Replace("m_TargetAssemblyTypeName: StageSelectController, Assembly-CSharp",
                "m_TargetAssemblyTypeName: RiftAStageSelectController, Assembly-CSharp");
            yaml = yaml.Replace("Assembly-CSharp::StageSelectController",
                "Assembly-CSharp::RiftAStageSelectController");
            File.WriteAllText(TARGET_SELECT_SCENE, yaml);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        StageDatabase database = AssetDatabase.LoadAssetAtPath<StageDatabase>(TARGET_DATABASE);
        if (database == null || database.Areas == null || database.Areas.Length == 0)
            throw new InvalidOperationException("複製したStageDatabaseを取得できません。");
        database.Areas[0].sceneName = "RiftA_Stage1";
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();

        SetupStageScene();
        SetupSelectScene();
        Debug.Log("方式Aの独立シーンを生成しました。RiftA_Stage1のGameplayRootsを確認し、手順3でScene Listに登録してください。");
    }

    private static void CopyAssetIfMissing(string source, string target)
    {
        if (File.Exists(target)) return;
        if (!AssetDatabase.CopyAsset(source, target))
            throw new InvalidOperationException($"Asset複製に失敗しました: {source}");
    }

    private static void SetupStageScene()
    {
        Scene scene = EditorSceneManager.OpenScene(TARGET_STAGE_SCENE, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<RiftAStageEntry>(true) != null))
            return;

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(camera => camera.gameObject.scene == scene).ToArray();
        Camera gameCamera = cameras.FirstOrDefault(camera => camera.orthographic && camera.name.Contains("Main"))
                            ?? cameras.FirstOrDefault(camera => camera.orthographic)
                            ?? cameras.FirstOrDefault();
        if (gameCamera == null)
            throw new InvalidOperationException("複製したステージにCameraがありません。");

        GameObject root = new GameObject("RiftA_StageEntry");
        root.transform.SetParent(null);
        RiftAStageEntry entry = root.AddComponent<RiftAStageEntry>();
        Transform start = new GameObject("StartPoint").transform;
        start.SetParent(root.transform);
        start.SetPositionAndRotation(gameCamera.transform.position - gameCamera.transform.forward * 6f,
            gameCamera.transform.rotation);
        Transform end = new GameObject("EndPoint").transform;
        end.SetParent(root.transform);
        end.SetPositionAndRotation(gameCamera.transform.position + gameCamera.transform.forward * 4f,
            gameCamera.transform.rotation);

        AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(listener => listener.gameObject.scene == scene).ToArray();
        GameObject[] uiRoots = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(eventSystem => eventSystem.gameObject.scene == scene)
            .Select(eventSystem => eventSystem.gameObject)
            .Concat(UnityEngine.Object.FindObjectsByType<Canvas>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(canvas => canvas.gameObject.scene == scene)
                .Select(canvas => canvas.gameObject))
            .Distinct().ToArray();

        SerializedObject serialized = new SerializedObject(entry);
        serialized.FindProperty("startPoint").objectReferenceValue = start;
        serialized.FindProperty("endPoint").objectReferenceValue = end;
        serialized.FindProperty("gameplayCamera").objectReferenceValue = gameCamera;
        SetArray(serialized, "gameplayCameras", cameras);
        SetArray(serialized, "gameplayListeners", listeners);
        SetArray(serialized, "gameplayRoots", uiRoots);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupSelectScene()
    {
        Scene scene = EditorSceneManager.OpenScene(TARGET_SELECT_SCENE, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(root => root.name == "RiftA_TransitionRoot"))
            return;

        MonoBehaviour selectedController = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(mono => mono != null && mono.gameObject.scene == scene &&
                                    mono.GetType().Name == "RiftAStageSelectController");
        if (selectedController == null)
            throw new InvalidOperationException("RiftAStageSelectControllerがシーンに見つかりません。スクリプトのコンパイル結果を確認してください。");

        Canvas[] menuCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(canvas => canvas.gameObject.scene == scene).ToArray();
        Camera[] menuCameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(camera => camera.gameObject.scene == scene).ToArray();
        Light[] menuLights = UnityEngine.Object.FindObjectsByType<Light>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(light => light.gameObject.scene == scene).ToArray();
        AudioListener[] menuListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(listener => listener.gameObject.scene == scene).ToArray();
        EventSystem[] menuEventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(eventSystem => eventSystem.gameObject.scene == scene).ToArray();

        GameObject root = new GameObject("RiftA_TransitionRoot");
        RiftAStageLoader loader = root.AddComponent<RiftAStageLoader>();
        RiftAPreviewCamera previewCamera = root.AddComponent<RiftAPreviewCamera>();
        RiftATransitionController controller = root.AddComponent<RiftATransitionController>();

        GameObject overlay = new GameObject("RiftA_OverlayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(root.transform, false);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 5000;

        RawImage riftImage = CreateFullScreenGraphic<RawImage>("RiftA_RiftImage", overlay.transform);
        riftImage.color = Color.white;
        riftImage.raycastTarget = false;
        Image flash = CreateFullScreenGraphic<Image>("RiftA_FlashImage", overlay.transform);
        flash.color = new Color(1f, 1f, 1f, 0f);
        flash.raycastTarget = false;
        flash.transform.SetAsLastSibling();

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("riftShader").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Shader>(RIFT_SHADER);
        serialized.FindProperty("riftImage").objectReferenceValue = riftImage;
        serialized.FindProperty("flashImage").objectReferenceValue = flash;
        serialized.FindProperty("stageLoader").objectReferenceValue = loader;
        serialized.FindProperty("previewCamera").objectReferenceValue = previewCamera;
        SetArray(serialized, "menuCanvases", menuCanvases);
        SetArray(serialized, "menuCameras", menuCameras);
        SetArray(serialized, "menuLights", menuLights);
        SetArray(serialized, "menuListeners", menuListeners);
        SetArray(serialized, "menuEventSystems", menuEventSystems);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject selectSerialized = new SerializedObject(selectedController);
        SerializedProperty transitionProp = selectSerialized.FindProperty("riftATransitionController");
        if (transitionProp == null)
            throw new InvalidOperationException("方式Aコントローラーのフィールドが見つかりません。");
        transitionProp.objectReferenceValue = controller;
        selectSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static T CreateFullScreenGraphic<T>(string name, Transform parent) where T : Graphic
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return gameObject.GetComponent<T>();
    }

    private static void SetArray<T>(SerializedObject serialized, string propertyName, T[] elements)
        where T : UnityEngine.Object
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        property.arraySize = elements.Length;
        for (int i = 0; i < elements.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = elements[i];
    }

    [MenuItem("Tools/Rift Prototype A/3. Add Test Scenes To Scene List")]
    public static void RegisterSceneList()
    {
        AddToSceneList(TARGET_SELECT_SCENE, TARGET_STAGE_SCENE);
    }

    private static void AddToSceneList(params string[] paths)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        foreach (string path in paths)
        {
            if (scenes.All(scene => scene.path != path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("Scene Listを更新しました。Unity 6の有効なBuild Profileにも両シーンが含まれているか確認してください。");
    }
}
#endif
