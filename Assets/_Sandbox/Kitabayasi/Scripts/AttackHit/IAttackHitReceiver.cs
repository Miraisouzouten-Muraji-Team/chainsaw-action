/// <summary>
/// 攻撃命中情報を受け取れるオブジェクトであることを表す。
/// </summary>
public interface IAttackHitReceiver
{
    /// <summary>
    /// 攻撃命中情報を受け取る。
    /// </summary>
    /// <param name="attackHitData">
    /// 今回の攻撃命中に関する情報。
    /// </param>
    void ReceiveAttackHit(AttackHitData attackHitData);
}
