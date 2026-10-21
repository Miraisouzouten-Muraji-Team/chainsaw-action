using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

// 敵1体ごとの生成設定
[Serializable]
public class EnemySpawnEntry
{
    public enum PlacementMode
    {
        Simple,
        Detailed
    }

    [Header("敵の設定")]
    [Tooltip("この枠で生成する敵のPrefab")]
    public GameObject enemyPrefab;

    [Header("配置設定")]
    [Tooltip("簡易配置または詳細配置")]
    public PlacementMode placementMode = PlacementMode.Simple;

    [Tooltip("スポナー位置からのX・Y方向のずれ")]
    public Vector2 offset = Vector2.zero;

    [Tooltip("指定したオブジェクトのX・Y座標を使用")]
    public Transform spawnPoint;
}

public class EnemySpawner : MonoBehaviour
{
    [Header("スポーン設定")]
    [Tooltip("生成するエネミーの数")]
    [SerializeField, Min(1)]
    private int enemyCount = 1;

    [Tooltip("生成する敵を1体ずつ登録")]
    [SerializeField]
    private List<EnemySpawnEntry> enemies =
        new List<EnemySpawnEntry>();

    [Header("時間差スポーン")]
    [Tooltip("ONにすると、敵を1体ずつ時間差で生成する")]
    [SerializeField]
    private bool useSpawnDelay = false;

    [Tooltip("敵を1体生成してから次の敵を生成するまでの秒数")]
    [SerializeField, Min(0f)]
    private float spawnInterval = 0.5f;

    [Header("出現演出")]
    [Tooltip("敵が完成するまでの時間")]
    [SerializeField, Min(0f)]
    private float appearanceDuration = 1.0f;

    private bool hasSpawned;

    public int EnemyCount => enemyCount;
    public List<EnemySpawnEntry> Enemies => enemies;

    // EnemySpawnAreaから呼び出される
    public void SpawnOnce()
    {
        if (hasSpawned)
        {
            return;
        }

        // 登録数が足りない場合は生成を開始しない
        if (enemies == null || enemies.Count < enemyCount)
        {
            Debug.LogError(
                $"{name}: 敵の登録数が足りません。",
                this);
            return;
        }

        hasSpawned = true;

        // 同時生成・時間差生成の処理を開始
        SpawnEnemiesAsync().Forget();
    }

    // 敵を順番に生成する
    private async UniTask SpawnEnemiesAsync()
    {
        bool hasSpawnedAny = false;

        for (int i = 0; i < enemyCount; i++)
        {
            EnemySpawnEntry entry = enemies[i];

            // Prefabが未設定なら、この枠はスキップ
            if (entry == null || entry.enemyPrefab == null)
            {
                Debug.LogWarning(
                    $"{name}: エネミー {i + 1} のPrefabが未設定です。",
                    this);
                continue;
            }

            Vector3 spawnPosition = transform.position;
            Quaternion spawnRotation = transform.rotation;

            // 1体目は必ずスポナーの位置から生成
            if (i > 0)
            {
                if (entry.placementMode ==
                    EnemySpawnEntry.PlacementMode.Simple)
                {
                    // スポナー位置からX・Yだけずらす
                    spawnPosition = transform.position +
                        new Vector3(
                            entry.offset.x,
                            entry.offset.y,
                            0f);
                }
                else
                {
                    // 詳細配置では指定した位置を使用
                    if (entry.spawnPoint == null)
                    {
                        Debug.LogWarning(
                            $"{name}: エネミー {i + 1} の" +
                            "スポーンポイントが未設定です。",
                            this);
                        continue;
                    }

                    spawnPosition = new Vector3(
                        entry.spawnPoint.position.x,
                        entry.spawnPoint.position.y,
                        transform.position.z);

                    spawnRotation = entry.spawnPoint.rotation;
                }
            }

            // 時間差設定がONの場合のみ待機する
            // 最初に生成できた敵は待たずに生成する
            if (useSpawnDelay && hasSpawnedAny && spawnInterval > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(spawnInterval));
            }

            // 敵を生成
            GameObject spawnedEnemy = Instantiate(
                entry.enemyPrefab,
                spawnPosition,
                spawnRotation);

            // 出現中はAIを停止
            EnemyStateMachine stateMachine =
                spawnedEnemy.GetComponent<EnemyStateMachine>();

            if (stateMachine != null)
            {
                stateMachine.enabled = false;
            }

            // ディゾルブ制御を取得
            EnemyDissolveController dissolveController =
                spawnedEnemy.GetComponent<EnemyDissolveController>();

            if (dissolveController == null)
            {
                Debug.LogError(
                    $"{spawnedEnemy.name}: " +
                    "EnemyDissolveControllerがありません。",
                    spawnedEnemy);

                // 演出用コンポーネントがない場合はAIを再開する
                if (stateMachine != null)
                {
                    stateMachine.enabled = true;
                }

                continue;
            }

            // ディゾルブ完了後にAIを再開
            PlayAppearanceAsync(
                spawnedEnemy,
                stateMachine,
                dissolveController).Forget();

            hasSpawnedAny = true;
        }
    }

    // ディゾルブ完了後にAIを再開する
    private async UniTaskVoid PlayAppearanceAsync(
        GameObject spawnedEnemy,
        EnemyStateMachine stateMachine,
        EnemyDissolveController dissolveController)
    {
        // 出現演出が終わるまで待機する
        await dissolveController.PlayAppearAsync(
            appearanceDuration);

        // 演出中に敵が削除されていたら終了
        if (spawnedEnemy == null)
        {
            return;
        }

        //  完全に表示された後でAIを再開する
        if (stateMachine != null)
        {
            stateMachine.enabled = true;
        }
    }
}
