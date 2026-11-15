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
        [SerializeField] private Transform verticalSlashRangeSource;
        [SerializeField] private GameObject shieldReturnCue;

        [Header("攻撃方向")]
        [Tooltip("モデル正面に合わせたローカル攻撃方向")]
        [SerializeField] private Vector3 localAttackDirection = Vector3.left;

        [Header("突き")]
        [SerializeField, Min(0f)] private float thrustDistance = 2f;
        [SerializeField, Min(0f)] private float thrustPullbackDistance = 0.5f;

        [Header("縦振り")]
        [Tooltip("縦切り判定が未設定の場合に使う切り下ろし距離")]
        [SerializeField, Min(0f)] private float slashDropDistance = 2f;

        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 currentThrustDirection;
        private Sequence currentSequence;
        private Transform bladeCube;
        private Vector3 initialBladePosition;
        private Vector3 initialBladeScale;
        private BossAttackHitbox verticalSlashHitbox;
        private Quaternion slashFrontRotation;

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
            if (TryGetComponent(out BossAttackHitboxController hitboxController))
            {
                verticalSlashRangeSource ??= hitboxController.VerticalSlashRangeSource;
                bladeCube = hitboxController.WeaponRangeSource;
            }

            if (bladeCube != null)
            {
                initialBladePosition = bladeCube.localPosition;
                initialBladeScale = bladeCube.localScale;
            }
            localAttackDirection = localAttackDirection.sqrMagnitude <= 0.0001f
                ? Vector3.left
                : localAttackDirection.normalized;
            currentThrustDirection = initialRotation * localAttackDirection;
            SetShieldReturnCue(false);
        }


        /// <summary>
        /// 仮モデルの刃と命中Colliderの形状を一致させる。
        /// </summary>
        private void SynchronizeWeaponCollider()
        {
            if (TryGetComponent(
                out BossAttackHitboxController hitboxController))
            {
                hitboxController.SynchronizeWeaponHitbox();
            }
        }

        /// <summary>
        /// 突きの予備動作として武器を後ろへ引く。
        /// </summary>
        public void PlayThrustPreparation(float duration)
        {
            if (weaponPivot == null) { return; }

            ResetPose();
            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition - localAttackDirection * thrustPullbackDistance,
                    Mathf.Max(0.01f, duration)).SetEase(Ease.InOutSine))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// ダメージ判定開始直前のPlayer位置へ突きの向きを合わせる。
        /// </summary>
        public void AimThrustAt(Vector3 targetPosition)
        {
            if (weaponPivot == null) { return; }

            currentSequence?.Kill();
            currentSequence = null;

            Vector3 worldDirection = targetPosition - weaponPivot.position;
            worldDirection.z = 0f;

            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                currentThrustDirection = initialRotation * localAttackDirection;
                return;
            }

            Transform parent = weaponPivot.parent;
            currentThrustDirection = parent != null
                ? parent.InverseTransformDirection(worldDirection).normalized
                : worldDirection.normalized;
            currentThrustDirection.z = 0f;
            currentThrustDirection.Normalize();

            Vector3 initialDirection = initialRotation * localAttackDirection;
            Quaternion aimRotation = Quaternion.FromToRotation(
                initialDirection,
                currentThrustDirection);
            weaponPivot.localRotation = aimRotation * initialRotation;
            weaponPivot.localPosition = initialPosition -
                currentThrustDirection * thrustPullbackDistance;
        }


        /// <summary>
        /// 突きの速度倍率を反映しつつ、判定時間内で武器を戻す。
        /// </summary>
        public void PlayThrust(float duration, float animationSpeed)
        {
            if (weaponPivot == null) { return; }

            currentSequence?.Kill();
            currentSequence = null;

            float totalDuration = Mathf.Max(0.05f, duration);
            float safeSpeed = Mathf.Max(0.05f, animationSpeed);
            float extendDuration = totalDuration *
                Mathf.Clamp(0.25f / safeSpeed, 0.05f, 0.4f);
            float returnDuration = totalDuration * 0.25f;
            float holdDuration =
                totalDuration - extendDuration - returnDuration;

            currentSequence = DOTween.Sequence()
                .Append(weaponPivot.DOLocalMove(
                    initialPosition + currentThrustDirection * thrustDistance,
                    extendDuration).SetEase(Ease.OutCubic))
                .AppendInterval(holdDuration)
                .Append(weaponPivot.DOLocalMove(
                    initialPosition,
                    returnDuration).SetEase(Ease.InOutSine))
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }


        /// <summary>
        /// 通常姿勢から頭上へチェーンソーを振り上げる。
        /// </summary>
        public void PlayVerticalSlashPreparation(float duration, float raiseAngle)
        {
            if (weaponPivot == null) { return; }

            ResetPose();
            ApplyVerticalSlashRangePosition();
            ConfigureVerticalSlashBlade(false);

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
        /// 振り上げた姿勢から前方を通って振り下ろす。
        /// </summary>
        public void PlayVerticalSlash(
            float duration,
            float raiseAngle,
            float finishAngle,
            float animationSpeed)
        {
            if (weaponPivot == null) { return; }

            // 予備動作のTweenだけ停止する。ResetPoseは呼ばない。
            currentSequence?.Kill();
            currentSequence = null;

            Quaternion baseRotation = slashFrontRotation;
            weaponPivot.localRotation =
                baseRotation * Quaternion.Euler(0f, 0f, raiseAngle);

            Vector3 startPosition = weaponPivot.localPosition;
            float totalDuration = Mathf.Max(0.05f, duration);

            // 速度倍率は振り下ろしに割り当てる時間へ反映する。
            // 攻撃全体の有効時間は変えない。
            float safeSpeed = Mathf.Max(0.05f, animationSpeed);
            float swingDuration =
                totalDuration * Mathf.Clamp(0.6f / safeSpeed, 0.1f, 0.75f);
            float dropDuration = totalDuration * 0.2f;
            float returnDuration = totalDuration - swingDuration - dropDuration;

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
                                baseRotation * Quaternion.Euler(0f, 0f, angle);
                        }
                    }).SetEase(Ease.InCubic))
                .AppendCallback(() => ConfigureVerticalSlashBlade(true))
                .Append(weaponPivot.DOLocalMove(
                    startPosition + GetVerticalSlashDropOffset(),
                    dropDuration).SetEase(Ease.InCubic))
                .Append(weaponPivot.DOLocalMove(
                    initialPosition,
                    returnDuration).SetEase(Ease.OutSine))
                .Join(weaponPivot.DOLocalRotateQuaternion(
                    initialRotation,
                    returnDuration).SetEase(Ease.OutSine))
                .AppendCallback(ResetBladePose)
                .SetUpdate(UpdateType.Fixed)
                .SetLink(gameObject);
        }

        /// <summary>
        /// 縦切りCubeの回転中心を実際の複合判定の中心へ合わせる。
        /// </summary>
        private void ApplyVerticalSlashRangePosition()
        {
            slashFrontRotation = initialRotation;
            if (verticalSlashRangeSource == null &&
                TryGetComponent(out BossAttackHitboxController hitboxController))
            {
                verticalSlashRangeSource =
                    hitboxController.VerticalSlashRangeSource;
            }

            if (verticalSlashRangeSource == null) { return; }

            verticalSlashHitbox = verticalSlashRangeSource.GetComponent<BossAttackHitbox>();
            Transform rangeParent = verticalSlashRangeSource.parent;
            weaponPivot.localPosition = weaponPivot.parent == rangeParent
                ? verticalSlashRangeSource.localPosition
                : weaponPivot.parent.InverseTransformPoint(
                    verticalSlashRangeSource.position);

            if (verticalSlashHitbox != null)
            {
                Vector3 forward = verticalSlashRangeSource.TransformDirection(
                    verticalSlashHitbox.VerticalSlashForward);
                if (weaponPivot.parent != null)
                {
                    forward = weaponPivot.parent.InverseTransformDirection(forward);
                }

                slashFrontRotation = Quaternion.FromToRotation(
                    initialRotation * localAttackDirection, forward) * initialRotation;
            }
        }

        /// <summary>半円では半径、切り下ろしでは長方形の幅にCubeを合わせる。</summary>
        private void ConfigureVerticalSlashBlade(bool isDropping)
        {
            if (bladeCube == null || verticalSlashHitbox == null) { return; }

            float radius = verticalSlashHitbox.VerticalSlashRadius;
            float padding = isDropping ? verticalSlashHitbox.VerticalSlashWidthPadding : 0f;
            Vector3 scale = initialBladeScale;
            scale.x = radius + padding;
            bladeCube.localScale = scale;
            bladeCube.localPosition = localAttackDirection * ((radius - padding) * 0.5f);

            SynchronizeWeaponCollider();
        }

        /// <summary>切り下ろしの終点を判定の長方形下端へ合わせる。</summary>
        private Vector3 GetVerticalSlashDropOffset()
        {
            if (verticalSlashHitbox == null) { return Vector3.down * slashDropDistance; }

            Vector3 worldOffset = verticalSlashRangeSource.TransformVector(
                Vector3.up * verticalSlashHitbox.VerticalSlashGroundOffset);
            return weaponPivot.parent != null
                ? weaponPivot.parent.InverseTransformVector(worldOffset)
                : worldOffset;
        }

        /// <summary>次の攻撃へ寸法変更を持ち越さないようにCubeを戻す。</summary>
        private void ResetBladePose()
        {
            if (bladeCube == null) { return; }
            bladeCube.localPosition = initialBladePosition;
            bladeCube.localScale = initialBladeScale;

            SynchronizeWeaponCollider();
        }

        /// <summary>
        /// 盾が戻り始める予告表示を切り替える。
        /// </summary>
        public void SetShieldReturnCue(bool visible)
        {
            if (shieldReturnCue != null)
            {
                shieldReturnCue.SetActive(visible);
            }
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
            ResetBladePose();
            currentThrustDirection = initialRotation * localAttackDirection;
            SetShieldReturnCue(false);
        }

        private void OnDisable()
        {
            ResetPose();
        }

#if UNITY_EDITOR
        [ContextMenu("縦切りプレビュー（再生中・6秒）")]
        private void PreviewVerticalSlash()
        {
            if (!Application.isPlaying) { return; }

            PlayVerticalSlashPreparation(0.01f, -90f);
            PlayVerticalSlash(6f, -90f, 90f, 1f);
        }
#endif
    }
}
