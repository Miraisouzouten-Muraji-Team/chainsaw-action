using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// テトリミノ方式でボスの攻撃を抽選する。
    /// </summary>
    public sealed class BossAttackSelector
    {
        private readonly List<IBossAttackStrategy> attacks;
        private readonly List<IBossAttackStrategy> remainingAttacks = new();

        private bool? previousShieldState;

        public int AttackCount => attacks.Count;

        public BossAttackSelector(IEnumerable<IBossAttackStrategy> strategies)
        {
            attacks = new List<IBossAttackStrategy>(strategies);
        }

        /// <summary>
        /// 使用可能な攻撃から次の攻撃を取り出す。
        /// </summary>
        public IBossAttackStrategy DrawNext(bool hasShield)
        {
            // 盾の有無が変わった場合、攻撃候補を作り直す。
            if (previousShieldState != hasShield || remainingAttacks.Count == 0)
            {
                Refill(hasShield);
            }

            if (remainingAttacks.Count == 0) { return null; }

            // シャッフル済みの候補から最後の1件を取り出す。
            var lastIndex = remainingAttacks.Count - 1;
            var selectedAttack = remainingAttacks[lastIndex];

            remainingAttacks.RemoveAt(lastIndex);

            return selectedAttack;
        }

        /// <summary>
        /// 現在使用できる攻撃を集め、順序をシャッフルする。
        /// </summary>
        private void Refill(bool hasShield)
        {
            remainingAttacks.Clear();
            previousShieldState = hasShield;

            foreach (var attack in attacks)
            {
                if (attack.Definition.RequiresShield && !hasShield)
                {
                    continue;
                }
                remainingAttacks.Add(attack);
            }

            // Fisher-Yates法で候補をシャッフルする。
            for (var i = remainingAttacks.Count - 1; i > 0; i--)
            {
                var randomIndex = Random.Range(0, i + 1);

                (remainingAttacks[i], remainingAttacks[randomIndex]) =
                    (remainingAttacks[randomIndex], remainingAttacks[i]);
            }
        }
    }
}
