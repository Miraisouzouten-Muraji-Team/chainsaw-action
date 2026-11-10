using UnityEngine;
using UnityEditor;

/// <summary>
/// EnemySpawnerのInspector表示をカスタマイズするクラス
/// </summary>
[CustomEditor(typeof(EnemySpawner))]
public class EnemySpawnerEditor : Editor
{
    /// <summary>
    /// 敵の生成数やPrefab、配置方法、出現演出などの設定項目を
    /// Inspectorに表示する
    /// </summary>
    public override void OnInspectorGUI()
    {
        // Inspector上の変更を取得
        serializedObject.Update();

        // EnemySpawnerの設定項目を取得
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

        // 生成数を表示
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

        // 敵ごとのPrefab・配置・出現演出を表示
        for (int i = 0; i < enemies.arraySize; i++)
        {
            SerializedProperty entry =
                enemies.GetArrayElementAtIndex(i);

            SerializedProperty enemyPrefab =
                entry.FindPropertyRelative("enemyPrefab");

            SerializedProperty appearanceType =
                entry.FindPropertyRelative("appearanceType");

            SerializedProperty dissolveMaterial =
                entry.FindPropertyRelative("dissolveMaterial");

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

            // 生成する敵のPrefabを設定
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

            // エネミーごとの出現演出
            EditorGUILayout.PropertyField(
                appearanceType,
                new GUIContent("出現演出"));

            // ディゾルブ演出を選択した場合だけマテリアルを表示
            if (appearanceType.enumValueIndex ==
                (int)EnemySpawnEntry.AppearanceType.Dissolve)
            {
                EditorGUILayout.PropertyField(
                    dissolveMaterial,
                    new GUIContent("ディゾルブ用マテリアル"));

                if (dissolveMaterial.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "ディゾルブ用マテリアルが未設定です。",
                        MessageType.Warning);
                }
            }

            // 1体目はスポナー位置に固定
            if (i == 0)
            {
                EditorGUILayout.LabelField(
                    "生成位置",
                    "スポナーの位置に固定");
            }
            else
            {
                // 2体目以降は配置方法を選択できる
                EditorGUILayout.PropertyField(
                    placementMode,
                    new GUIContent("配置方法"));

                if (placementMode.enumValueIndex ==
                    (int)EnemySpawnEntry.PlacementMode.Simple)
                {
                    // スポナーからのX・Y方向のずれを設定する
                    EditorGUILayout.PropertyField(
                        offset,
                        new GUIContent("X・Yオフセット"));
                }
                else
                {
                    // 詳細配置ではスポーンポイントを指定する
                    EditorGUILayout.PropertyField(
                        spawnPoint,
                        new GUIContent("スポーンポイント"));

                    // スポーンポイントが未設定の場合は警告する
                    if (spawnPoint.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            $"エネミー {i + 1} のスポーンポイントが" +
                            "未設定です。",
                            MessageType.Warning);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }
        // 2体以上の場合だけ時間差スポーンの設定を表示する
        if (enemyCount.intValue >= 2)
        {
            EditorGUILayout.PropertyField(
                useSpawnDelay,
                new GUIContent("時間差を有効にする"));

            // 時間差が有効な場合だけ生成間隔を表示する
            if (useSpawnDelay.boolValue)
            {
                EditorGUILayout.PropertyField(
                    spawnInterval,
                    new GUIContent("生成間隔（秒）"));

                // 生成間隔は0秒未満にならないようにする
                spawnInterval.floatValue =
                    Mathf.Max(0f, spawnInterval.floatValue);

                EditorGUILayout.HelpBox(
                    "最初の敵はすぐに生成され、" +
                    "2体目以降は指定した間隔で生成されます。",
                    MessageType.Info);
            }
        }
        //// ディゾルブ演出の共通設定
        //EditorGUILayout.Space(8);

        //EditorGUILayout.LabelField(
        //    "ディゾルブ設定（共通）",
        //    EditorStyles.boldLabel);

        //// ディゾルブ用マテリアル
        //EditorGUILayout.PropertyField(
        //    dissolveMaterial,
        //    new GUIContent("Spawndissolve Material"));

        //// マテリアル未設定時の警告
        //if (dissolveMaterial.objectReferenceValue == null)
        //{
        //    EditorGUILayout.HelpBox(
        //        "ディゾルブ用マテリアルが未設定です。",
        //        MessageType.Warning);
        //}

        // 出現時間
        EditorGUILayout.PropertyField(
            appearanceDuration,
            new GUIContent("出現時間（秒）"));

        appearanceDuration.floatValue =
            Mathf.Max(0f, appearanceDuration.floatValue);

        //EditorGUILayout.Space(8);

        //EditorGUILayout.LabelField(
        //    "ディゾルブ設定（共通）",
        //    EditorStyles.boldLabel);

        //EditorGUILayout.PropertyField(
        //    appearanceDuration,
        //    new GUIContent("出現時間（秒）"));

        //appearanceDuration.floatValue =
        //    Mathf.Max(0f, appearanceDuration.floatValue);

        //// Inspector上の変更をEnemySpawnerに反映する
        serializedObject.ApplyModifiedProperties();
    }
}

//using UnityEngine;
//using UnityEditor;

///// <summary>
///// EnemySpawnerのInspector表示をカスタマイズするクラス。
///// </summary>
//[CustomEditor(typeof(EnemySpawner))]
//public class EnemySpawnerEditor : Editor
//{
//    /// <summary>
//    /// 敵の生成数やPrefab、マテリアル、配置方法、
//    /// 出現演出などの設定項目をInspectorに表示する。
//    /// </summary>
//    public override void OnInspectorGUI()
//    {
//        // Inspector上の変更を取得する。
//        serializedObject.Update();

//        // EnemySpawnerの設定項目を取得する。
//        SerializedProperty enemyCount =
//            serializedObject.FindProperty("enemyCount");

//        SerializedProperty enemies =
//            serializedObject.FindProperty("enemies");

//        SerializedProperty useSpawnDelay =
//            serializedObject.FindProperty("useSpawnDelay");

//        SerializedProperty spawnInterval =
//            serializedObject.FindProperty("spawnInterval");

//        SerializedProperty appearanceDuration =
//            serializedObject.FindProperty("appearanceDuration");

//        // 生成数を表示する。
//        EditorGUILayout.PropertyField(
//            enemyCount,
//            new GUIContent("出現するエネミーの数"));

//        enemyCount.intValue =
//            Mathf.Max(1, enemyCount.intValue);

//        // 生成数と登録欄の数を一致させる。
//        if (enemies.arraySize != enemyCount.intValue)
//        {
//            enemies.arraySize = enemyCount.intValue;
//        }

//        EditorGUILayout.Space(8);

//        EditorGUILayout.LabelField(
//            "エネミー登録",
//            EditorStyles.boldLabel);

//        // 敵ごとのPrefab・マテリアル・配置・出現演出を表示する。
//        for (int i = 0; i < enemies.arraySize; i++)
//        {
//            SerializedProperty entry =
//                enemies.GetArrayElementAtIndex(i);

//            SerializedProperty enemyPrefab =
//                entry.FindPropertyRelative("enemyPrefab");

//            SerializedProperty appearanceType =
//                entry.FindPropertyRelative("appearanceType");

//            SerializedProperty placementMode =
//                entry.FindPropertyRelative("placementMode");

//            SerializedProperty offset =
//                entry.FindPropertyRelative("offset");

//            SerializedProperty spawnPoint =
//                entry.FindPropertyRelative("spawnPoint");

//            SerializedProperty rendererMaterials =
//                entry.FindPropertyRelative("rendererMaterials");

//            EditorGUILayout.BeginVertical(
//                EditorStyles.helpBox);

//            EditorGUILayout.LabelField(
//                $"エネミー {i + 1}",
//                EditorStyles.boldLabel);

//            // 生成する敵のPrefabを設定する。
//            EditorGUILayout.PropertyField(
//                enemyPrefab,
//                new GUIContent("Enemy Prefab"));

//            // Prefab未設定時の警告。
//            if (enemyPrefab.objectReferenceValue == null)
//            {
//                EditorGUILayout.HelpBox(
//                    $"エネミー {i + 1} のPrefabが未設定です。" +
//                    "生成する敵のPrefabを登録してください。",
//                    MessageType.Warning);
//            }

//            // エネミーごとの出現演出を設定する。
//            EditorGUILayout.PropertyField(
//                appearanceType,
//                new GUIContent("出現演出"));

//            // ディゾルブを選択した場合のみマテリアル設定を表示する。
//            if (appearanceType.enumValueIndex ==
//                (int)EnemySpawnEntry.AppearanceType.Dissolve)
//            {
//                EditorGUILayout.Space(4);

//                EditorGUILayout.LabelField(
//                    "マテリアル設定",
//                    EditorStyles.boldLabel);

//                // Rendererごとのマテリアル設定を表示する。
//                EditorGUILayout.PropertyField(
//                    rendererMaterials,
//                    new GUIContent("Renderer Materials"),
//                    true);

//                // 登録内容を確認する。
//                for (int j = 0; j < rendererMaterials.arraySize; j++)
//                {
//                    SerializedProperty materialEntry =
//                        rendererMaterials.GetArrayElementAtIndex(j);

//                    SerializedProperty material =
//                        materialEntry.FindPropertyRelative("material");

//                    if (material.objectReferenceValue == null)
//                    {
//                        EditorGUILayout.HelpBox(
//                            $"Element {j} のマテリアルが未設定です。",
//                            MessageType.Warning);
//                    }
//                }

//                EditorGUILayout.HelpBox(
//                    "敵本体はTarget Pathを空欄にします。" +
//                    "子オブジェクトはHierarchyの階層パスを指定してください。",
//                    MessageType.Info);
//            }

//            // 1体目はスポナー位置に固定する。
//            if (i == 0)
//            {
//                EditorGUILayout.Space(4);

//                EditorGUILayout.LabelField(
//                    "生成位置",
//                    "スポナーの位置に固定");
//            }
//            else
//            {
//                // 2体目以降は配置方法を選択できる。
//                EditorGUILayout.Space(4);

//                EditorGUILayout.PropertyField(
//                    placementMode,
//                    new GUIContent("配置方法"));

//                if (placementMode.enumValueIndex ==
//                    (int)EnemySpawnEntry.PlacementMode.Simple)
//                {
//                    // スポナーからのX・Y方向のずれを設定する。
//                    EditorGUILayout.PropertyField(
//                        offset,
//                        new GUIContent("X・Yオフセット"));
//                }
//                else
//                {
//                    // 詳細配置ではスポーンポイントを指定する。
//                    EditorGUILayout.PropertyField(
//                        spawnPoint,
//                        new GUIContent("スポーンポイント"));

//                    // スポーンポイントが未設定の場合は警告する。
//                    if (spawnPoint.objectReferenceValue == null)
//                    {
//                        EditorGUILayout.HelpBox(
//                            $"エネミー {i + 1} のスポーンポイントが" +
//                            "未設定です。",
//                            MessageType.Warning);
//                    }
//                }
//            }

//            EditorGUILayout.EndVertical();

//            EditorGUILayout.Space(4);
//        }

//        // 2体以上の場合だけ時間差スポーンの設定を表示する。
//        if (enemyCount.intValue >= 2)
//        {
//            EditorGUILayout.PropertyField(
//                useSpawnDelay,
//                new GUIContent("時間差を有効にする"));

//            // 時間差が有効な場合だけ生成間隔を表示する。
//            if (useSpawnDelay.boolValue)
//            {
//                EditorGUILayout.PropertyField(
//                    spawnInterval,
//                    new GUIContent("生成間隔（秒）"));

//                // 生成間隔は0秒未満にならないようにする。
//                spawnInterval.floatValue =
//                    Mathf.Max(0f, spawnInterval.floatValue);

//                EditorGUILayout.HelpBox(
//                    "最初の敵はすぐに生成され、" +
//                    "2体目以降は指定した間隔で生成されます。",
//                    MessageType.Info);
//            }
//        }

//        // ディゾルブ演出の共通設定。
//        EditorGUILayout.Space(8);

//        EditorGUILayout.LabelField(
//            "ディゾルブ設定（共通）",
//            EditorStyles.boldLabel);

//        EditorGUILayout.PropertyField(
//            appearanceDuration,
//            new GUIContent("出現時間（秒）"));

//        appearanceDuration.floatValue =
//            Mathf.Max(0f, appearanceDuration.floatValue);

//        // Inspector上の変更をEnemySpawnerに反映する。
//        serializedObject.ApplyModifiedProperties();
//    }
//}
