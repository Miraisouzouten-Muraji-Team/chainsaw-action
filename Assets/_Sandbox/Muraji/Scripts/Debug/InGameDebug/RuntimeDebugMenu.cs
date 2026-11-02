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

    [Header("アクセル値の表示")]
    [Tooltip("値を表示するプレイヤーのChainsawAccelerator。未設定の場合はシーン内から自動取得します。")]
    [SerializeField] private ChainsawAccelerator accelerator;
    [Tooltip("メニューを閉じている間も、画面上部にアクセルの現在値・最大値・割合を表示します。")]
    [SerializeField] private bool showAccelerator = true;
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

    // 指定グループの当たり判定・Ray表示が有効か確認する。
    public static bool IsGroupVisible(string group)
    {
        return instance != null && instance.isActiveAndEnabled &&
            instance.visibleGroups.Contains(group ?? "");
    }

    // デバッグ設定と重複配置を確認し、表示対象のアクセルを取得する。
    private void Awake()
    {
        if (settings == null || (!Application.isEditor && !Debug.isDebugBuild && !settings.allowReleaseBuild))
        { enabled = false; return; }
        if (instance != null && instance != this)
        { enabled = false; return; }
        instance = this;
        showFPS = settings.showFPS;
        ResolveAccelerator();
    }

    // メニュー入力とFPSを更新し、対象が破棄された場合はFPS更新時にアクセルを再取得する。
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
            ResolveAccelerator();
            fps = frames / elapsed;
            milliseconds = 1000f * elapsed / frames;
            elapsed = 0f; frames = 0;
        }
    }

    // メニューの開閉に合わせてカーソルの表示状態とロックを切り替える。
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

    // メニューを閉じてカーソルを戻し、このメニューの共有参照を解除する。
    private void OnDisable()
    {
        SetOpen(false);
        if (instance == this) instance = null;
    }

    // FPS・アクセル値と、敵召喚・当たり判定・シーン移動の操作欄を描画する。
    private void OnGUI()
    {
        if (settings == null) return;
        if (showFPS) GUI.Box(new Rect(12, 12, 240, 28), $"FPS {fps:F1} / {milliseconds:F2} ms");
        if (showAccelerator)
            GUI.Box(new Rect(showFPS ? 264 : 12, 12, 300, 28), GetAcceleratorStatus());
        if (GUI.Button(new Rect(12, 44, 160, 30), "Debug [F1]")) SetOpen(!opened);
        if (!opened) return;
        GUILayout.BeginArea(new Rect(12, 80, Mathf.Min(440, Screen.width - 24),
            Mathf.Max(100, Screen.height - 92)), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("Runtime Debug");
        GUILayout.Label("Game continues while this menu is open.");
        showFPS = GUILayout.Toggle(showFPS, "Show FPS");
        showAccelerator = GUILayout.Toggle(showAccelerator, "アクセル値を画面上部に表示");
        GUILayout.Label(GetAcceleratorStatus());
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

    // 未設定または破棄された場合だけ、シーン内のアクセルを探す。Inspectorの指定を優先する。
    private void ResolveAccelerator()
    {
        if (accelerator != null) return;
        accelerator = Object.FindFirstObjectByType<ChainsawAccelerator>();
    }

    // アクセル本体の公開値を読み取り、現在値・最大値・通常上限に対する割合を表示する。
    private string GetAcceleratorStatus()
    {
        if (accelerator == null) return "アクセル：対象未取得";
        if (!accelerator.isActiveAndEnabled) return "アクセル：対象が無効です";
        return $"アクセル {accelerator.CurrentSpeed:F1} / {accelerator.MaxSpeed:F1}" +
            $"（{accelerator.SpeedRatio * 100f:F0}%）";
    }

    // 召喚数の上限を確認し、指定プレハブを召喚位置へ生成する。
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

    // ビルド設定内の指定シーンを非同期で読み込み、結果をメニューに表示する。
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
