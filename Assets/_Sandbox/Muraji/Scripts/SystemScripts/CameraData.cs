using UnityEngine;

[CreateAssetMenu(menuName = "Camera/CameraData")]
public class CameraData : ScriptableObject
{
    [Header("カメラ設定")]

    [Tooltip("オフセット")]
    public Vector2 followOffset;

    [Tooltip("プレイヤーの進行方向へどれだけ視野を広げるか。")]
    [Min(0f)]
    public float directionLookAhead = 2f;

    [Tooltip("追従率")]
    [Range(0.01f, 1f)]
    public float followPercent = 0.1f;
}
