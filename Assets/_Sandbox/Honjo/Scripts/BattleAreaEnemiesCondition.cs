
using System.Collections.Generic;
using UnityEngine;

public class BattleAreaEnemiesCondition : GimmickConditionBase
{
    [Header("エリア設定")]
    [SerializeField]
    private EnemySpawnArea esa = null;

    [SerializeField]
    private Collider areaTrigger;

    [Header("初期配置された敵")]
    [SerializeField]
    private List<EnemyHealth> areaAllEnemies = new();

    [Header("敵の検出設定")]
    [Tooltip("トリガー内を検索する間隔（秒）")]
    [SerializeField, Min(0.05f)]
    private float scanInterval = 0.2f;

    [Tooltip("検索対象のレイヤー")]
    [SerializeField]
    private LayerMask enemyLayerMask = ~0;

    [Header("全滅判定")]
    [Tooltip("最後の敵が倒されてから全滅を確定するまでの猶予時間")]
    [SerializeField, Min(0f)]
    private float completionCheckDelay = 1.5f;

    // エリア内で検知した敵。エリア外に出ても保持する
    private readonly HashSet<EnemyHealth> areaAllEnemy = new();

    // 生存中の敵
    private readonly HashSet<EnemyHealth> aliveEnemies = new();

    // 死亡イベントを登録済みの敵
    private readonly HashSet<EnemyHealth> subscribedEnemies = new();

    // 死亡イベント解除用
    private readonly Dictionary<EnemyHealth, System.Action> deathHandlers = new();

    // OverlapBoxNonAlloc用バッファ
    private readonly Collider[] detectedColliders = new Collider[256];

    private float scanTimer;

    // 1体以上の敵を登録したか
    private bool hasRegisteredAnyEnemy;

    // 条件達成済みか
    private bool hasTriggered;

    private void Awake()
    {
        if (esa == null)
        {
            esa = GetComponent<EnemySpawnArea>();
        }

        if (areaTrigger == null)
        {
            areaTrigger = GetComponent<Collider>();
        }

        if (areaTrigger == null)
        {
            Debug.LogError(
                $"{name}: エリア用のColliderが設定されていません。",
                this
            );
        }
        else if (!areaTrigger.isTrigger)
        {
            Debug.LogWarning(
                $"{name}: エリア用ColliderのIs TriggerがOFFです。",
                this
            );
        }
    }

    private void OnEnable()
    {
        RegisterInitialEnemies();
    }

    private void Start()
    {
        // 初回の検索をすぐに実行
        ScanEnemiesInArea();

        CheckAllEnemiesDefeated();
    }

    private void Update()
    {
        if (hasTriggered || areaTrigger == null)
        {
            return;
        }

        scanTimer += Time.deltaTime;

        if (scanTimer < scanInterval)
        {
            return;
        }

        scanTimer = 0f;

        ScanEnemiesInArea();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(CheckAllEnemiesDefeated));

        foreach (var pair in deathHandlers)
        {
            if (pair.Key != null)
            {
                pair.Key.Died -= pair.Value;
            }
        }

        deathHandlers.Clear();
        subscribedEnemies.Clear();
        aliveEnemies.Clear();
        areaAllEnemy.Clear();

        hasRegisteredAnyEnemy = false;
        scanTimer = 0f;
    }

    /// <summary>
    /// Inspectorで指定した初期配置の敵を登録する
    /// </summary>
    private void RegisterInitialEnemies()
    {
        foreach (EnemyHealth enemy in areaAllEnemies)
        {
            RegisterEnemy(enemy);
        }
    }

    /// <summary>
    /// トリガー範囲内にいる敵を検索して登録する
    /// </summary>
    private void ScanEnemiesInArea()
    {
        if (areaTrigger == null || hasTriggered)
        {
            return;
        }

        Bounds bounds = areaTrigger.bounds;

        // ColliderのBoundsを利用して範囲内のColliderを検索
        // 回転したBox Colliderでは、範囲が広めに検索される
        int count = Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            detectedColliders,
            Quaternion.identity,
            enemyLayerMask,
            QueryTriggerInteraction.Collide
        );

        // バッファが満杯の場合は警告
        if (count >= detectedColliders.Length)
        {
            Debug.LogWarning(
                $"{name}: 敵の検索バッファが満杯です。検出漏れの可能性があります。",
                this
            );
        }

        for (int i = 0; i < count; i++)
        {
            Collider detected = detectedColliders[i];

            if (detected == null || detected == areaTrigger)
            {
                continue;
            }

            // 子Colliderから親のEnemyHealthも検索
            EnemyHealth enemy =
                detected.GetComponentInParent<EnemyHealth>();

            if (enemy == null)
            {
                continue;
            }

            RegisterEnemy(enemy);
        }
    }

    /// <summary>
    /// 敵を管理対象に登録する
    /// </summary>
    private void RegisterEnemy(EnemyHealth enemy)
    {
        if (enemy == null || hasTriggered)
        {
            return;
        }

        // HashSetによって重複登録を防止
        if (!areaAllEnemy.Add(enemy))
        {
            return;
        }

        hasRegisteredAnyEnemy = true;

        // 死亡イベントを登録
        if (subscribedEnemies.Add(enemy))
        {
            System.Action handler = () => HandleEnemyDied(enemy);

            deathHandlers.Add(enemy, handler);
            enemy.Died += handler;
        }

        // 生存状態を登録
        if (!enemy.IsDead)
        {
            aliveEnemies.Add(enemy);

            // 新しい生存敵を検知したので全滅判定をキャンセル
            CancelInvoke(nameof(CheckAllEnemiesDefeated));
        }
        else
        {
            aliveEnemies.Remove(enemy);
        }

        Debug.Log(
            $"{name}: 敵を登録しました。敵={enemy.name}, " +
            $"登録数={areaAllEnemy.Count}, 生存数={aliveEnemies.Count}",
            enemy
        );

        CheckAllEnemiesDefeated();
    }

    /// <summary>
    /// 敵の死亡イベント
    /// </summary>
    private void HandleEnemyDied(EnemyHealth enemy)
    {
        aliveEnemies.Remove(enemy);

        Debug.Log(
            $"{name}: 敵の死亡を検知しました。敵={enemy.name}, " +
            $"残り生存数={aliveEnemies.Count}",
            this
        );

        CancelInvoke(nameof(CheckAllEnemiesDefeated));

        if (completionCheckDelay <= 0f)
        {
            CheckAllEnemiesDefeated();
        }
        else
        {
            Invoke(
                nameof(CheckAllEnemiesDefeated),
                completionCheckDelay
            );
        }
    }

    /// <summary>
    /// 管理対象の敵が全滅したか確認する
    /// </summary>
    private void CheckAllEnemiesDefeated()
    {
        if (hasTriggered ||
            !hasRegisteredAnyEnemy ||
            aliveEnemies.Count > 0)
        {
            return;
        }

        hasTriggered = true;

        CancelInvoke(nameof(CheckAllEnemiesDefeated));

        Debug.Log(
            $"{name}: エリア内の敵の全滅を検知しました。",
            this
        );

        CompleteCondition();
    }
}
