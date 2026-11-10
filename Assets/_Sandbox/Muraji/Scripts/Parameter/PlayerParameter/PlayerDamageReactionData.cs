using UnityEngine;

// プレイヤーの被弾時に使う共通設定を管理する。残り時間や描画対象は保存しない。
[CreateAssetMenu(fileName = "PlayerDamageReactionData", menuName = "Player/Damage Reaction Data")]
public sealed class PlayerDamageReactionData : ScriptableObject
{
    [Header("やられ・無敵時間")]
    [Tooltip("やられ状態で操作を止める秒数です。硬直終了時に待機へ戻ります。空中では落下を続けます。")]
    [SerializeField, Min(0.01f)] private float hurtDuration = 0.4f;

    [Tooltip("敵の攻撃を受けた後、追加ダメージを無効にする秒数です。0なら追加の無敵時間を設けません。")]
    [SerializeField, Min(0f)] private float damageInvulnerabilityDuration = 2f;

    [Header("無敵中の白黒点滅")]
    [Tooltip("白黒点滅の元にするUniversal Render Pipeline/Litのマテリアルです。実行時は専用コピーを使い、元のマテリアルは変更しません。")]
    [SerializeField] private Material flashMaterial;

    [Tooltip("白と黒を切り替える間隔です。点滅は被弾後の無敵時間中に行います。")]
    [SerializeField, Min(0.01f)] private float flashInterval = 0.08f;

    public float HurtDuration => Mathf.Max(0.01f, hurtDuration);
    public float DamageInvulnerabilityDuration => Mathf.Max(0f, damageInvulnerabilityDuration);
    public Material FlashMaterial => flashMaterial;
    public float FlashInterval => Mathf.Max(0.01f, flashInterval);

    // Inspector編集時に秒数を有効範囲へ補正する。ゲーム実行中の経過状態は扱わない。
    private void OnValidate()
    {
        hurtDuration = Mathf.Max(0.01f, hurtDuration);
        damageInvulnerabilityDuration = Mathf.Max(0f, damageInvulnerabilityDuration);
        flashInterval = Mathf.Max(0.01f, flashInterval);
    }
}
