namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾を地面に打ち込む攻撃。
    /// </summary>
    public sealed class ShieldBashAttack : BossAttackStrategyBase
    {
        public ShieldBashAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override void OnActiveStart(BossContext context)
        {
            // TODO: 盾の打ち込み判定を開始する。
        }

        protected override void OnActiveEnd(BossContext context)
        {
            // TODO: 盾の打ち込み判定を終了する。
        }
    }
}
