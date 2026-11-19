using UnityEngine;

/// <summary>
/// EnemyのStateが接触開始通知を受け取るための契約を定義する。
/// </summary>
/// <remarks>
/// 接触開始通知を必要とするStateだけが実装する。
///
/// Collisionの具体的な処理内容は定義せず、
/// 移動前QueryやTriggerから構築した接触情報をStateへ渡すための契約のみを担当する。
/// </remarks>
public interface IEnemyCollisionEnterReceiver
{
    /// <summary>
    /// Enemyが別のCollider / Rigidbodyとの接触を開始した際の
    /// 接触情報を受け取る。
    /// </summary>
    /// <param name="collision">
    /// EnemyTriggerMovementMotorが構築した接触情報。
    /// </param>
    void HandleCollisionEnter(EnemyContactInfo collision);
}
