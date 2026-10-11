using UnityEngine;

[CreateAssetMenu(menuName = "Player/AttackData")]
public class AttackData : ScriptableObject
{
    [Header("弱攻撃設定")]

    [Tooltip("攻撃力")]
    public int damage;

    [Tooltip("攻撃ベクトル")]
    public float attackVector;

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
}
