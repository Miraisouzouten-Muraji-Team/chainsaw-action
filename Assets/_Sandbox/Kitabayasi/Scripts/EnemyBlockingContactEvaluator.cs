using UnityEngine;

/// <summary>
/// 接触情報の法線から、指定した移動方向を正面から塞ぐ接触点を判定する。
/// </summary>
/// <remarks>
/// 接触後に停止・反転・攻撃終了など何を行うかは判断せず、接触の評価だけを担当する。
/// </remarks>
public static class EnemyBlockingContactEvaluator
{
    public static bool TryGetBlockingContact(
        EnemyContactInfo collision,
        Vector3 moveDirection,
        float blockingNormalThreshold,
        out EnemyContactInfo blockingContact)
    {
        if (!collision.IsValid ||
            moveDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            blockingContact = default;
            return false;
        }

        Vector3 normalizedMoveDirection =
            moveDirection.normalized;

        int contactCount =
            collision.ContactCount;

        for (int i = 0;
             i < contactCount;
             i++)
        {
            EnemyContactInfo contact =
                collision.GetContact(i);

            float blockingDot =
                Vector3.Dot(
                    contact.Normal,
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
