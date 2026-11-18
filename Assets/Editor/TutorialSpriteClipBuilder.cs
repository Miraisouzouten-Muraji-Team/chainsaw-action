using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Project.Tutorial
{
    // 選択したスプライトの連番から、チュートリアル用のループクリップを作る。
    public static class TutorialSpriteClipBuilder
    {
        private const string MENU_PATH = "Assets/チュートリアル用AnimationClipを作成";
        private const float FRAME_RATE = 12f;

        // Projectで選択した画像を名前順に並べ、ルートのSpriteキーとして保存する。
        [MenuItem(MENU_PATH, false, 2000)]
        private static void CreateClip()
        {
            List<Sprite> frames = CollectSelectedFrames();
            if (frames.Count < 2)
            {
                EditorUtility.DisplayDialog("チュートリアル用クリップ",
                    "Projectウィンドウで連番スプライトを2枚以上選択してください。分割済みのスプライトシートも選べます。",
                    "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "チュートリアル用クリップを保存", "TutorialSpriteLoop", "anim",
                "保存先を指定してください。");
            if (string.IsNullOrEmpty(path)) return;

            // 元のクリップや既存の参照を守るため、必ず別のアセットとして保存する。
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var clip = new AnimationClip { frameRate = FRAME_RATE };
            var keys = new ObjectReferenceKeyframe[frames.Count + 1];
            for (int i = 0; i < frames.Count; i++)
            {
                keys[i] = new ObjectReferenceKeyframe { time = i / FRAME_RATE, value = frames[i] };
            }

            // 最後の画像も他と同じ時間だけ見えるよう、次の時刻をループの区切りにする。
            keys[frames.Count] = new ObjectReferenceKeyframe
            {
                time = frames.Count / FRAME_RATE,
                value = frames[0]
            };

            // チュートリアルは画像オブジェクト自体をルートとして読み込む。
            var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
        }

        // 選択したSpriteと画像アセットに含まれるSpriteを集め、番号順に並べる。
        private static List<Sprite> CollectSelectedFrames()
        {
            var unique = new HashSet<Sprite>();
            foreach (UnityEngine.Object asset in Selection.objects)
            {
                if (asset is Sprite sprite)
                {
                    if (EditorUtility.IsPersistent(sprite)) unique.Add(sprite);
                    continue;
                }

                if (!(asset is Texture2D)) continue;
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;

                foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (subAsset is Sprite frame) unique.Add(frame);
                }
            }

            var frames = new List<Sprite>(unique);
            frames.Sort(CompareFrames);
            return frames;
        }

        // 同名の画像がある場合も、ファイルのパスとアセット内IDで順番を固定する。
        private static int CompareFrames(Sprite first, Sprite second)
        {
            int order = EditorUtility.NaturalCompare(first.name, second.name);
            if (order != 0) return order;

            order = string.Compare(AssetDatabase.GetAssetPath(first),
                AssetDatabase.GetAssetPath(second), StringComparison.Ordinal);
            if (order != 0) return order;

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(first, out string _, out long firstId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(second, out string _, out long secondId);
            return firstId.CompareTo(secondId);
        }
    }
}
