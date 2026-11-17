#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 描画用カメラを変更せず、追従・背景・シェイクを描画用カメラの同期より前に実行します。
/// Assets内のEditorフォルダーに配置してください。ビルド中の実行処理は追加しません。
/// </summary>
public static class CameraFollowExecutionOrderFix
{
    private const string SCRIPT_DIRECTORY = "Assets/_Sandbox/Muraji/Scripts/CameraScripts/";
    private const int FOLLOW_ORDER = -300;
    private const int PARALLAX_ORDER = -200;
    private const int SHAKE_ORDER = -100;

    /// <summary>スクリプト読み込み後に、必要な実行順を一度確認して設定します。</summary>
    [InitializeOnLoadMethod]
    private static void ScheduleExecutionOrderFix()
    {
        EditorApplication.delayCall += ApplyExecutionOrderFix;
    }

    /// <summary>既存3スクリプトの参照を確認し、追従・背景・シェイクの実行順だけを設定します。</summary>
    [MenuItem("Tools/Camera/追従と描画の実行順を修正")]
    public static void ApplyExecutionOrderFix()
    {
        // 再生中の設定変更を避け、通常の編集時だけ適用します。
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        MonoScript followScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            SCRIPT_DIRECTORY + "PlayerCamera.cs");
        MonoScript parallaxScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            SCRIPT_DIRECTORY + "ParallaxCameraRig.cs");
        MonoScript shakeScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            SCRIPT_DIRECTORY + "CameraShake_System.cs");

        // 一部だけ実行順が変わらないよう、対象がすべて存在することを先に確認します。
        if (followScript == null || parallaxScript == null || shakeScript == null)
        {
            Debug.LogError(
                "カメラ実行順の設定に失敗しました。" + SCRIPT_DIRECTORY +
                "にPlayerCamera.cs・ParallaxCameraRig.cs・CameraShake_System.csがあるか確認してください。");
            return;
        }

        // 描画側のPixelLayerRenderControllerは既存の実行順0のまま使用します。
        // 実行順の設定は.metaへ保存され、既存スクリプトの内容とInspector参照は維持します。
        bool changed = SetExecutionOrder(followScript, FOLLOW_ORDER);
        changed |= SetExecutionOrder(parallaxScript, PARALLAX_ORDER);
        changed |= SetExecutionOrder(shakeScript, SHAKE_ORDER);

        if (changed)
        {
            Debug.Log(
                "カメラ実行順を設定しました：PlayerCamera(-300) → " +
                "ParallaxLayerRig(-200) → CameraShake_System(-100) → " +
                "描画用カメラ同期(0)。");
        }
    }

    /// <summary>設定値が異なる場合だけ実行順を変更し、不要な再インポートを防ぎます。</summary>
    private static bool SetExecutionOrder(MonoScript script, int executionOrder)
    {
        if (MonoImporter.GetExecutionOrder(script) == executionOrder)
        {
            return false;
        }

        MonoImporter.SetExecutionOrder(script, executionOrder);
        return true;
    }
}
#endif
