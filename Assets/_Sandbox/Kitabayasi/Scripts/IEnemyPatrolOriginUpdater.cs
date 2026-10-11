/// <summary>
/// 現在のEnemyの位置と向きを、
/// 新しい巡回基準として取り込めるStrategyの契約を定義する。
/// </summary>
/// <remarks>
/// すべてのEnemyへ巡回基準更新を強制せず、
/// Damage後などに巡回基準を更新する必要があるStrategyだけが実装する。
/// </remarks>
public interface IEnemyPatrolOriginUpdater
{
    /// <summary>
    /// 現在位置と現在向きを新しい巡回基準として保存する。
    /// </summary>
    void UpdatePatrolOriginFromCurrentPose();
}
