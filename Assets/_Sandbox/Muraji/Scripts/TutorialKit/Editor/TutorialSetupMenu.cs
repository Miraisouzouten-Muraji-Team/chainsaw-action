#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Tutorial.Editor
{
    public static class TutorialSetupMenu
    {
        [MenuItem("Tools/Tutorial/4. Create accelerator and combat tutorials")]
        private static void CreateCombatSamples()
        {
            if (Application.isPlaying) { Debug.LogWarning("再生を停止して実行してください。"); return; }
            const string folder = "Assets/TutorialSamples";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "TutorialSamples");
            Vector3 origin = Selection.activeTransform != null ? Selection.activeTransform.position : Vector3.zero;
            CreateZone(folder, "Accelerator", origin, TutorialMode.InteractiveSlides,
                new TutorialPage { title = "アクセル", description = "ZRトリガーを長押しするとチェーンソーの<color=#FF7050>回転数</color>が上昇します。", demo = TutorialDemo.Accelerator, buttonLabel = "ZR", cycleSeconds = 5f, firstPress = .12f, customRelease = .6f },
                new TutorialPage { title = "食い込みボーナス", description = "回転速度が40を超えて食い込みを発動すると<color=#FF7050>ボーナス効果</color>を得ることが出来ます。", demo = TutorialDemo.FloorBonus, buttonLabel = "ZL", cycleSeconds = 4f, firstPress = .3f, landing = .85f });
            CreateZone(folder, "WeakCombo", origin + Vector3.right * 8, TutorialMode.Sign,
                new TutorialPage { title = "弱攻撃", description = "Xボタンを3回押すと、3連続で攻撃します。", demo = TutorialDemo.WeakCombo, buttonLabel = "X", cycleSeconds = 3f, firstPress = .15f, secondPress = .38f, thirdPress = .61f });
            CreateZone(folder, "EnemyDig", origin + Vector3.right * 16, TutorialMode.InteractiveSlides,
                new TutorialPage { title = "敵食い込み", description = "敵に食い込ませることも可能です。回転数に応じてダメージも変動する。", demo = TutorialDemo.EnemyDig, buttonLabel = "ZL", cycleSeconds = 4f, firstPress = .2f, landing = .85f },
                new TutorialPage { title = "空中の敵への食い込み", description = "空中の敵にも食い込みは有効です。ノックバックの方向に注意しましょう。", demo = TutorialDemo.AirEnemyDig, buttonLabel = "ZL", cycleSeconds = 4f, firstPress = .2f, landing = .85f });
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("アクセル2ページ・弱攻撃・敵食い込み2ページを追加しました。Zoneの配置とProjection Cameraを設定し、シーンを保存してください。");
        }

        [MenuItem("Tools/Tutorial/3. Create digging tutorials")]
        private static void CreateDiggingSamples()
        {
            if (Application.isPlaying) { Debug.LogWarning("再生を停止して実行してください。"); return; }
            const string folder = "Assets/TutorialSamples";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "TutorialSamples");
            Vector3 origin = Selection.activeTransform != null ? Selection.activeTransform.position : Vector3.zero;
            CreateZone(folder, "FloorDig", origin, TutorialMode.Sign,
                new TutorialPage { title = "床食い込み", description = "ZLを1回押して床に食い込みます。離した後も食い込みを続けます。", demo = TutorialDemo.FloorDig, buttonLabel = "ZL", cycleSeconds = 3f });
            CreateZone(folder, "WallDig", origin + Vector3.right * 8, TutorialMode.Sign,
                new TutorialPage { title = "壁食い込み", description = "壁際でZLを1回押して食い込みます。ジャンプからも食い込めます。", demo = TutorialDemo.WallDig, buttonLabel = "ZL", cycleSeconds = 3f, firstPress = .3f });
            CreateZone(folder, "WallKick", origin + Vector3.right * 16, TutorialMode.Sign,
                new TutorialPage { title = "壁キック", description = "壁に食い込んだ状態でAを押すと、壁を蹴ってジャンプします。", demo = TutorialDemo.WallKick, buttonLabel = "A", cycleSeconds = 3f, firstPress = .25f });
            CreateZone(folder, "CeilingDig", origin + Vector3.right * 24, TutorialMode.Sign,
                new TutorialPage { title = "天井食い込み", description = "天井に近づいてZLを1回押すと、天井に食い込みます。", demo = TutorialDemo.CeilingDig, buttonLabel = "ZL", cycleSeconds = 3f, firstPress = .35f });
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("4種類の看板を追加しました。位置とProjection Cameraを設定してシーンを保存してください。既存のPlayer設定をそのまま使えます。");
        }

        [MenuItem("Tools/Tutorial/1. Create sample tutorials")]
        private static void CreateSamples()
        {
            if (Application.isPlaying) { Debug.LogWarning("再生を停止して実行してください。"); return; }
            const string folder = "Assets/TutorialSamples";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "TutorialSamples");
            Vector3 origin = Selection.activeTransform != null ? Selection.activeTransform.position : Vector3.zero;
            CreateZone(folder, "Move", origin, TutorialMode.Sign,
                new TutorialPage { title = "移動", description = "Lスティックを左右に倒すと移動します。", demo = TutorialDemo.Move });
            CreateZone(folder, "Jump", origin + Vector3.right * 8, TutorialMode.Sign,
                new TutorialPage { title = "ジャンプ", description = "Aボタンでジャンプします。", demo = TutorialDemo.Jump });
            CreateZone(folder, "DoubleJump", origin + Vector3.right * 16, TutorialMode.Sign,
                new TutorialPage { title = "2段ジャンプ", description = "空中でもう一度Aボタンを押すと、2段ジャンプします。", demo = TutorialDemo.DoubleJump });
            CreateZone(folder, "Detail", origin + Vector3.right * 24, TutorialMode.InteractiveSlides,
                new TutorialPage { title = "詳細説明：ページ1", description = "ここに食い込みやボーナスの説明を設定してください。\n左スティックを左右に倒すとページを切り替えます。", demo = TutorialDemo.Custom, buttonLabel = "X" },
                new TutorialPage { title = "詳細説明：ページ2", description = "図や連番アニメーションをページごとに登録できます。", demo = TutorialDemo.Custom, buttonLabel = "X" });
            if (UnityEngine.Object.FindFirstObjectByType<TutorialPresenter>() == null)
            {
                var presenter = new GameObject("Tutorial Presenter");
                Undo.RegisterCreatedObjectUndo(presenter, "Create Tutorial Presenter");
                presenter.AddComponent<TutorialPresenter>();
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("サンプルを作成しました。次にPlayerのルートを選択し Tools/Tutorial/2 を実行してください。各ZoneのProjection Cameraに実際の3D Cameraを設定してください。");
        }

        private static void CreateZone(string folder, string name, Vector3 position, TutorialMode mode, params TutorialPage[] pages)
        {
            var data = ScriptableObject.CreateInstance<TutorialDefinition>();
            data.mode = mode; data.pages = pages;
            AssetDatabase.CreateAsset(data, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
            var go = new GameObject("Tutorial " + name);
            Undo.RegisterCreatedObjectUndo(go, "Create Tutorial Zone");
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider>(); collider.isTrigger = true;
            collider.size = new Vector3(5, 4, 6); collider.center = Vector3.up;
            var zone = go.AddComponent<TutorialZone>(); zone.definition = data;
            zone.anchorOffset = new Vector3(0, 3.5f, 0);
        }

        [MenuItem("Tools/Tutorial/2. Setup selected player")]
        private static void SetupPlayer()
        {
            if (Application.isPlaying || Selection.activeGameObject == null)
            { Debug.LogWarning("再生停止中にPlayerのルートGameObjectを選択してください。"); return; }
            var root = Selection.activeGameObject;
            var player = root.GetComponent<TutorialPlayer>();
            if (player == null) player = Undo.AddComponent<TutorialPlayer>(root);
            Undo.RecordObject(player, "Setup Tutorial Player");
            var inputs = new List<Behaviour>();
            player.clearBufferedInput = new UnityEvent();
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                string name = behaviour.GetType().Name;
                if (name != "PlayerInputHandler" && name != "PlayerInput") continue;
                inputs.Add(behaviour);
                var method = behaviour.GetType().GetMethod("ResetInput", Type.EmptyTypes);
                if (method == null || method.ReturnType != typeof(void)) continue;
                var action = Delegate.CreateDelegate(typeof(UnityAction), behaviour, method, false) as UnityAction;
                if (action != null) UnityEventTools.AddPersistentListener(player.clearBufferedInput, action);
            }
            player.inputBehaviours = inputs.ToArray();
            EditorUtility.SetDirty(player);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            if (root.scene.IsValid()) EditorSceneManager.MarkSceneDirty(root.scene);
            if (inputs.Count == 0)
                Debug.LogWarning("入力コンポーネントが見つかりません。TutorialPlayerのInput BehavioursとClear Buffered Inputを手動設定してください。", player);
            else Debug.Log("TutorialPlayer設定完了。シーン保存と、必要ならPlayerプレハブへのApplyを行ってください。", player);
        }
    }
}
#endif
