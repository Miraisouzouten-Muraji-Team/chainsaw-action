
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class LoadScene : MonoBehaviour
{
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private string loadScene = "";

    private float timer = 0f;
    private bool isPlaying = false;

    // シーン名を指定して切り替え
    public void LoadSceneNext(string sceneName)
    {
        if (isPlaying) return;

        isPlaying = true;
        timer = 0f;
        loadScene = sceneName;
    }

    private void Update()
    {
        // Rキーが押された瞬間に現在のシーンをリロード
        if (Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            ReLoad();
        }

        if (!isPlaying) return;

        timer += Time.unscaledDeltaTime;

        if (timer >= duration)
        {
            isPlaying = false;
            SceneLoader.Load(loadScene);
        }
    }

    // 現在のシーンをリロード
    public void ReLoad()
    {
        if (isPlaying) return;

        isPlaying = true;
        timer = 0f;
        loadScene = SceneManager.GetActiveScene().name;
    }
}
