using UnityEngine;

// 被弾演出の表示時間を管理する。色・濃さ・ぼかしは専用VolumeのProfileで設定する。
[CreateAssetMenu(menuName = "Player/PlayerDamageScreenData")]
public sealed class PlayerDamageScreenData : ScriptableObject
{
    [Tooltip("最大の強さを保つ秒数です。その後、徐々に消えます。")]
    [SerializeField, Min(0f)] private float holdDuration = 0.08f;

    [Tooltip("最大の強さから完全に消えるまでの秒数です。0なら維持時間の終了時に消します。")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

    public float HoldDuration => Mathf.Max(0f, holdDuration);
    public float FadeDuration => Mathf.Max(0f, fadeDuration);
}
