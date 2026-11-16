using DG.Tweening;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの仮モデル用武器モーションを管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackVisualController : MonoBehaviour
    {
        [Header("基本参照")]
        [SerializeField] private Transform weaponPivot;
        [SerializeField] private GameObject shieldReturnCue;

        [Header("武器の基準")]
        [SerializeField] private Vector3 localAttackDirection = Vector3.left;
        [SerializeField, Min(0f)] private float thrustDistance = 2f;
        [SerializeField, Min(0f)] private float thrustPullbackDistance = 0.5f;

        [Header("縦切り")]
        [SerializeField] private Transform verticalSlashPivotAnchor;
        [SerializeField] private Transform verticalSlashDropEndAnchor;
        [SerializeField] private BossVerticalSlashRange verticalSlashRange;

        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialBladePosition;
        private Vector3 initialBladeScale;
        private Vector3 currentThrustDirection;
        private Quaternion slashBaseRotation;

        private Transform bladeCube;
        private BossAttackHitboxController hitboxController;
        private Sequence currentSequence;

        private void Awake()
        {
            if (weaponPivot == null)
            {
                Debug.LogError(
                    "BossAttackVisualController: WeaponPivotが未設定です。",
                    this);
                enabled = false;
                return;
            }

            hitboxController = GetComponent<BossAttackHitboxController>();
            bladeCube = hitboxController != null
                ? hitboxController.WeaponRangeSource
                : null;

            initialPosition = weaponPivot.localPosition;
            initialRotation = weaponPivot.localRotation;

            if (bladeCube != null)
            {
                initialBladePosition = bladeCube.localPosition;
                initialBladeScale = bladeCube.localScale;
            }

            localAttackDirection = localAttackDirection.sqrMagnitude < 0.0001f
                ? Vector3.left
                : localAttackDirection.normalized;

            currentThrustDirection = initialRotation * localAttackDirection;
            slashBaseRotation = initialRotation;

            SetShieldReturnCue(false);
        }

        /// <summary>
        /// 突きの予備動作として武器を後ろへ引く。
        /// </summary>
        public void PlayThrustPreparation(float duration)
        {
            ResetPose();

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition -
                    localAttackDirection * thrustPullbackDistance,
                    Mathf.Max(0.01f, duration)).SetEase(Ease.InOutSine))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// プレイヤー位置へ武器を向ける。
        /// </summary>
        public void AimThrustAt(Vector3 targetPosition)
        {
            currentSequence?.Kill();
            currentSequence = null;

            Vector3 worldDirection = targetPosition - weaponPivot.position;
            worldDirection.z = 0f;

            if (worldDirection.sqrMagnitude < 0.0001f)
                return;

            currentThrustDirection =
                weaponPivot.parent != null
                    ? weaponPivot.parent.InverseTransformDirection(
                        worldDirection).normalized
                    : worldDirection.normalized;

            Quaternion currentBase = initialRotation;

            weaponPivot.localRotation =
                Quaternion.FromToRotation(
                    currentBase * localAttackDirection,
                    currentThrustDirection) * currentBase;

            weaponPivot.localPosition =
                initialPosition -
                currentThrustDirection * thrustPullbackDistance;
        }

        /// <summary>
        /// 突きの速度倍率を反映して武器を突き出す。
        /// </summary>
        public void PlayThrust(float duration, float animationSpeed)
        {
            currentSequence?.Kill();

            float total = Mathf.Max(0.05f, duration);
            float speed = Mathf.Max(0.05f, animationSpeed);
            float extend = total * Mathf.Clamp(
                0.25f / speed, 0.05f, 0.4f);
            float returnTime = total * 0.25f;
            float hold = total - extend - returnTime;

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition +
                    currentThrustDirection * thrustDistance,
                    extend).SetEase(Ease.OutCubic))
                .AppendInterval(hold)
                .Append(weaponPivot.DOLocalMove(
                    initialPosition,
                    returnTime).SetEase(Ease.InOutSine))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }


        /// <summary>
        /// 肩付近の回転中心へ移動し、刃を頭上へ振り上げる。
        /// </summary>
        public void PlayVerticalSlashPreparation(float duration, float raiseAngle)
        {
            if (weaponPivot == null)
                return;

            ResetPose();
            SetupVerticalSlashPose();

            Quaternion baseRotation = slashFrontRotation;
            weaponPivot.localRotation = baseRotation;

            currentSequence = DOTween.Sequence()
                .Append(DOVirtual.Float(
                    0f,
                    raiseAngle,
                    Mathf.Max(0.01f, duration),
                    angle =>
                    {
                        if (weaponPivot != null)
                        {
                            weaponPivot.localRotation =
                                baseRotation * Quaternion.Euler(0f, 0f, angle);
                        }
                    }).SetEase(Ease.OutSine))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 半円状に振った後、その位置から真下へ切り下ろす。
        /// </summary>
        public void PlayVerticalSlash(
            float duration,
            float raiseAngle,
            float finishAngle,
            float animationSpeed)
        {
            // 予備動作の位置を維持する。ResetPoseは呼ばない。
            currentSequence?.Kill();
            currentSequence = null;

            weaponPivot.localRotation =
                slashBaseRotation *
                Quaternion.Euler(0f, 0f, raiseAngle);

            float total = Mathf.Max(0.05f, duration);
            float speed = Mathf.Max(0.05f, animationSpeed);

            float swingDuration = total *
                Mathf.Clamp(0.65f / speed, 0.45f, 0.85f);
            float dropDuration = total - swingDuration;

            Vector3 dropPosition = GetSlashDropPosition();

            currentSequence = DOTween.Sequence()
                .Append(DOVirtual.Float(
                    raiseAngle,
                    finishAngle,
                    swingDuration,
                    angle =>
                    {
                        if (weaponPivot != null)
                        {
                            weaponPivot.localRotation =
                                slashBaseRotation *
                                Quaternion.Euler(0f, 0f, angle);
                        }
                    }).SetEase(Ease.InCubic))
                .Append(weaponPivot.DOLocalMove(
                    dropPosition,
                    dropDuration).SetEase(Ease.InCubic))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 攻撃終了後の硬直中に通常姿勢へ戻す。
        /// </summary>
        public void PlayVerticalSlashRecovery(float duration)
        {
            currentSequence?.Kill();

            float recovery = Mathf.Max(0.01f, duration);

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition,
                    recovery).SetEase(Ease.OutSine))
                .Join(weaponPivot.DOLocalRotateQuaternion(
                    initialRotation,
                    recovery).SetEase(Ease.OutSine))
                .AppendCallback(ResetBladePose)
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 任意のアンカー位置を武器Pivotの親座標に変換する。
        /// </summary>
        private void SetPivotPosition(Transform anchor)
        {
            if (anchor == null)
                return;

            weaponPivot.localPosition =
                weaponPivot.parent != null
                    ? weaponPivot.parent.InverseTransformPoint(
                        anchor.position)
                    : anchor.position;
        }

        /// <summary>
        /// 表示範囲で指定した正面方向へ刃を向ける。
        /// </summary>
        private void CalculateSlashBaseRotation()
        {
            slashBaseRotation = initialRotation;

            if (verticalSlashRange == null)
                return;

            Vector3 worldForward =
                verticalSlashRange.transform.TransformDirection(
                    verticalSlashRange.LocalForward);

            Vector3 localForward = weaponPivot.parent != null
                ? weaponPivot.parent.InverseTransformDirection(
                    worldForward)
                : worldForward;

            localForward.z = 0f;

            if (localForward.sqrMagnitude < 0.0001f)
                return;

            slashBaseRotation =
                Quaternion.FromToRotation(
                    initialRotation * localAttackDirection,
                    localForward.normalized) * initialRotation;
        }

        /// <summary>
        /// 半径に応じて仮の刃の長さを変更する。
        /// </summary>
        private void ConfigureSlashBlade()
        {
            if (bladeCube == null || verticalSlashRange == null)
                return;

            float length = verticalSlashRange.Radius;

            Vector3 scale = initialBladeScale;
            scale.x = length;
            bladeCube.localScale = scale;

            bladeCube.localPosition =
                localAttackDirection * length * 0.5f;

            hitboxController?.SynchronizeWeaponHitbox();
        }

        /// <summary>
        /// 真下への切り下ろし終点を取得する。
        /// </summary>
        private Vector3 GetSlashDropPosition()
        {
            if (verticalSlashDropEndAnchor != null)
            {
                return weaponPivot.parent != null
                    ? weaponPivot.parent.InverseTransformPoint(
                        verticalSlashDropEndAnchor.position)
                    : verticalSlashDropEndAnchor.position;
            }

            float distance = verticalSlashRange != null
                ? verticalSlashRange.DropDistance
                : 2f;

            Vector3 localDown = weaponPivot.parent != null
                ? weaponPivot.parent.InverseTransformVector(
                    Vector3.down * distance)
                : Vector3.down * distance;

            return weaponPivot.localPosition + localDown;
        }

        private void ResetBladePose()
        {
            if (bladeCube == null)
                return;

            bladeCube.localPosition = initialBladePosition;
            bladeCube.localScale = initialBladeScale;
            hitboxController?.SynchronizeWeaponHitbox();
        }

        public void SetShieldReturnCue(bool visible)
        {
            if (shieldReturnCue != null)
                shieldReturnCue.SetActive(visible);
        }

        /// <summary>
        /// 中断時も含め、武器を通常姿勢に戻す。
        /// </summary>
        public void ResetPose()
        {
            currentSequence?.Kill();
            currentSequence = null;

            if (weaponPivot == null)
                return;

            weaponPivot.localPosition = initialPosition;
            weaponPivot.localRotation = initialRotation;
            currentThrustDirection =
                initialRotation * localAttackDirection;

            ResetBladePose();
            SetShieldReturnCue(false);
        }

        private void OnDisable()
        {
            ResetPose();
        }
    }
}
