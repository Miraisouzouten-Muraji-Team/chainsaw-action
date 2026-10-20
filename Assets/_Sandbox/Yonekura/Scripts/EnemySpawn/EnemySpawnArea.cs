using System.Collections.Generic;
using UnityEngine;

public class EnemySpawnArea : MonoBehaviour
{
    [Header("初期配置された敵")]
    [SerializeField]
    private List<EnemyHealth> initialEnemies = new();

    [Header("このエリアのスポナー")]
    [SerializeField]
    private List<EnemySpawner> spawners = new();

    private readonly HashSet<EnemyHealth> aliveEnemies = new();
    private readonly HashSet<EnemyHealth> subscribedEnemies = new();
    private readonly Dictionary<EnemyHealth, System.Action> deathHandlers = new();

    private bool hasInitialEnemies;
    private bool hasTriggered;

    private void OnEnable()
    {
        RegisterInitialEnemies();
    }

    private void Start()
    {
        CheckAllEnemiesDefeated();
    }

    private void OnDisable()
    {
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
    }

    private void RegisterInitialEnemies()
    {
        hasInitialEnemies = false;

        foreach (EnemyHealth enemy in initialEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            hasInitialEnemies = true;

            if (subscribedEnemies.Add(enemy))
            {
                System.Action handler = () => HandleEnemyDied(enemy);
                deathHandlers.Add(enemy, handler);
                enemy.Died += handler;
            }

            if (!enemy.IsDead)
            {
                aliveEnemies.Add(enemy);
            }
        }
    }

    private void HandleEnemyDied(EnemyHealth enemy)
    {
        aliveEnemies.Remove(enemy);
        CheckAllEnemiesDefeated();
    }

    private void CheckAllEnemiesDefeated()
    {
        if (hasTriggered || !hasInitialEnemies || aliveEnemies.Count > 0)
        {
            return;
        }

        hasTriggered = true;

        foreach (EnemySpawner spawner in spawners)
        {
            if (spawner != null)
            {
                spawner.SpawnOnce();
            }
        }

        Debug.Log($"{name}: 初期配置敵の全滅を検知しました。", this);
    }
}
