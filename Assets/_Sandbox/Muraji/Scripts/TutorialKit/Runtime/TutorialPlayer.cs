using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Tutorial
{
    [DisallowMultipleComponent]
    public sealed class TutorialPlayer : MonoBehaviour
    {
        [Header("看板をプレイヤーの後ろに表示")]
        public bool hideTutorialBehindPlayer = true;
        [Tooltip("空なら子のMeshRenderer / SkinnedMeshRendererを自動取得。武器を含められます。残像等を除外したい場合は明示登録。")]
        public Renderer[] tutorialOccluders = new Renderer[0];
        [Tooltip("マスク位置が上下反転する環境のみON。")]
        public bool flipTutorialMaskY;
        [Tooltip("詳細説明中に無効化する入力スクリプト。PlayerInputHandler等を登録。GameObject本体は無効化しない。")]
        public Behaviour[] inputBehaviours = new Behaviour[0];
        [Tooltip("入力バッファの破棄など。PlayerInputHandler.ResetInputを登録してください。")]
        public UnityEvent clearBufferedInput = new UnityEvent();
        private readonly Dictionary<Behaviour, bool> states = new Dictionary<Behaviour, bool>();
        private bool suspended;

        public void SuspendInput()
        {
            if (suspended) return;
            suspended = true;
            clearBufferedInput.Invoke();
            states.Clear();
            foreach (var item in inputBehaviours)
            {
                if (item == null || item == this || states.ContainsKey(item)) continue;
                states.Add(item, item.enabled);
                item.enabled = false;
            }
        }

        public void RestoreInput()
        {
            if (!suspended) return;
            suspended = false;
            clearBufferedInput.Invoke();
            foreach (var pair in states)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            states.Clear();
        }

        private void OnDisable() { RestoreInput(); }
    }
}
