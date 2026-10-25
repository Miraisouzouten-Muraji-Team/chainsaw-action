using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class RuntimeDebugMenu : MonoBehaviour
{
    public RuntimeDebugSettings settings;
    [Tooltip("現在のシーン内の召喚位置。移動先シーンにもメニューを配置してください。")]
    public Transform spawnPoint;
    public Vector3 spawnOffset = Vector3.zero;
    [Min(1)] public int maximumSpawnedEnemies = 50;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly HashSet<string> visibleGroups = new HashSet<string>();
    private static RuntimeDebugMenu instance;
    private bool opened, loading, showFPS;
    private float elapsed, fps, milliseconds;
    private int frames;
    private Vector2 scroll;
    private string message = "";
    private CursorLockMode previousLock;
    private bool previousVisible;

    public static bool IsGroupVisible(string group)
    {
        return instance != null && instance.isActiveAndEnabled &&
            instance.visibleGroups.Contains(group ?? "");
    }

    private void Awake()
    {
        if (settings == null || (!Application.isEditor && !Debug.isDebugBuild && !settings.allowReleaseBuild))
        { enabled = false; return; }
        if (instance != null && instance != this)
        { enabled = false; return; }
        instance = this;
        showFPS = settings.showFPS;
    }

    private void Update()
    {
        bool toggle = false;
#if ENABLE_INPUT_SYSTEM
        toggle = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        toggle = Input.GetKeyDown(KeyCode.F1);
#endif
        if (toggle) SetOpen(!opened);
        elapsed += Time.unscaledDeltaTime;
        frames++;
        if (elapsed >= Mathf.Max(0.1f, settings.fpsInterval))
        {
            fps = frames / elapsed;
            milliseconds = 1000f * elapsed / frames;
            elapsed = 0f; frames = 0;
        }
    }

    private void SetOpen(bool value)
    {
        if (opened == value) return;
        opened = value;
        if (opened)
        {
            previousLock = Cursor.lockState; previousVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else { Cursor.lockState = previousLock; Cursor.visible = previousVisible; }
    }

    private void OnDisable()
    {
        SetOpen(false);
        if (instance == this) instance = null;
    }

    private void OnGUI()
    {
        if (settings == null) return;
        if (showFPS) GUI.Box(new Rect(12, 12, 240, 28), $"FPS {fps:F1} / {milliseconds:F2} ms");
        if (GUI.Button(new Rect(12, 44, 160, 30), "Debug [F1]")) SetOpen(!opened);
        if (!opened) return;
        GUILayout.BeginArea(new Rect(12, 80, Mathf.Min(440, Screen.width - 24),
            Mathf.Max(100, Screen.height - 92)), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("Runtime Debug");
        GUILayout.Label("Game continues while this menu is open.");
        showFPS = GUILayout.Toggle(showFPS, "Show FPS");
        GUILayout.Space(8);
        GUILayout.Label("Spawn enemies");
        GUI.enabled = !loading && spawnPoint != null;
        if (settings.enemies != null)
            foreach (var entry in settings.enemies)
            {
                if (entry == null || entry.prefab == null) continue;
                if (GUILayout.Button(string.IsNullOrEmpty(entry.label) ? entry.prefab.name : entry.label))
                    Spawn(entry.prefab);
            }
        GUI.enabled = !loading;
        if (spawnPoint == null) GUILayout.Label("Assign Spawn Point in this scene.");
        if (GUILayout.Button("Remove debug-spawned enemies"))
        {
            foreach (var enemy in spawned) if (enemy != null) Destroy(enemy);
            spawned.Clear();
        }
        GUILayout.Space(8);
        GUILayout.Label("Collider / ray groups");
        if (settings.groups != null)
            foreach (var group in settings.groups)
            {
                if (string.IsNullOrEmpty(group)) continue;
                bool next = GUILayout.Toggle(visibleGroups.Contains(group), group);
                if (next) visibleGroups.Add(group); else visibleGroups.Remove(group);
            }
        GUILayout.Space(8);
        GUILayout.Label("Scenes (enabled build scenes)");
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (GUILayout.Button(System.IO.Path.GetFileNameWithoutExtension(path))) LoadScene(i);
        }
        GUI.enabled = true;
        GUILayout.Label(message);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void Spawn(GameObject prefab)
    {
        spawned.RemoveAll(item => item == null);
        if (spawned.Count >= Mathf.Max(1, maximumSpawnedEnemies))
        { message = "Spawn limit reached."; return; }
        var enemy = Instantiate(prefab, spawnPoint.position + spawnOffset, spawnPoint.rotation);
        enemy.SetActive(true);
        spawned.Add(enemy);
        message = "Spawned: " + prefab.name;
    }

    private void LoadScene(int index)
    {
        if (loading) return;
        loading = true;
        try
        {
            var operation = SceneManager.LoadSceneAsync(index, LoadSceneMode.Single);
            if (operation == null) { loading = false; message = "Scene load failed."; }
            else message = "Loading...";
        }
        catch (System.Exception exception)
        { loading = false; message = exception.Message; Debug.LogException(exception, this); }
    }
}
