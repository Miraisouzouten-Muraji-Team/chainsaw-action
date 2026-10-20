using UnityEngine;
using Cysharp.Threading.Tasks;

// 指定した位置から敵を一度だけ生成する
public class EnemySpawner : MonoBehaviour
{
    [Header("スポーン設定")]
    [Tooltip("生成する敵のPrefab")]
    [SerializeField]
    private GameObject enemyPrefab;

    [Tooltip("生成位置。未設定ならこのオブジェクトの位置を使う")]
    [SerializeField]
    private Transform spawnPoint;

    [Header("出現演出")]
    [Tooltip("敵が完成するまでの時間")]
    [SerializeField]
    private float appearanceDuration = 1.0f;

    private bool hasSpawned;

    // EnemySpawnAreaから呼び出される
    public void SpawnOnce()
    {
        if (hasSpawned)
        {
            return;
        }

        if (enemyPrefab == null)
        {
            Debug.LogError(
                $"{name}: Enemy Prefabが設定されていません。",
                this);
            return;
        }

        // 生成位置が未設定ならスポナー自身を使う
        Transform point = spawnPoint != null
            ? spawnPoint
            : transform;

        // 二重生成を防止
        hasSpawned = true;

        // 指定した位置に敵を生成
        GameObject spawnedEnemy = Instantiate(
            enemyPrefab,
            point.position,
            point.rotation
        );

        // 敵のAIを取得して停止
        EnemyStateMachine stateMachine =
            spawnedEnemy.GetComponent<EnemyStateMachine>();

        if (stateMachine != null)
        {
            stateMachine.enabled = false;
        }
        else
        {
            Debug.LogWarning(
                $"{spawnedEnemy.name}: EnemyStateMachineが見つかりません。",
                spawnedEnemy);
        }

        // 出現演出は次の手順で実装する
        PlayAppearanceAsync(spawnedEnemy, stateMachine).Forget();

        Debug.Log(
            $"{name}: {spawnedEnemy.name} を生成しました。",
            this);
    }

    // 出現演出を再生し、完了後にAIを再開する
    private async UniTaskVoid PlayAppearanceAsync(
        GameObject spawnedEnemy,
        EnemyStateMachine stateMachine)
    {
        // 現段階では演出の代わりに1秒待機
        await UniTask.Delay(
            (int)(appearanceDuration * 1000));

        // 敵が削除されていたら終了
        if (spawnedEnemy == null)
        {
            return;
        }

        // 演出完了後にAIを再開
        if (stateMachine != null)
        {
            stateMachine.enabled = true;
        }
    }
}
