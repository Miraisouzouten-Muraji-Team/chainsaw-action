using UnityEngine;
using UnityEngine.SceneManagement;

public class ClearSceneChange : MonoBehaviour
{
    [SerializeField]
    private SceneManager sceneManager; // シーンマネージャー
    [SerializeField]
    private string nextSceneName; // 次のシーンの名前

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            // プレイヤーがゴールに触れたときの処理
            Debug.Log("Player has reached the goal!");
            // シーンをクリアする処理をここに追加
            SceneManager.LoadScene(nextSceneName); // 次のシーンに遷移する例
        }
    }
}
