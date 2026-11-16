using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 前回と今回の刃の位置・回転の間を補間し、
    /// 高速回転中の命中漏れを減らす。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossAttackHitbox))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BossWeaponSweepProbe : MonoBehaviour
    {
        private const int HIT_BUFFER_SIZE = 64;

        [Header("判定対象")]
        [SerializeField] private LayerMask playerLayers;

        [Header("補間精度")]
        [SerializeField, Range(2, 12)]
        private int sampleCount = 6;

        private readonly Collider[] hitBuffer =
            new Collider[HIT_BUFFER_SIZE];

        private BossAttackHitbox hitbox;
        private BoxCollider bladeCollider;

        private Vector3 previousCenter;
        private Vector3 previousHalfExtents;
        private Quaternion previousRotation;
        private bool hasPreviousPose;

        private void Awake()
        {
            hitbox = GetComponent<BossAttackHitbox>();
            bladeCollider = GetComponent<BoxCollider>();
        }

        /// <summary>
        /// 攻撃開始時点の刃の姿勢を記録する。
        /// </summary>
        public void CaptureCurrentPose()
        {
            if (bladeCollider == null)
                return;

            ReadCurrentPose(
                out previousCenter,
                out previousHalfExtents,
                out previousRotation);

            hasPreviousPose = true;
        }

        private void FixedUpdate()
        {
            if (hitbox == null || bladeCollider == null ||
                !hitbox.IsWindowOpen || playerLayers.value == 0)
            {
                hasPreviousPose = false;
                return;
            }

            ReadCurrentPose(
                out Vector3 currentCenter,
                out Vector3 currentHalfExtents,
                out Quaternion currentRotation);

            if (!hasPreviousPose)
            {
                previousCenter = currentCenter;
                previousHalfExtents = currentHalfExtents;
                previousRotation = currentRotation;
                hasPreviousPose = true;
            }

            // 直前の姿勢から現在の姿勢までの中間形状を調べる。
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = (float)i / sampleCount;

                Vector3 center = Vector3.Lerp(
                    previousCenter, currentCenter, t);

                Vector3 halfExtents = Vector3.Lerp(
                    previousHalfExtents, currentHalfExtents, t);

                Quaternion rotation = Quaternion.Slerp(
                    previousRotation, currentRotation, t);

                int hitCount = Physics.OverlapBoxNonAlloc(
                    center,
                    halfExtents,
                    hitBuffer,
                    rotation,
                    playerLayers,
                    QueryTriggerInteraction.Ignore);

                for (int j = 0; j < hitCount; j++)
                {
                    Collider other = hitBuffer[j];

                    if (other != null)
                        hitbox.TryHitCollider(other);

                    hitBuffer[j] = null;
                }

                if (hitCount == HIT_BUFFER_SIZE)
                {
                    Debug.LogWarning(
                        "BossWeaponSweepProbe: 判定配列が満杯です。",
                        this);
                }
            }

            previousCenter = currentCenter;
            previousHalfExtents = currentHalfExtents;
            previousRotation = currentRotation;
        }

        /// <summary>
        /// 刃の現在のワールド位置・寸法・回転を返す。
        /// </summary>
        private void ReadCurrentPose(
            out Vector3 center,
            out Vector3 halfExtents,
            out Quaternion rotation)
        {
            Transform bladeTransform = bladeCollider.transform;
            Vector3 scale = bladeTransform.lossyScale;

            center = bladeTransform.TransformPoint(
                bladeCollider.center);

            halfExtents = Vector3.Scale(
                bladeCollider.size * 0.5f,
                new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)));

            rotation = bladeTransform.rotation;
        }

        private void OnDisable()
        {
            hasPreviousPose = false;
        }
    }
}
