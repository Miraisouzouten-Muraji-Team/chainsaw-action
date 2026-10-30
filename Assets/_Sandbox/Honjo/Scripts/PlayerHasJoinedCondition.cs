using UnityEngine;

/// プレイヤーがトリガー内に入ってきたら扉を動かす
public class PlayerHasJoinedCondition : GimmickConditionBase
{
    string m_tag = "Player";
    private void OnTriggerEnter(Collider other)
    {
        if (! other.CompareTag(m_tag)) { return; }
        CompleteCondition();
    }
}
