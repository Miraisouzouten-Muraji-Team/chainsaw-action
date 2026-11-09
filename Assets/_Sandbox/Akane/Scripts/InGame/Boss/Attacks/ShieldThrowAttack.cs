namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾を投げて攻撃する。
    /// </summary>
    public sealed class ShieldThrowAttack : BossAttackStrategyBase
    {
        public ShieldThrowAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override void OnActiveStart(BossContext context)
        {
            // TODO: 盾の投擲処理を開始する。
        }

        protected override void OnActiveEnd(BossContext context)
        {
            // TODO: 盾の投擲処理の終了を確認する。
        }
    }
}
