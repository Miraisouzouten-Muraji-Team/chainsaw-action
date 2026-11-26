using System.Collections.Generic;
using UnityEngine;

/// 登録された敵をすべて倒すと達成される条件。
public class DefeatAllEnemiesCondition : GimmickConditionBase
{
    [Header("倒す必要がある敵")]
    [SerializeField]
    private List<EnemyDeathNotifier> targetEnemies
        = new List<EnemyDeathNotifier>();

    [Header("敵の自動登録用")]
    [Tooltip("ここを設定すると、このTransform以下の敵をInspectorから自動登録できます。")]
    [SerializeField]
    private Transform enemyRoot;

    private readonly HashSet<EnemyDeathNotifier> subscribedEnemies = new();

    private void OnEnable()
    {
        EnemyRespawnManager.LayerObjectsReset += HandleLayerObjectsReset;
        BindTargets(EnemyRespawnManager.FindForScene(gameObject.scene));
    }

    private void Start()
    {
        CheckCondition();
    }

    private void OnDisable()
    {
        EnemyRespawnManager.LayerObjectsReset -= HandleLayerObjectsReset;
        UnbindTargets();
    }

    private void HandleLayerObjectsReset(EnemyRespawnManager manager)
    {
        if (manager.gameObject.scene != gameObject.scene || IsCompleted) return;

        BindTargets(manager);
        CheckCondition();
    }

    private void BindTargets(EnemyRespawnManager manager)
    {
        UnbindTargets();
        foreach (EnemyDeathNotifier original in targetEnemies)
        {
            EnemyDeathNotifier enemy = manager != null ? manager.ResolveEnemyDeathNotifier(original) : original;
            if (enemy != null && subscribedEnemies.Add(enemy))
                enemy.Died += OnEnemyDied;
        }
    }

    private void UnbindTargets()
    {
        foreach (EnemyDeathNotifier enemy in subscribedEnemies)
            enemy.Died -= OnEnemyDied;

        subscribedEnemies.Clear();
    }

    /// 登録された敵が死亡したときに呼ばれる。
    private void OnEnemyDied(EnemyDeathNotifier enemy)
    {
        CheckCondition();
    }

    /// 対象の敵がすべて死亡しているか確認する。
    private void CheckCondition()
    {
        // 敵が1体も登録されていない場合は、
        // 誤作動防止のため条件達成にはしない。
        if (subscribedEnemies.Count == 0)
        {
            return;
        }

        foreach (EnemyDeathNotifier enemy in subscribedEnemies)
        {
            // nullになっている敵は無視する。
            if (enemy == null)
            {
                continue;
            }

            // まだ生きている敵が1体でもいれば条件未達成。
            if (!enemy.IsDead)
            {
                return;
            }
        }

        // 全員死亡していたら条件達成。
        CompleteCondition();
    }

    /// Inspectorの右クリックメニューから実行できます。
    /// enemyRoot以下に存在するEnemyDeathNotifierを
    /// targetEnemiesへまとめて登録します。
    [ContextMenu("Enemy Root から敵を自動登録")]
    private void CollectEnemiesFromRoot()
    {
        if (enemyRoot == null)
        {
            Debug.LogWarning(
                "Enemy Root が設定されていません。",
                this
            );

            return;
        }

        targetEnemies.Clear();

        EnemyDeathNotifier[] enemies =
            enemyRoot.GetComponentsInChildren<EnemyDeathNotifier>(true);

        targetEnemies.AddRange(enemies);

        Debug.Log(
            $"{targetEnemies.Count}体の敵を登録しました。",
            this
        );
    }
}
