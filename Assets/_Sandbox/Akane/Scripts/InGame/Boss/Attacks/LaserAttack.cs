namespace Project.InGame.Boss
{
    /// <summary>
    /// プレイヤーを狙ってレーザーを発射する攻撃。
    /// </summary>
    public sealed class LaserAttack : BossAttackStrategyBase
    {
        public LaserAttack(BossAttackDefinition definition)
            : base(definition)
        {
        }

        protected override void OnActiveStart(BossContext context)
        {
            // TODO: レーザーの発射処理を開始する。
        }

        protected override void OnActiveEnd(BossContext context)
        {
            // TODO: レーザーの発射処理を終了する。
        }
    }
}
