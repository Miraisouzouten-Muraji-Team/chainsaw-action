using UnityEngine;

/// <summary>
/// Enemy側で共通利用するPlayer参照を保持し、Player階層に属するColliderと攻撃受信先を解決する。
/// </summary>
/// <remarks>
/// Playerの索敵可否、攻撃可否、State遷移は担当しない。
/// Playerが未取得または破棄済みの場合は、必要になった時点でPlayer Tagから再解決する。
/// </remarks>
public sealed class EnemyPlayerReference
{
    private const string PLAYER_TAG = "Player";

    private Transform playerTransform;
    private MonoBehaviour attackHitReceiverComponent;
    private IAttackHitReceiver attackHitReceiver;

    /// <summary>
    /// Player RootのTransformを取得する。
    /// 未解決の場合はPlayer Tagから再検索する。
    /// </summary>
    public bool TryGetPlayerTransform(
        out Transform resolvedPlayerTransform)
    {
        if (playerTransform == null)
        {
            ClearAttackHitReceiver();

            GameObject playerObject =
                GameObject.FindGameObjectWithTag(
                    PLAYER_TAG);

            if (playerObject == null)
            {
                resolvedPlayerTransform = null;
                return false;
            }

            playerTransform =
                playerObject.transform;
        }

        resolvedPlayerTransform =
            playerTransform;

        return true;
    }

    /// <summary>
    /// 保持しているPlayer Rootと攻撃受信先を破棄し、次回利用時に再解決させる。
    /// </summary>
    public void Clear()
    {
        playerTransform = null;
        ClearAttackHitReceiver();
    }

    /// <summary>
    /// 指定Colliderが現在のPlayer Root自身またはその子階層に属するか判定する。
    /// </summary>
    public bool IsPlayerCollider(
        Collider targetCollider)
    {
        if (targetCollider == null ||
            !TryGetPlayerTransform(
                out Transform resolvedPlayerTransform))
        {
            return false;
        }

        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == resolvedPlayerTransform ||
            targetTransform.IsChildOf(
                resolvedPlayerTransform);
    }

    /// <summary>
    /// Player階層からIAttackHitReceiverを解決する。
    /// </summary>
    /// <param name="requireActiveAndEnabled">
    /// trueの場合は有効なMonoBehaviourだけを候補にする。
    /// falseの場合は非アクティブな子階層も含め、最初に見つかった実装を返す。
    /// </param>
    public bool TryGetAttackHitReceiver(
        bool requireActiveAndEnabled,
        out IAttackHitReceiver resolvedReceiver,
        out MonoBehaviour resolvedReceiverComponent)
    {
        if (!TryGetPlayerTransform(
                out Transform resolvedPlayerTransform))
        {
            ClearAttackHitReceiver();
            resolvedReceiver = null;
            resolvedReceiverComponent = null;
            return false;
        }

        if (IsCachedAttackHitReceiverValid(
                requireActiveAndEnabled))
        {
            resolvedReceiver =
                attackHitReceiver;

            resolvedReceiverComponent =
                attackHitReceiverComponent;

            return true;
        }

        ClearAttackHitReceiver();

        MonoBehaviour[] behaviours =
            resolvedPlayerTransform.GetComponentsInChildren<MonoBehaviour>(
                true);

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour is not IAttackHitReceiver receiver)
            {
                continue;
            }

            if (requireActiveAndEnabled &&
                !behaviour.isActiveAndEnabled)
            {
                continue;
            }

            attackHitReceiverComponent =
                behaviour;

            attackHitReceiver =
                receiver;

            resolvedReceiver =
                attackHitReceiver;

            resolvedReceiverComponent =
                attackHitReceiverComponent;

            return true;
        }

        resolvedReceiver = null;
        resolvedReceiverComponent = null;
        return false;
    }

    private bool IsCachedAttackHitReceiverValid(
        bool requireActiveAndEnabled)
    {
        if (attackHitReceiverComponent == null ||
            attackHitReceiver == null)
        {
            return false;
        }

        return
            !requireActiveAndEnabled ||
            attackHitReceiverComponent.isActiveAndEnabled;
    }

    private void ClearAttackHitReceiver()
    {
        attackHitReceiverComponent = null;
        attackHitReceiver = null;
    }
}
