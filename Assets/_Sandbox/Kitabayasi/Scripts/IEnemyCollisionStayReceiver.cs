using UnityEngine;

/// <summary>
/// EnemyのStateがCollision継続通知を受け取るための契約を定義する。
/// </summary>
/// <remarks>
/// OnCollisionStayを必要とするStateだけが実装する。
///
/// Collisionの具体的な処理内容は定義せず、
/// Unityから受け取ったCollision情報をStateへ渡すための契約のみを担当する。
/// </remarks>
public interface IEnemyCollisionStayReceiver
{
    /// <summary>
    /// Enemyが別のCollider / Rigidbodyと接触している間の
    /// Collision情報を受け取る。
    /// </summary>
    /// <param name="collision">
    /// Unityから通知されたCollision情報。
    /// </param>
    void HandleCollisionStay(Collision collision);
}
