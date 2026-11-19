using System;
using UnityEngine;

/// <summary>
/// Enemyの接触先と接触位置・法線をStrategyへ渡す値。
/// </summary>
/// <remarks>
/// Triggerイベント自体に接触点・法線はないため、移動前のPhysics Queryから得た情報を使用する。
/// 物理ソルバー由来のImpulseを意味する値は取得できない。既存の診断表示との互換用に0を返す。
/// </remarks>
public readonly struct EnemyContactInfo
{
    public Collider OtherCollider { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 SecondaryNormal { get; }
    public Vector3 RelativeVelocity { get; }
    public Vector3 Impulse => Vector3.zero;
    public bool IsValid => OtherCollider != null;
    public int ContactCount => !IsValid || Normal.sqrMagnitude <= 0f ? 0 :
        SecondaryNormal.sqrMagnitude > 0f ? 2 : 1;

    public EnemyContactInfo(
        Collider otherCollider,
        Vector3 point,
        Vector3 normal,
        Vector3 relativeVelocity,
        Vector3 secondaryNormal = default)
    {
        OtherCollider = otherCollider;
        Point = point;
        Normal = normal;
        SecondaryNormal = secondaryNormal;
        RelativeVelocity = relativeVelocity;
    }

    /// <summary>
    /// 既存の接触評価が使用していたContact取得を最大2つの接触面情報で提供する。
    /// </summary>
    public EnemyContactInfo GetContact(int index)
    {
        if (index < 0 || index >= ContactCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index == 0 ? this :
            new EnemyContactInfo(OtherCollider, Point, SecondaryNormal, RelativeVelocity);
    }
}
