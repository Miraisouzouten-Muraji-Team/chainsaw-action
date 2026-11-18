using UnityEngine;

namespace RiftPrototypeA
{
    /// <summary>方式A用タイトルに配置。タイトル表示後、最初のステージの先行読み込みを開始する。</summary>
    public sealed class RiftATitlePreloadBootstrap : MonoBehaviour
    {
        [SerializeField] private StageDatabase stageDatabase;
        [SerializeField, Min(0)] private int initialStageIndex;

        private void Start()
        {
            if (stageDatabase == null || stageDatabase.Areas == null || stageDatabase.Areas.Length == 0)
            {
                Debug.LogError("RiftA用のStageDatabaseを設定してください。", this);
                return;
            }
            int index = Mathf.Clamp(initialStageIndex, 0, stageDatabase.Areas.Length - 1);
            string sceneName = stageDatabase.Areas[index].sceneName;
            if (string.IsNullOrWhiteSpace(sceneName)) return;
            RiftAStagePreloadService.GetOrCreate().RequestPreload(sceneName);
        }
    }
}
