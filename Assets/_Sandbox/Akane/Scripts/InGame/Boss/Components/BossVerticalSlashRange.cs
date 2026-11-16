using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 縦切りの表示範囲と仮モーションの寸法を管理する。
    /// 実際のダメージ判定はWeaponHitboxが担当する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossVerticalSlashRange : MonoBehaviour
    {
        [Header("半円部分")]
        [SerializeField, Min(0.01f)] private float radius = 4f;
        [SerializeField] private Vector3 localForward = Vector3.left;

        [Header("真下への切り下ろし部分")]
        [SerializeField, Min(0f)] private float dropDistance = 2f;
        [SerializeField, Min(0f)] private float rectangleWidthPadding = 0.5f;

        public float Radius => radius;
        public float DropDistance => dropDistance;
        public float RectangleWidthPadding => rectangleWidthPadding;

        public Vector3 LocalForward
        {
            get
            {
                Vector3 forward = localForward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : Vector3.left;
            }
        }

        /// <summary>
        /// デバッグ表示用の半円上の位置を返す。
        /// </summary>
        public Vector3 GetArcPoint(float t)
        {
            float angle = Mathf.Lerp(0f, Mathf.PI, Mathf.Clamp01(t));
            return new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f);
        }
    }
}
