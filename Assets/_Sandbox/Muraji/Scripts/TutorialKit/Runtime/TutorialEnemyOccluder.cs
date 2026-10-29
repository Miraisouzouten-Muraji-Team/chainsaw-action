using System.Collections.Generic;
using UnityEngine;

namespace Project.Tutorial
{
    // 敵を看板の切り抜き対象として登録する。敵プレハブのルートに追加する。
    [DisallowMultipleComponent]
    public sealed class TutorialEnemyOccluder : MonoBehaviour
    {
        [Tooltip("看板より手前に見せる敵本体・武器のRenderer。空の場合は子のRendererを自動取得します。エフェクトは登録しないでください。")]
        public Renderer[] tutorialOccluders = new Renderer[0];
        internal static readonly HashSet<TutorialEnemyOccluder> Active = new HashSet<TutorialEnemyOccluder>();
        private readonly List<Renderer> childRenderers = new List<Renderer>();

        // プレイ開始時に以前の登録を消去する。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active.Clear(); }

        // 生成時・プールからの復帰時に対象へ登録する。
        private void OnEnable() { Active.Add(this); }

        // 非表示・破棄・シーン移動時に対象から除外する。
        private void OnDisable() { Active.Remove(this); }

        // 現在の敵の描画対象を収集する。子の追加・差し替えにも対応する。
        internal void CollectRenderers(HashSet<Renderer> destination)
        {
            if (tutorialOccluders != null && tutorialOccluders.Length > 0)
            {
                bool hasValidRenderer = false;
                foreach (Renderer renderer in tutorialOccluders)
                {
                    if (renderer == null) continue;
                    destination.Add(renderer);
                    hasValidRenderer = true;
                }
                if (hasValidRenderer) return;
            }
            childRenderers.Clear();
            GetComponentsInChildren<Renderer>(true, childRenderers);
            foreach (Renderer renderer in childRenderers)
                if (renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    destination.Add(renderer);
        }
    }
}
