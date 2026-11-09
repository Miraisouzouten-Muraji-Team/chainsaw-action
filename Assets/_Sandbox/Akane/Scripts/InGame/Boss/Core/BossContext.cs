using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの各行動から参照する共通情報。
    /// </summary>
    public sealed class BossContext
    {
        public BossController Boss { get; }
        public Transform Player => Boss.Player;
        public Animator Animator => Boss.Animator;
        public BossHealth Health => Boss.Health;
        public BossShieldController Shield => Boss.Shield;

        public BossContext(BossController boss)
        {
            Boss = boss;
        }
    }
}
