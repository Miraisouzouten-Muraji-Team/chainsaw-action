using UnityEngine;

/// <summary>
/// Enemyがデバッグ表示へ提供するRayの用途を識別する。
/// </summary>
/// <remarks>
/// 判定処理側は用途だけを指定し、Sceneビュー上の表示色はEditor側で決定する。
/// EnemyDebugRay専用の小さな分類型のため、同じファイルに配置する。
/// </remarks>
public enum EnemyDebugRayKind
{
    LineOfSight,
    GroundCheck
}

/// <summary>
/// デバッグ対象Rayの判定結果を識別する。
/// </summary>
/// <remarks>
/// 現在はLineOfSightの成立・遮蔽結果に使用する。
/// 結果を持たないRayではNotApplicableを使用する。
/// </remarks>
public enum EnemyDebugRayResult
{
    NotApplicable,
    Success,
    Blocked
}

/// <summary>
/// Enemy処理で実際に使用するRay 1本分の幾何情報を保持する。
/// </summary>
/// <remarks>
/// デバッグ描画側でRayの位置や長さを再計算しないため、
/// 判定処理がPhysicsへ渡す始点・方向・距離をそのまま記録する。
/// </remarks>
public readonly struct EnemyDebugRay
{
    public EnemyDebugRay(
        Vector3 origin,
        Vector3 direction,
        float distance,
        EnemyDebugRayKind kind,
        EnemyDebugRayResult result =
            EnemyDebugRayResult.NotApplicable)
    {
        Origin = origin;
        Direction = direction;
        Distance = distance;
        Kind = kind;
        Result = result;
    }

    public Vector3 Origin { get; }
    public Vector3 Direction { get; }
    public float Distance { get; }
    public EnemyDebugRayKind Kind { get; }
    public EnemyDebugRayResult Result { get; }
}
