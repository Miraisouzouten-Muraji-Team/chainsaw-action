#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using RiftPrototypeA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>方式A用のタイトル複製とガラス割れ参照設定。元のシーンは変更しない。</summary>
public static class RiftAPrototypePreloadUpgrade
{
    private const string ROOT = "Assets/_Sandbox/Akane/RiftPrototype";
    private const string SOURCE_TITLE = "Assets/_Project/Scenes/TitleScene.unity";
    private const string TARGET_TITLE = ROOT + "/Scenes/RiftA_Title.unity";
    private const string SELECT_SCENE = ROOT + "/Scenes/RiftA_StageSelect.unity";
    private const string STAGE_SCENE = ROOT + "/Scenes/RiftA_Stage1.unity";
    private const string DATABASE_ASSET = ROOT + "/Data/RiftA_StageDatabase.asset";
    private const string GLASS_MASK = ROOT + "/Shaders/RiftA_GlassCracks.png";
    private const string GLASS_SHADER = ROOT + "/Shaders/RiftAScreenSpace.shader";

    [MenuItem("Tools/Rift Prototype A/4. Setup Early Preload And Glass Crack")]
    public static void SetupEarlyPreloadAndGlassCrack()
    {
        if (!File.Exists(SELECT_SCENE) || !File.Exists(STAGE_SCENE) ||
            !File.Exists(DATABASE_ASSET) || !File.Exists(SOURCE_TITLE))
        {
            Debug.LogError("方式AのシーンとDatabaseを先に生成してください。元のタイトルも必要です。");
            return;
        }

        StageDatabase database = AssetDatabase.LoadAssetAtPath<StageDatabase>(DATABASE_ASSET);
        Texture2D crackMask = AssetDatabase.LoadAssetAtPath<Texture2D>(GLASS_MASK);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GLASS_SHADER);
        if (database == null || crackMask == null || shader == null)
            throw new InvalidOperationException("StageDatabase、亀裂画像またはシェーダーを読み込めません。");

        // タイトルは方式A専用に一度だけ複製する。元のTitleSceneには手を加えない。
        if (!File.Exists(TARGET_TITLE) && !AssetDatabase.CopyAsset(SOURCE_TITLE, TARGET_TITLE))
            throw new InvalidOperationException("タイトルシーンを複製できません。");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        Scene titleScene = EditorSceneManager.OpenScene(TARGET_TITLE, OpenSceneMode.Single);
        GameObject titleEntry = titleScene.GetRootGameObjects().FirstOrDefault(root => root.name == "RiftA_TitleEntry");
        if (titleEntry == null) titleEntry = new GameObject("RiftA_TitleEntry");
        RiftATitlePreloadBootstrap bootstrap = titleEntry.GetComponent<RiftATitlePreloadBootstrap>();
        if (bootstrap == null) bootstrap = titleEntry.AddComponent<RiftATitlePreloadBootstrap>();
        RiftATitleToSelectNavigator navigator = titleEntry.GetComponent<RiftATitleToSelectNavigator>();
        if (navigator == null) navigator = titleEntry.AddComponent<RiftATitleToSelectNavigator>();

        SerializedObject bootstrapSerialized = new SerializedObject(bootstrap);
        bootstrapSerialized.FindProperty("stageDatabase").objectReferenceValue = database;
        bootstrapSerialized.FindProperty("initialStageIndex").intValue = 0;
        bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject navigatorSerialized = new SerializedObject(navigator);
        navigatorSerialized.FindProperty("stageSelectSceneName").stringValue = "RiftA_StageSelect";
        navigatorSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(titleScene);
        EditorSceneManager.SaveScene(titleScene);

        Scene selectScene = EditorSceneManager.OpenScene(SELECT_SCENE, OpenSceneMode.Single);
        RiftATransitionController transition = selectScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<RiftATransitionController>(true))
            .FirstOrDefault();
        if (transition == null)
            throw new InvalidOperationException("RiftA_StageSelectにRiftATransitionControllerがありません。");
        SerializedObject transitionSerialized = new SerializedObject(transition);
        transitionSerialized.FindProperty("glassCrackMask").objectReferenceValue = crackMask;
        transitionSerialized.FindProperty("riftShader").objectReferenceValue = shader;
        // 以前のインストーラー実行時に参照が欠けていた場合は、既存の子を検索して復旧する。
        GameObject transitionRoot = transition.gameObject;
        var rawImages = transitionRoot.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
        var flashImages = transitionRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        RiftAPreviewCamera preview = transitionRoot.GetComponent<RiftAPreviewCamera>();
        if (transitionSerialized.FindProperty("riftImage").objectReferenceValue == null)
            transitionSerialized.FindProperty("riftImage").objectReferenceValue =
                rawImages.FirstOrDefault(image => image.name == "RiftA_RiftImage");
        if (transitionSerialized.FindProperty("flashImage").objectReferenceValue == null)
            transitionSerialized.FindProperty("flashImage").objectReferenceValue =
                flashImages.FirstOrDefault(image => image.name == "RiftA_FlashImage");
        if (transitionSerialized.FindProperty("previewCamera").objectReferenceValue == null)
            transitionSerialized.FindProperty("previewCamera").objectReferenceValue = preview;
        transitionSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(selectScene);
        EditorSceneManager.SaveScene(selectScene);

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        foreach (string path in new[] { TARGET_TITLE, SELECT_SCENE, STAGE_SCENE })
        {
            if (scenes.All(scene => scene.path != path))
                scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }
        EditorBuildSettings.scenes = scenes;
        Debug.Log("方式Aのタイトルとガラス亀裂を設定しました。" +
            "RiftA_Titleのステージ選択ボタンのOnClickは手動でNavigator.OpenStageSelectに変更してください。");
    }
}
#endif
