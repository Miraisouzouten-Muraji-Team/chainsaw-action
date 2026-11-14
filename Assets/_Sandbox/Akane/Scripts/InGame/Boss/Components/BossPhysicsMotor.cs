
using System;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Rigidbodyを使用したボスの横移動・上昇・落下を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BossPhysicsMotor : MonoBehaviour
    {
        private enum MotionMode
        {
            None,
            Horizontal,
            Ascending,
            Holding,
            Falling
        }

        private Rigidbody body;
        private MotionMode motionMode;

        private float targetX;
        private float moveSpeed;

        private Vector3 ascentTarget;
        private float ascentRemaining;
        private float fallSpeed;

        public bool IsGrounded { get; private set; }
        public event Action Landed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// プレイヤーとの目標距離まで横方向に移動する。
        /// </summary>
        public void SetHorizontalTarget(float x, float speed)
        {
            if (motionMode == MotionMode.Ascending ||
                motionMode == MotionMode.Holding ||
                motionMode == MotionMode.Falling)
                return;

            targetX = x;
            moveSpeed = Mathf.Max(0f, speed);
            motionMode = MotionMode.Horizontal;
        }

        public void StopHorizontal()
        {
            if (motionMode != MotionMode.Horizontal)
                return;

            motionMode = MotionMode.None;

            Vector3 velocity = body.linearVelocity;
            velocity.x = 0f;
            body.linearVelocity = velocity;
        }

        /// <summary>
        /// 盾打ち込みで指定した高さへ上昇する。
        /// </summary>
        public void BeginAscent(float x, float height, float duration)
        {
            body.useGravity = false;
            IsGrounded = false;

            ascentTarget = new Vector3(
                x,
                body.position.y + Mathf.Max(0f, height),
                body.position.z);

            ascentRemaining = Mathf.Max(Time.fixedDeltaTime, duration);
            motionMode = MotionMode.Ascending;
        }

        /// <summary>
        /// 上空で停止する。
        /// </summary>
        public void HoldInAir()
        {
            motionMode = MotionMode.Holding;
            body.linearVelocity = Vector3.zero;
        }

        /// <summary>
        /// 指定した速度で地面へ落下する。
        /// </summary>
        public void BeginFall(float speed)
        {
            fallSpeed = Mathf.Max(0f, speed);
            IsGrounded = false;
            motionMode = MotionMode.Falling;
        }

        /// <summary>
        /// 攻撃終了時に通常の物理移動へ戻す。
        /// </summary>
        public void EndScriptedMotion()
        {
            motionMode = MotionMode.None;
            body.useGravity = true;

            Vector3 velocity = body.linearVelocity;
            velocity.x = 0f;
            velocity.y = 0f;
            body.linearVelocity = velocity;
        }

        private void FixedUpdate()
        {
            Vector3 velocity = body.linearVelocity;

            switch (motionMode)
            {
                case MotionMode.Horizontal:
                    {
                        float difference = targetX - body.position.x;
                        float maxSpeed = Mathf.Min(
                            moveSpeed,
                            Mathf.Abs(difference) / Time.fixedDeltaTime);

                        velocity.x = Mathf.Sign(difference) * maxSpeed;
                        body.linearVelocity = velocity;
                        break;
                    }

                case MotionMode.Ascending:
                    {
                        float remaining = Mathf.Max(
                            Time.fixedDeltaTime, ascentRemaining);

                        velocity.x = (ascentTarget.x - body.position.x) / remaining;
                        velocity.y = (ascentTarget.y - body.position.y) / remaining;
                        body.linearVelocity = velocity;

                        ascentRemaining -= Time.fixedDeltaTime;

                        if (ascentRemaining <= 0f)
                            HoldInAir();

                        break;
                    }

                case MotionMode.Holding:
                    body.linearVelocity = Vector3.zero;
                    break;

                case MotionMode.Falling:
                    velocity.x = 0f;
                    velocity.y = -fallSpeed;
                    body.linearVelocity = velocity;
                    break;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            CheckGroundContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            CheckGroundContact(collision);
        }

        /// <summary>
        /// StageColliderとの接触法線から着地を判定する。
        /// </summary>
        private void CheckGroundContact(Collision collision)
        {
            if (collision.gameObject.layer !=
                LayerMask.NameToLayer("StageCollider"))
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y <= 0.5f)
                    continue;

                bool wasFalling = motionMode == MotionMode.Falling;
                IsGrounded = true;

                if (wasFalling)
                {
                    EndScriptedMotion();
                    Landed?.Invoke();
                }

                return;
            }
        }

        private void OnDisable()
        {
            if (body != null)
                EndScriptedMotion();
        }
    }
}
