using DG.Tweening;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// DOTweenによる攻撃予告の表示を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackTelegraph : MonoBehaviour
    {
        [Header("予告表示")]
        [SerializeField] private Transform indicator;
        [SerializeField, Range(0.1f, 1f)]
        private float startScaleRatio = 0.5f;

        private Vector3 initialScale;
        private Tween currentTween;

        private void Awake()
        {
            if (indicator == null) { return; }

            initialScale = indicator.localScale;
            indicator.gameObject.SetActive(false);
        }

        /// <summary>
        /// 攻撃予告を表示し、予備動作の時間に合わせて拡大する。
        /// </summary>
        public void Show(float preparationTime)
        {
            if (indicator == null) { return; }

            currentTween?.Kill();
            currentTween = null;

            indicator.gameObject.SetActive(true);
            indicator.localScale = initialScale * startScaleRatio;

            if (preparationTime <= 0f)
            {
                indicator.localScale = initialScale;
                return;
            }

            currentTween = indicator.DOScale(initialScale, preparationTime).SetEase(Ease.OutQuad);
        }

        /// <summary>
        /// 攻撃予告を終了する。
        /// </summary>
        public void Hide()
        {
            currentTween?.Kill();
            currentTween = null;

            if (indicator == null) { return; }

            indicator.localScale = initialScale;
            indicator.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            Hide();
        }
    }
}
