using UnityEngine;

[CreateAssetMenu(fileName = "PlayerHealthData", menuName = "Player/HP設定")]
public sealed class PlayerHealthData : ScriptableObject
{
    [Tooltip("プレイヤーの最大HPです。現在HPはこのアセットではなく、プレイヤーごとに管理します。")]
    [SerializeField, Min(0.01f)] private float maxHealth = 100f;

    [Header("被ダメージがないときの自動回復")]
    [Tooltip("最後にダメージを受けてから、最初の自動回復までの待ち時間です。単位は秒です。再びダメージを受けると最初から数え直します。")]
    [SerializeField, Min(0f)] private float recoveryDelay = 10f;

    [Tooltip("自動回復1回あたりに増えるHPです。最大HPを超えて回復しません。0なら自動回復しません。")]
    [SerializeField, Min(0f)] private float recoveryAmount = 1f;

    [Tooltip("最初の自動回復後、次に回復するまでの間隔です。単位は秒です。待ち時間と回復間隔はゲーム内時間で進みます。")]
    [SerializeField, Min(0.01f)] private float recoveryInterval = 1f;

    public float MaxHealth => Mathf.Max(0.01f, maxHealth);
    public float RecoveryDelay => Mathf.Max(0f, recoveryDelay);
    public float RecoveryAmount => Mathf.Max(0f, recoveryAmount);
    public float RecoveryInterval => Mathf.Max(0.01f, recoveryInterval);
}
