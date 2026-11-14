
using DG.Tweening;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 仮モデルの武器モーションをDOTweenで再生する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackVisualController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private Transform weaponPivot;

        [Header("突き")]
        [SerializeField, Min(0f)] private float thrustDistance = 2f;

        [Header("縦振り")]
        [SerializeField] private float slashStartAngle = 70f;
        [SerializeField] private float slashEndAngle = -70f;

        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Sequence currentSequence;

        private void Awake()
        {
            if (weaponPivot == null)
            {
                Debug.LogError("BossAttackVisualController: WeaponPivotが未設定です。", this);
                enabled = false;
                return;
            }

            initialPosition = weaponPivot.localPosition;
            initialRotation = weaponPivot.localRotation;
        }

        /// <summary>
        /// チェーンソーを前方へ突き出して戻す。
        /// </summary>
        public void PlayThrust(float duration)
        {
            if (weaponPivot == null)
                return;

            ResetPose();

            float totalDuration = Mathf.Max(0.05f, duration);
            float extendDuration = totalDuration * 0.25f;
            float returnDuration = totalDuration * 0.25f;
            float holdDuration = totalDuration - extendDuration - returnDuration;

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition + Vector3.right * thrustDistance,
                    extendDuration).SetEase(Ease.OutCubic))
                .AppendInterval(holdDuration)
                .Append(weaponPivot.DOLocalMove(
                    initialPosition, returnDuration).SetEase(Ease.InOutSine));
        }

        /// <summary>
        /// チェーンソーを上から下へ振り下ろす。
        /// </summary>
        public void PlayVerticalSlash(float duration)
        {
            if (weaponPivot == null)
                return;

            ResetPose();

            float totalDuration = Mathf.Max(0.05f, duration);

            weaponPivot.localRotation =
                initialRotation * Quaternion.Euler(0f, 0f, slashStartAngle);

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalRotateQuaternion(
                    initialRotation * Quaternion.Euler(0f, 0f, slashEndAngle),
                    totalDuration * 0.75f).SetEase(Ease.InCubic))
                .Append(weaponPivot.DOLocalRotateQuaternion(
                    initialRotation,
                    totalDuration * 0.25f).SetEase(Ease.OutSine));
        }

        /// <summary>
        /// 攻撃の途中中断時も元の姿勢へ戻す。
        /// </summary>
        public void ResetPose()
        {
            currentSequence?.Kill();
            currentSequence = null;

            if (weaponPivot == null)
                return;

            weaponPivot.localPosition = initialPosition;
            weaponPivot.localRotation = initialRotation;
        }

        private void OnDisable()
        {
            ResetPose();
        }
    }
}
