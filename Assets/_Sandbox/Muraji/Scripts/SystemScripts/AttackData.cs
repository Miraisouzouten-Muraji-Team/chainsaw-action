using UnityEngine;

[CreateAssetMenu(menuName ="Player/AttackData")]
public class AttackData : ScriptableObject
{
    public int damage; // 攻撃力

    public float hitStopTime; // ヒットストップ時間

    public float attackVector; // 攻撃ベクトル

    public float attackMoveRange; // 攻撃移動距離
}
