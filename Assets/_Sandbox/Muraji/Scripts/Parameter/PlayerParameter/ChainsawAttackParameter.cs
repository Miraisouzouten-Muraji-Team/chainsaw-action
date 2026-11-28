using UnityEngine;

[CreateAssetMenu(fileName = "ChainsawAttackParameter", menuName = "Player/Chainsaw Attack Parameter")]
public sealed class ChainsawAttackParameter : ScriptableObject
{

    [Header("チェーンソー攻撃の共通設定")]
    [Tooltip("弱攻撃の食い込みでダメージを与える回数です。この回数に達したら振り抜きへ進みます。")]
    [Min(1)] public int weakDiggingHitCount = 3;
    [Tooltip("振り抜きの攻撃ベクトルに沿って敵モデルを震わせる片側の幅です。ワールド座標の距離で指定し、0なら震えを無効にします。当たり判定とRigidbodyは動かしません。")]
    [Min(0f)]
    public float enemyDiggingShakeAmplitude = 0.025f;

    [Tooltip("食い込み中に敵モデルが1秒間に往復する回数です。ヒットストップ中は震えの時間も止まります。")]
    [Min(0f)]
    public float enemyDiggingShakeFrequency = 20f;

    [Tooltip("振り抜き後に敵がプレイヤーを押さないよう、両者の接触を一時的に無視する秒数です。敵の速度・位置・地面衝突は敵側で制御します。")]
    [Min(0.01f)]
    public float slashKnockbackPriorityDuration = 0.15f;

    [Tooltip("通常攻撃・食い込み共通の揺れ時間です。0なら Default Hit Shake Duration を使用します。揺れの強さは各攻撃SOで設定します。")]
    [Min(0f)]
    public float hitShakeDuration = 0.15f;

    [Tooltip("弱攻撃の食い込みでダメージが成立するたびに増えるアクセル値です。")]
    [Min(0f)]
    public float weakDiggingAcceleratorGain = 1f;

    [Tooltip("振り抜きでダメージが成立したときに増えるアクセル値です。複数の敵に当たっても弱攻撃1回につき一度だけ増えます。")]
    [Min(0f)]
    public float weakFinisherAcceleratorGain = 5f;

    [Tooltip("揺れ時間が0の場合に使うヒット時の揺れ時間です。")]
    [Min(0f)] public float defaultHitShakeDuration = 0.15f;
}
