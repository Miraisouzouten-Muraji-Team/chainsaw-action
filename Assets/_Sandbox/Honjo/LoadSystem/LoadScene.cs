using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadScene : MonoBehaviour
{
    [SerializeField] private float duration = 0.5f;
    float timer = 0f;
    [SerializeField] string loadScene = "";

    bool isPlaying = false;
    //シーン名で切り替え

    //[SerializeField] TransitionScript ts = null;
    public void LoadSceneNext(string sceneName)
    {
        isPlaying = true;
        loadScene = sceneName; 
        //if (ts) { ts.Close(); }
    }

    private void Update()
    {
        if (!isPlaying) return;

        timer += Time.unscaledDeltaTime;

        if (timer > duration)
        {
            isPlaying = false;
            SceneLoader.Load(loadScene);
        }
    }

    public void ReLoad()
    {
        isPlaying = true;
        loadScene = SceneManager.GetActiveScene().name;
        //if (ts) { ts.Close(); }
    }
}
