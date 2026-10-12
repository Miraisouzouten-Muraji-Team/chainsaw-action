using UnityEngine;

[CreateAssetMenu(fileName = "PlayerHealthData", menuName = "Player/HP設定")]
public sealed class PlayerHealthData : ScriptableObject
{
    [Tooltip("プレイヤーの最大HPです。現在HPはこのアセットではなく、プレイヤーごとに管理します。")]
    [SerializeField, Min(0.01f)] private float maxHealth = 100f;

    public float MaxHealth => Mathf.Max(0.01f, maxHealth);
}
