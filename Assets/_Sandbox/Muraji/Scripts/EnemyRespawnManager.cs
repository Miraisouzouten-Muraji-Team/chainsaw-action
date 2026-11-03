using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 指定Layerの初期配置を自動保存し、復活時に対象ルートを子ごと作り直す。
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class EnemyRespawnManager : MonoBehaviour
{
    [Tooltip("再生前に、復活時に開始状態へ戻すLayerを選択します。個々の敵・Prefabの登録は不要です。対象のルートと、そのすべての子をまとめて初期化します。プレイヤー・この管理オブジェクトは除外します。")]
    [SerializeField] private LayerMask resetLayers;

    private sealed class Snapshot
    {
        public GameObject Template;
        public GameObject Current;
        public Transform Parent;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public Vector3 WorldPosition;
        public Quaternion WorldRotation;
        public Vector3 WorldScale;
        public int SiblingIndex;
        public string Name;
        public bool Active;
    }

    private readonly List<Snapshot> snapshots = new List<Snapshot>();
    private GameObject snapshotStorage;
    private int capturedLayers;
    private bool captured;
    private bool resetting;

    // 同じシーンの有効な管理コンポーネントを自動取得し、個別の参照登録を不要にする。
    public static EnemyRespawnManager FindForScene(Scene scene)
    {
        EnemyRespawnManager result = null;
        foreach (EnemyRespawnManager manager in Object.FindObjectsByType<EnemyRespawnManager>(
            FindObjectsSortMode.None))
        {
            if (!manager.isActiveAndEnabled || manager.gameObject.scene != scene) continue;
            if (result != null)
            {
                Debug.LogError("Layer初期化：同じシーンに有効なEnemyRespawnManagerを複数配置しないでください。", manager);
                return null;
            }
            result = manager;
        }
        return result;
    }

    // 通常の敵Awakeより前に、動作しない保管用階層へ初期状態を複製する。
    private void Awake()
    {
        if (FindForScene(gameObject.scene) != this)
        {
            enabled = false;
            return;
        }
        capturedLayers = resetLayers.value;
        if (capturedLayers == 0)
        {
            Debug.LogWarning("Layer初期化：Reset Layersに初期化したいLayerを指定してください。再生開始前に設定します。", this);
            return;
        }

        snapshotStorage = new GameObject("Respawn Initial Snapshots");
        snapshotStorage.SetActive(false);
        SceneManager.MoveGameObjectToScene(snapshotStorage, gameObject.scene);
        snapshotStorage.hideFlags = HideFlags.HideInHierarchy;

        foreach (GameObject target in CollectTargetRoots())
        {
            Transform source = target.transform;
            // 非アクティブの親をInstantiate時に渡し、保存コピーのAwake/OnEnableを実行させない。
            GameObject template = Instantiate(target, snapshotStorage.transform, false);
            snapshots.Add(new Snapshot
            {
                Template = template,
                Current = target,
                Parent = source.parent,
                LocalPosition = source.localPosition,
                LocalRotation = source.localRotation,
                LocalScale = source.localScale,
                WorldPosition = source.position,
                WorldRotation = source.rotation,
                WorldScale = source.lossyScale,
                SiblingIndex = source.GetSiblingIndex(),
                Name = target.name,
                Active = target.activeSelf
            });
        }
        captured = true;
        if (snapshots.Count == 0)
            Debug.LogWarning("Layer初期化：指定Layerに保存対象がありません。対象ルートのLayerとReset Layersを確認してください。", this);
    }

    // 対象Layerの最上位だけを集め、親子が同じLayerでも二重に保存・再生成しない。
    private List<GameObject> CollectTargetRoots()
    {
        List<GameObject> result = new List<GameObject>();
        foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
        {
            if (sceneRoot == snapshotStorage) continue;
            foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTarget(candidate.gameObject)) continue;
                bool hasTargetParent = false;
                for (Transform parent = candidate.parent; parent != null; parent = parent.parent)
                {
                    if (!IsTarget(parent.gameObject)) continue;
                    hasTargetParent = true;
                    break;
                }
                if (!hasTargetParent) result.Add(candidate.gameObject);
            }
        }
        return result;
    }

    // Layer一致を確認し、復活処理本体・プレイヤー・保存コピーを置き換え対象から除外する。
    private bool IsTarget(GameObject target)
    {
        if (target == null || (capturedLayers & (1 << target.layer)) == 0) return false;
        Transform candidate = target.transform;
        if (snapshotStorage != null && candidate.IsChildOf(snapshotStorage.transform)) return false;
        if (candidate == transform || candidate.IsChildOf(transform) || transform.IsChildOf(candidate)) return false;
        if (target.GetComponentInParent<SensorPlayerController>(true) != null ||
            target.GetComponentInChildren<SensorPlayerController>(true) != null ||
            target.GetComponentInParent<PlayerRespawn>(true) != null ||
            target.GetComponentInChildren<PlayerRespawn>(true) != null ||
            target.GetComponentInChildren<EnemyRespawnManager>(true) != null) return false;
        return true;
    }

    // 現在の対象を停止・破棄し、開始時のコピーから全個体を復元する。途中生成の対象は除去する。
    public void ResetLayerObjects()
    {
        if (!isActiveAndEnabled || !captured || resetting || snapshotStorage == null) return;
        resetting = true;
        List<GameObject> replacements = new List<GameObject>();
        try
        {
            HashSet<GameObject> removal = new HashSet<GameObject>(CollectTargetRoots());
            // 死亡後にLayerが変更された個体も、開始時から管理している範囲として片付ける。
            foreach (Snapshot snapshot in snapshots)
                if (snapshot.Current != null) removal.Add(snapshot.Current);
            foreach (GameObject target in removal)
            {
                if (target == null) continue;
                target.SetActive(false);
                // Destroyの実行待ちの旧ルートを退避し、新しい個体の親・兄弟順へ混ぜない。
                target.transform.SetParent(snapshotStorage.transform, true);
                Destroy(target);
            }

            // 全対象の位置・親・スケールを先に戻してから有効化し、初期化時の配置をそろえる。
            foreach (Snapshot snapshot in snapshots)
            {
                GameObject restored = Instantiate(snapshot.Template, snapshotStorage.transform, false);
                restored.SetActive(false);
                restored.name = snapshot.Name;
                Transform target = restored.transform;
                Transform restoreParent = snapshot.Parent;
                for (Transform parent = restoreParent; parent != null; parent = parent.parent)
                {
                    // 親自身が途中で対象Layerへ変わった場合、破棄予定の親へ新個体を入れない。
                    if (!removal.Contains(parent.gameObject)) continue;
                    restoreParent = null;
                    break;
                }
                target.SetParent(restoreParent, false);
                if (restoreParent != null)
                {
                    target.localPosition = snapshot.LocalPosition;
                    target.localRotation = snapshot.LocalRotation;
                    target.localScale = snapshot.LocalScale;
                }
                else
                {
                    target.SetPositionAndRotation(snapshot.WorldPosition, snapshot.WorldRotation);
                    target.localScale = snapshot.WorldScale;
                }
                target.SetSiblingIndex(snapshot.SiblingIndex);
                snapshot.Current = restored;
                replacements.Add(restored);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < replacements.Count; i++)
                replacements[i].SetActive(snapshots[i].Active);
        }
        finally
        {
            resetting = false;
        }
    }

    // 旧版のUnityEventから呼ばれても、現在のLayer単位の初期化へ接続する。
    public void RestoreDefeatedEnemies() => ResetLayerObjects();

    // 管理オブジェクトを破棄したときは、動作しない保管コピーもまとめて片付ける。
    private void OnDestroy()
    {
        if (snapshotStorage != null) Destroy(snapshotStorage);
        snapshots.Clear();
    }
}
