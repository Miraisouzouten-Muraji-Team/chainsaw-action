namespace Project.InGame.Boss
{
    /// <summary>
    /// 前方への突き攻撃。
    /// </summary>
    public sealed class ThrustAttack : BossAttackStrategyBase
    {
        public ThrustAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override void OnActiveStart(BossContext context)
        {
            // TODO: 突き攻撃の判定を開始する。
        }

        protected override void OnActiveEnd(BossContext context)
        {
            // TODO: 突き攻撃の判定を終了する。
        }
    }
}
