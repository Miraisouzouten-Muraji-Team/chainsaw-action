using UnityEngine;
using UnityEditor;

// EnemySpawnerのInspector表示をカスタマイズする
[CustomEditor(typeof(EnemySpawner))]
public class EnemySpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty enemyCount =
            serializedObject.FindProperty("enemyCount");

        SerializedProperty enemies =
            serializedObject.FindProperty("enemies");

        SerializedProperty useSpawnDelay =
            serializedObject.FindProperty("useSpawnDelay");

        SerializedProperty spawnInterval =
            serializedObject.FindProperty("spawnInterval");

        SerializedProperty appearanceDuration =
            serializedObject.FindProperty("appearanceDuration");

        // 生成数
        EditorGUILayout.PropertyField(
            enemyCount,
            new GUIContent("出現するエネミーの数"));

        enemyCount.intValue = Mathf.Max(1, enemyCount.intValue);

        // 生成数と登録欄の数を一致させる
        if (enemies.arraySize != enemyCount.intValue)
        {
            enemies.arraySize = enemyCount.intValue;
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(
            "エネミー登録",
            EditorStyles.boldLabel);

        // 敵ごとの設定欄
        for (int i = 0; i < enemies.arraySize; i++)
        {
            SerializedProperty entry =
                enemies.GetArrayElementAtIndex(i);

            SerializedProperty enemyPrefab =
                entry.FindPropertyRelative("enemyPrefab");

            SerializedProperty placementMode =
                entry.FindPropertyRelative("placementMode");

            SerializedProperty offset =
                entry.FindPropertyRelative("offset");

            SerializedProperty spawnPoint =
                entry.FindPropertyRelative("spawnPoint");

            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                $"エネミー {i + 1}",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                enemyPrefab,
                new GUIContent("Enemy Prefab"));

            // Prefab未設定時の警告
            if (enemyPrefab.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    $"エネミー {i + 1} のPrefabが未設定です。" +
                    "生成する敵のPrefabを登録してください。",
                    MessageType.Warning);
            }

            // 1体目はスポナー位置に固定
            if (i > 0)
            {
                EditorGUILayout.PropertyField(
                    placementMode,
                    new GUIContent("配置方法"));

                if (placementMode.enumValueIndex ==
                    (int)EnemySpawnEntry.PlacementMode.Simple)
                {
                    EditorGUILayout.PropertyField(
                        offset,
                        new GUIContent("X・Yオフセット"));
                }
                else
                {
                    EditorGUILayout.PropertyField(
                        spawnPoint,
                        new GUIContent("スポーンポイント"));

                    if (spawnPoint.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            $"エネミー {i + 1} のスポーンポイントが" +
                            "未設定です。",
                            MessageType.Warning);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField(
                    "生成位置",
                    "スポナーの位置に固定");
            }

            EditorGUILayout.EndVertical();
        }

        // 時間差スポーンの設定
        // エネミーが2体以上の場合だけ表示する
        if (enemyCount.intValue >= 2)
        {
            EditorGUILayout.PropertyField(
                useSpawnDelay,
                new GUIContent("時間差を有効にする"));

            // ONにした場合だけ生成間隔を表示
            if (useSpawnDelay.boolValue)
            {
                EditorGUILayout.PropertyField(
                    spawnInterval,
                    new GUIContent("生成間隔（秒）"));

                spawnInterval.floatValue =
                    Mathf.Max(0f, spawnInterval.floatValue);

                EditorGUILayout.HelpBox(
                    "最初の敵はすぐに生成され、" +
                    "2体目以降は指定した間隔で生成されます。",
                    MessageType.Info);
            }
        }

        // 出現演出
        EditorGUILayout.PropertyField(
            appearanceDuration,
            new GUIContent("出現時間"));

        appearanceDuration.floatValue =
            Mathf.Max(0f, appearanceDuration.floatValue);

        serializedObject.ApplyModifiedProperties();
    }
}
