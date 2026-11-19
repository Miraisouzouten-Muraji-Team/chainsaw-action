using UnityEngine;

[CreateAssetMenu(menuName = "Camera/CameraData")]
public class CameraData : ScriptableObject
{
    [Header("通常時")]

    [Tooltip("Xはプレイヤーが向いている側への距離、Yは上への距離です。")]
    public Vector2 followOffset = new Vector2(3f, 3f);

    [Tooltip("通常時の追従率です。0.1は60fps基準で10%です。")]
    [Range(0.01f, 1f)]
    public float followPercent = 0.1f;

    [Header("床への食い込み")]

    [Tooltip("床食い込み中のオフセット。Xは向いている側、Yは上への距離です。")]
    public Vector2 floorOffset = new Vector2(5f, 3f);

    [Tooltip("床食い込み中の追従率です。0.2は60fps基準で20%です。")]
    [Range(0.01f, 1f)]
    public float floorFollowPercent = 0.2f;

    [Header("壁への食い込み")]

    [Tooltip("壁食い込み中のオフセット。Xは向いている側、Yは上への距離です。")]
    public Vector2 wallOffset = new Vector2(3f, 5f);

    [Tooltip("壁食い込み中の追従率です。0.2は60fps基準で20%です。")]
    [Range(0.01f, 1f)]
    public float wallFollowPercent = 0.2f;

    [Header("天井への食い込み")]

    [Tooltip("天井食い込み中のオフセット。Xは向いている側、Yは上への距離です。")]
    public Vector2 ceilingOffset = new Vector2(5f, 0f);

    [Tooltip("天井食い込み中の追従率です。0.2は60fps基準で20%です。")]
    [Range(0.01f, 1f)]
    public float ceilingFollowPercent = 0.2f;

    [Header("空中")]

    [Tooltip("食い込んでいないジャンプ中・落下中のオフセット。Xは向いている側、Yは上への距離です。")]
    public Vector2 airOffset = new Vector2(3f, 3f);

    [Tooltip("空中での追従率です。0.05は60fps基準で5%です。")]
    [Range(0.01f, 1f)]
    public float airFollowPercent = 0.05f;

    [Header("左右の切り替え")]

    [Tooltip("振り向いた際に視野の方向を切り替える速さです。0.05は60fps基準で5%です。")]
    [Range(0.01f, 1f)]
    public float directionChangePercent = 0.05f;

    [Header("食い込み時のズーム")]

    [Tooltip("カーブのYが1のときのOrthographic Size倍率です。1.2なら画面に映る高さが通常の1.2倍になります。")]
    [Min(1f)]
    public float diggingZoomMultiplier = 1.2f;

    [Tooltip("横軸はアクセルの割合（最低0・最大1）、縦軸は引きの割合（通常0・最大1）です。")]
    public AnimationCurve diggingZoomCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("食い込み開始・解除時に画角を切り替える秒数です。アクセル値にかかわらず同じ時間で切り替わります。食い込み継続中はアクセル値の変化をなめらかに追います。0なら即時反映します。")]
    [Min(0f)]
    public float diggingZoomSmoothTime = 0.18f;

    [Header("旧カメラとの互換用")]

    [Tooltip("旧カメラ用の追加先読み距離です。今回の状態別カメラでは使いません。各オフセットのXで調整してください。")]
    [Min(0f)]
    public float directionLookAhead = 2f;
}
