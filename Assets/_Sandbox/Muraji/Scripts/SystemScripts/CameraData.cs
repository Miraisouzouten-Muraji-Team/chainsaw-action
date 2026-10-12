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

    [Header("旧カメラとの互換用")]

    [Tooltip("旧カメラ用の追加先読み距離です。今回の状態別カメラでは使いません。各オフセットのXで調整してください。")]
    [Min(0f)]
    public float directionLookAhead = 2f;
}
