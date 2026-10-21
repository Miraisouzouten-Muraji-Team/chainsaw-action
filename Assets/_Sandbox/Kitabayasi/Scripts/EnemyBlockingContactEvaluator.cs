using UnityEngine;

/// <summary>
/// Collision内の接触法線から、指定した移動方向を正面から塞ぐ接触点を判定する。
/// </summary>
/// <remarks>
/// 接触後に停止・反転・攻撃終了など何を行うかは判断せず、接触の評価だけを担当する。
/// </remarks>
public static class EnemyBlockingContactEvaluator
{
    public static bool TryGetBlockingContact(
        Collision collision,
        Vector3 moveDirection,
        float blockingNormalThreshold,
        out ContactPoint blockingContact)
    {
        if (collision == null ||
            moveDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            blockingContact = default;
            return false;
        }

        Vector3 normalizedMoveDirection =
            moveDirection.normalized;

        int contactCount =
            collision.contactCount;

        for (int i = 0;
             i < contactCount;
             i++)
        {
            ContactPoint contact =
                collision.GetContact(i);

            float blockingDot =
                Vector3.Dot(
                    contact.normal,
                    normalizedMoveDirection);

            if (blockingDot >
                -blockingNormalThreshold)
            {
                continue;
            }

            blockingContact = contact;
            return true;
        }

        blockingContact = default;
        return false;
    }
}
