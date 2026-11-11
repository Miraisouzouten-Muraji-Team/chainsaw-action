namespace Project.InGame.Boss
{
    /// <summary>
    /// チェーンソーを縦方向に振り下ろす攻撃。
    /// </summary>
    public sealed class VerticalSlashAttack : BossAttackStrategyBase
    {
        public VerticalSlashAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override void OnActiveStart(BossContext context)
        {
            // TODO: 縦振り攻撃の判定を開始する。
        }

        protected override void OnActiveEnd(BossContext context)
        {
            // TODO: 縦振り攻撃の判定を終了する。
        }
    }
}
