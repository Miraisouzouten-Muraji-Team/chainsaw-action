using UnityEngine;

/// <summary>
/// EnemyのStateがCollision開始通知を受け取るための契約を定義する。
/// </summary>
/// <remarks>
/// OnCollisionEnterを必要とするStateだけが実装する。
///
/// Collisionの具体的な処理内容は定義せず、
/// Unityから受け取ったCollision情報をStateへ渡すための契約のみを担当する。
/// </remarks>
public interface IEnemyCollisionEnterReceiver
{
    /// <summary>
    /// Enemyが別のCollider / Rigidbodyとの接触を開始した際の
    /// Collision情報を受け取る。
    /// </summary>
    /// <param name="collision">
    /// Unityから通知されたCollision情報。
    /// </param>
    void HandleCollisionEnter(Collision collision);
}
