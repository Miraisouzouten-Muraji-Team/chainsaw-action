using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Animation Eventをボスのモーション制御へ転送する。
    /// Animatorと同じGameObjectに配置する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class BossAnimationEventReceiver : MonoBehaviour
    {
        [SerializeField]
        private BossAnimationController animationController;

        private Animator animator;

        private void Awake()
        {
            animator = GetComponent<Animator>();

            if (animationController == null)
            {
                animationController =
                    GetComponentInParent<BossAnimationController>();
            }
        }

        /// <summary>
        /// Animation Clipからイベントを受信する。
        /// Int Parameterで通知の種類を指定する。
        /// </summary>
        public void OnBossAnimationEvent(AnimationEvent animationEvent)
        {
            animationController?.ReceiveEvent(
                animator,
                animationEvent);
        }
    }
}
