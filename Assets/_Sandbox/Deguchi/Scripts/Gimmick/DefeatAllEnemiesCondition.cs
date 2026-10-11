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

    private void OnEnable()
    {
        // 登録されている敵すべての死亡イベントを監視する。
        foreach (EnemyDeathNotifier enemy in targetEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            enemy.Died += OnEnemyDied;
        }
    }

    private void Start()
    {
        // 開始時点ですでに敵が死亡している可能性もあるので、
        // 最初に一度チェックする。
        CheckCondition();
    }

    private void OnDisable()
    {
        // イベントの登録解除。
        // オブジェクト破棄時などに不要な参照が残るのを防ぎます。
        foreach (EnemyDeathNotifier enemy in targetEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            enemy.Died -= OnEnemyDied;
        }
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
        if (targetEnemies.Count == 0)
        {
            return;
        }

        foreach (EnemyDeathNotifier enemy in targetEnemies)
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
