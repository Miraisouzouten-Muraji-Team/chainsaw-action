using System.Collections.Generic;
using UnityEngine;

//設定画面を開いているときのスクリプト
//オプションを開いている間はゲームを停止させる

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    private readonly HashSet<int> pauseOwners = new();

    private float previousTimeScale = 1f;

    public bool IsPaused => pauseOwners.Count > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
    }

    public void RequestPause(Object owner)
    {
        int id = owner.GetInstanceID();

        if (!pauseOwners.Add(id))
            return;

        if (pauseOwners.Count == 1)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
    }

    public void ReleasePause(Object owner)
    {
        int id = owner.GetInstanceID();

        pauseOwners.Remove(id);

        if (pauseOwners.Count == 0)
        {
            Time.timeScale = previousTimeScale;
        }
    }
}