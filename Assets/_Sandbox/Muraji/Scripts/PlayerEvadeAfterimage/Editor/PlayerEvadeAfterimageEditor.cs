using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(PlayerEvadeAfterimage))]
public sealed class PlayerEvadeAfterimageEditor : Editor
{
    // 参照・発生間隔・色を日本語で表示し、対応マテリアルの作成を案内します。
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawField("chainsawDigging", "食い込み処理");
        DrawField("modelRoot", "プレイヤーの見た目の親");
        DrawField("sourceRenderers", "描画対象の個別指定（通常は空）");
        DrawField("afterimageMaterial", "残像マテリアル");
        DrawField("spawnInterval", "残像を出す間隔（秒）");
        DrawField("lifetime", "残像が消えるまで（秒）");
        DrawField("maximumAfterimages", "残像の最大数");
        DrawField("minimumDistance", "生成に必要な移動距離");
        DrawField("minimumAngle", "生成に必要な向きの変化（度）");
        DrawField("freshColor", "新しい残像の色");
        DrawField("fadedColor", "消える直前の色");
        SerializedProperty layer = serializedObject.FindProperty("afterimageLayer");
        layer.intValue = EditorGUILayout.LayerField(new GUIContent("残像のレイヤー", layer.tooltip), layer.intValue);
        using (new EditorGUI.DisabledScope(true))
        {
            DrawField("isEvading", "現在の回避判定");
            DrawField("visibleAfterimages", "表示中の残像数");
        }
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.HelpBox("見た目の親を設定して、下のボタンでマテリアルを作成してください。既存のIsEvadingが有効な間だけ発生します。", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("残像マテリアルを作成して設定")) CreateMaterial();
            Material assigned = serializedObject.FindProperty("afterimageMaterial").objectReferenceValue as Material;
            using (new EditorGUI.DisabledScope(assigned == null || Shader.Find("Universal Render Pipeline/Lit") == null))
            {
                if (GUILayout.Button("設定済みマテリアルをURP/Litの残像用に変更")) ConvertMaterial(assigned);
            }
        }
    }

    // シリアライズされた値を、日本語の説明を保って表示します。
    private void DrawField(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
    }

    // プロジェクトの描画方式に合うシェーダーを選択し、保存したマテリアルを割り当てます。
    private void CreateMaterial()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        bool isUniversal = pipeline != null && pipeline.GetType().Name.Contains("Universal");
        if (pipeline != null && !isUniversal)
        {
            Debug.LogError("回避残像：自動マテリアル作成はBuilt-inまたはURP向けです。現在の描画方式には対応していません。", target);
            return;
        }
        string materialPath = EditorUtility.SaveFilePanelInProject("残像マテリアルの保存先", "PlayerEvadeAfterimage", "mat", "保存先を選択してください。");
        if (string.IsNullOrEmpty(materialPath)) return;
        materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);

        Shader shader = Shader.Find(isUniversal
            ? "Universal Render Pipeline/Lit"
            : "Custom/Player Evade Afterimage");

        if (shader == null)
        {
            Debug.LogError("回避残像：シェーダーを取得できません。Consoleのシェーダーエラーと同梱ファイルを確認してください。", target);
            return;
        }
        Material material = new Material(shader);
        PlayerEvadeAfterimage.ConfigureUrpLit(material);
        AssetDatabase.CreateAsset(material, materialPath);
        serializedObject.Update();
        serializedObject.FindProperty("afterimageMaterial").objectReferenceValue = material;
        serializedObject.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    // 明示ボタンでだけ既存アセットを変更。Undoに対応します。
    private void ConvertMaterial(Material material)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null || shader == null) return;
        Undo.RecordObject(material, "Configure Afterimage URP Lit");
        material.shader = shader;
        PlayerEvadeAfterimage.ConfigureUrpLit(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

}
