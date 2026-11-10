using UnityEngine;

[CreateAssetMenu(menuName = "Player/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("弱攻撃設定")]

    [Tooltip("攻撃力")]
    public int damage;

    [Tooltip("攻撃ベクトル")]
    public Vector2 attackVector;

    [Tooltip("踏み込み移動量")]
    [Min(0f)]
    public float attackMoveRange;

    [Tooltip("踏み込みの秒数")]
    [Min(0.01f)]
    public float attackMoveDuration = 0.15f;

    [Tooltip("カメラシェイク")]
    [Min(0f)]
    public float cameraShack;

    [Tooltip("ヒットストップ")]
    [Min(0f)]
    public float hitStopTime;

    [Tooltip("空中攻撃での重力設定")]
    [Min(0f)]
    public float airAttackGravityScale = 1.0f;
    [Header("ダメージ数字の表示")]
    [Tooltip("この攻撃が敵のHPを実際に減らしたとき、ダメージ数字を表示します。")]
    public bool showDamagePopup = true;

    [Tooltip("この攻撃の数字の大きさです。1920×1080の画面を基準にした文字サイズです。")]
    [Min(1f)] public float damagePopupFontSize = 56f;

    [Tooltip("数字本体の色です。グラデーションを使う場合は上側の色になります。")]
    public Color damagePopupColor = new Color(0.9f, 1f, 1f, 1f);

    [Header("数字の黒フチ・グラデーション")]
    [Tooltip("オンなら、ネオンの光と別にくっきりしたフチを付けます。画像のような黒フチは、この色を黒にします。")]
    public bool damagePopupUseOutline;

    [Tooltip("くっきりしたフチの色です。黒にすると文字が背景から見分けやすくなります。")]
    public Color damagePopupOutlineColor = Color.black;

    [Tooltip("くっきりしたフチの太さです。最初は0.15〜0.25を試してください。")]
    [Range(0f, 0.5f)] public float damagePopupOutlineWidth = 0.18f;

    [Tooltip("オンなら数字の上側から下側へ色を変化させます。上側にはDamage Popup Colorを使います。")]
    public bool damagePopupUseGradient;

    [Tooltip("グラデーションを使う場合の、数字の下側の色です。")]
    public Color damagePopupBottomColor = new Color(1f, 0.36f, 0.08f, 1f);

    [Tooltip("数字が出るたびに色合いを少し変える幅です。0なら毎回同じ色、0.02ならわずかな色違いになります。文字の明るさとフチの色は変えません。")]
    [Range(0f, 0.1f)] public float damagePopupHueVariation;

    [Header("数字のネオン")]
    [Tooltip("数字の縁と外側に重ねる光の色です。フォントの字形は変更しません。")]
    public Color damagePopupNeonColor = new Color(0f, 0.9f, 1f, 1f);

    [Tooltip("ネオンの光の濃さです。0なら光だけを消します。くっきりしたフチは別の設定で維持できます。")]
    [Range(0f, 1f)] public float damagePopupNeonStrength = 0.85f;
}
