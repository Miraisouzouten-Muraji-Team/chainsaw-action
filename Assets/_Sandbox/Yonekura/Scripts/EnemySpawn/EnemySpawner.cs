using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

[Serializable]
/// <summary>
/// 敵の生成位置や生成数、生成間隔を管理し、
/// 敵ごとの出現演出を制御する。
/// </summary>
public class EnemySpawnEntry
{
    /// <summary>
    /// 敵の配置方法。
    /// </summary>
    public enum PlacementMode
    {
        Simple,      // スポナーからの相対位置を指定
        Detailed     // 専用のスポーンポイントを指定
    }

    /// <summary>
    /// エネミーごとの出現演出
    /// <summary>
    public enum AppearanceType
    {
        None,        // 演出なし
        Dissolve     // ディゾルブ演出
    }

    [Header("敵の設定")]
    [Tooltip("この枠で生成する敵のPrefab")]
    public GameObject enemyPrefab;

    [Tooltip("このエネミーに適用する出現演出")]
    public AppearanceType appearanceType = AppearanceType.Dissolve;

    [Header("配置設定")]
    [Tooltip("簡易配置または詳細配置")]
    public PlacementMode placementMode = PlacementMode.Simple;

    [Tooltip("スポナー位置からのX・Y方向のずれ")]
    public Vector2 offset = Vector2.zero;

    [Tooltip("詳細配置で使用するスポーンポイント")]
    public Transform spawnPoint;
}

/// <summary>
/// 敵の生成と出現演出の開始を管理する
/// <summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("ディゾルブ設定")]
    [Tooltip("エネミーの出現時に使用するディゾルブ用マテリアル")]
    [SerializeField]
    private Material dissolveMaterial;

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

    [Tooltip("敵が完成するまでの時間")]
    [SerializeField, Min(0f)]
    private float appearanceDuration = 1.0f;

    // 同じスポナーからの重複生成を防ぐ
    private bool hasSpawned;

    // 外部のスクリプトから生成設定を参照するためのプロパティ
    public int EnemyCount => enemyCount;
    public List<EnemySpawnEntry> Enemies => enemies;

    /// <summary>
    /// EnemySpawnAreaから呼び出される
    /// </summary>
    public void SpawnOnce()
    {
        // すでに生成済みなら何もしない
        if (hasSpawned)
        {
            return;
        }

        // 敵の設定数が不足している場合は生成を開始しない
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

    // 登録された敵を順番に生成
    private async UniTask SpawnEnemiesAsync()
    {
        // 実際に1体以上生成できたかを記録
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

            // デフォルトの生成位置と向きはスポナーに合わせる
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

                    // X・Yは指定位置、Zはスポナーの位置を使用
                    spawnPosition = new Vector3(
                        entry.spawnPoint.position.x,
                        entry.spawnPoint.position.y,
                        transform.position.z);

                    spawnRotation = entry.spawnPoint.rotation;
                }
            }

            // 2体目以降は、時間差設定がONの場合のみ待機する
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

            hasSpawnedAny = true;

            // ディゾルブ演出が選択されている場合だけ再生
            if (entry.appearanceType ==
                EnemySpawnEntry.AppearanceType.Dissolve &&
                dissolveMaterial != null)
            {
                EnemyDissolveManager.Instance
                    .PlayAppearAsync(
                        spawnedEnemy,
                        dissolveMaterial,
                        appearanceDuration)
                    .Forget();
            }
        }
    }
}
