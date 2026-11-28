using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class PlayerNumericSettingsMigration
{
    private const string MENU = "Tools/Muraji/選択したプレイヤーの数値をSOへ移行";
    private const string ASSET_DIRECTORY = "Assets/_Sandbox/Muraji/ScriptableObject/MigratedNumericSettings";
    private const string MIGRATION_GUID = "numericSettingsMigrationAssetGuid";

    private static readonly (string Source, string Target)[] CONTROLLER_FIELDS =
    {
        ("walkEffectAnimatorLayer", "walkEffectAnimatorLayer"),
        ("walkEffectInterval", "walkEffectInterval"),
        ("walkEffectMinimumWeight", "walkEffectMinimumWeight"),
        ("landingEffectMinimumAirSeconds", "landingEffectMinimumAirSeconds"),
        ("diggingReleaseMomentumCurve", "diggingReleaseMomentumCurve"),
        ("uphillDiggingReleaseSpeedMultiplier", "uphillDiggingReleaseSpeedMultiplier"),
        ("directWallSpeedDivisor", "directWallSpeedDivisor"),
        ("wallTopMaxRise", "wallTopMaxRise"),
        ("wallTopMoveSpeed", "wallTopMoveSpeed"),
        ("diggingContactSpeedMultiplier", "diggingContactSpeedMultiplier"),
        ("diggingContactRecoverySeconds", "diggingContactRecoverySeconds"),
        ("ceilingJumpReleaseSpeed", "ceilingJumpReleaseSpeed"),
        ("ceilingJumpReleaseDuration", "ceilingJumpReleaseDuration"),
        ("impactTerrainLayers", "impactTerrainLayers"),
        ("slashEffectDuration", "slashEffectDuration"),
        ("walkEffect.Lifetime", "walkEffectLifetime"),
        ("jumpEffect.Lifetime", "jumpEffectLifetime"),
        ("doubleJumpEffect.Lifetime", "doubleJumpEffectLifetime"),
        ("landingEffect.Lifetime", "landingEffectLifetime"),
        ("groundAttackEffect.Lifetime", "groundAttackEffectLifetime"),
        ("airAttackEffect.Lifetime", "airAttackEffectLifetime"),
        ("groundSwingEffect.Lifetime", "groundSwingEffectLifetime"),
        ("airSwingEffect.Lifetime", "airSwingEffectLifetime"),
    };

    private static readonly (string Source, string Target)[] DIGGING_FIELDS =
    {
        ("wallJumpReleaseBufferSeconds", "wallJumpReleaseBufferSeconds"),
        ("movementContactDamageMultiplier", "movementContactDamageMultiplier"),
    };

    private static readonly (string Source, string Target)[] ATTACK_FIELDS =
    {
        ("enemyDiggingShakeAmplitude", "enemyDiggingShakeAmplitude"),
        ("enemyDiggingShakeFrequency", "enemyDiggingShakeFrequency"),
        ("slashKnockbackPriorityDuration", "slashKnockbackPriorityDuration"),
        ("duration", "hitShakeDuration"),
        ("weakDiggingAcceleratorGain", "weakDiggingAcceleratorGain"),
        ("weakFinisherAcceleratorGain", "weakFinisherAcceleratorGain"),
    };

    // Projectで選択したPrefab、またはHierarchyで選択したプレイヤー階層の3コンポーネントを移行する。
    [MenuItem(MENU)]
    private static void MigrateSelectedPlayers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var selected = Selection.gameObjects;
        var visitedPrefabs = new HashSet<string>();
        var visitedComponents = new HashSet<Component>();
        int migrated = 0;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("プレイヤーの数値設定をSOへ移行");

        foreach (GameObject root in selected)
        {
            if (EditorUtility.IsPersistent(root))
            {
                string path = AssetDatabase.GetAssetPath(root);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && visitedPrefabs.Add(path))
                    migrated += MigratePrefab(path);
            }
            else if (root.scene.IsValid() && root.scene.isLoaded)
            {
                int count = MigrateHierarchy(root, visitedComponents, null);
                if (count > 0) EditorSceneManager.MarkSceneDirty(root.scene);
                migrated += count;
            }
        }
        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        Debug.Log($"数値設定の移行完了: {migrated} コンポーネント。シーンのオブジェクトを選んだ場合は、シーンを保存してください。");
    }

    // 再生中は移行せず、ProjectまたはHierarchyに選択がある場合に実行できるようにする。
    [MenuItem(MENU, true)]
    private static bool CanMigrateSelectedPlayers()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Length > 0;
    }

    // Prefabを隔離環境で編集し、保存できた場合にだけ新しい設定アセットの参照を確定する。
    private static int MigratePrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        var createdAssets = new List<string>();
        bool saved = false;
        try
        {
            int count = MigrateHierarchy(root, new HashSet<Component>(), createdAssets);
            if (count == 0) return 0;
            PrefabUtility.SaveAsPrefabAsset(root, path, out saved);
            if (!saved)
                Debug.LogError($"Prefabを保存できなかったため、設定の移行を取り消しました: {path}");
            return saved ? count : 0;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            return 0;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            if (!saved)
                foreach (string created in createdAssets) AssetDatabase.DeleteAsset(created);
        }
    }

    // 選択されたルートと子にある対象コンポーネントだけを処理する。
    private static int MigrateHierarchy(GameObject root, HashSet<Component> visited, List<string> createdAssets)
    {
        int count = 0;
        foreach (var component in root.GetComponentsInChildren<SensorPlayerController>(true))
            if (visited.Add(component) &&
                MigrateComponent<PlayerParameter>(component, "playerParameter", CONTROLLER_FIELDS, createdAssets)) count++;
        foreach (var component in root.GetComponentsInChildren<SensorChainsawDigging>(true))
            if (visited.Add(component) &&
                MigrateComponent<DiggingParameter>(component, "diggingParameter", DIGGING_FIELDS, createdAssets)) count++;
        foreach (var component in root.GetComponentsInChildren<ChainsawAttack>(true))
            if (visited.Add(component) &&
                MigrateComponent<ChainsawAttackParameter>(component, "attackParameter", ATTACK_FIELDS, createdAssets)) count++;
        return count;
    }

    // 既存SOを複製してから現在のInspector値で上書きし、このコンポーネントに割り当てる。
    private static bool MigrateComponent<T>(Component component, string parameterField,
        (string Source, string Target)[] fields, List<string> createdAssets) where T : ScriptableObject
    {
        var original = new SerializedObject(component);
        var reference = original.FindProperty(parameterField);
        var marker = original.FindProperty(MIGRATION_GUID);
        if (reference == null || marker == null)
            throw new InvalidOperationException($"{component.name}: 設定のフィールドが見つかりません。");

        if (!string.IsNullOrEmpty(marker.stringValue) &&
            (!PrefabUtility.IsPartOfPrefabInstance(component) || marker.prefabOverride ||
             (!reference.prefabOverride && !HasLegacyOverrides(original, fields))))
            return false;

        // 新しい設定を手で割り当て済みのChainsawAttackは、その参照を優先する。
        if (component is ChainsawAttack && reference.objectReferenceValue != null &&
            string.IsNullOrEmpty(marker.stringValue))
            return false;

        T existing = reference.objectReferenceValue as T;
        T parameter = existing != null ? UnityEngine.Object.Instantiate(existing) : ScriptableObject.CreateInstance<T>();
        var target = new SerializedObject(parameter);
        foreach (var field in fields)
        {
            var from = original.FindProperty(field.Source);
            var to = target.FindProperty(field.Target);
            if (from == null || to == null)
            {
                UnityEngine.Object.DestroyImmediate(parameter);
                throw new InvalidOperationException($"{component.name}: {field.Source} → {field.Target} を移行できません。");
            }

            CopyNumericProperty(from, to);
        }
        target.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder(ASSET_DIRECTORY))
        {
            Directory.CreateDirectory(ASSET_DIRECTORY);
            AssetDatabase.Refresh();
        }
        string safeName = component.gameObject.name.Replace('/', '_').Replace('\\', '_');
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{ASSET_DIRECTORY}/{safeName}_{typeof(T).Name}.asset");
        AssetDatabase.CreateAsset(parameter, assetPath);
        createdAssets?.Add(assetPath);
        reference.objectReferenceValue = parameter;
        marker.stringValue = AssetDatabase.AssetPathToGUID(assetPath);
        original.ApplyModifiedProperties();
        return true;
    }

    // ベースを移行した後も、Prefab Variantやシーンのインスタンスの数値上書きを検出する。
    private static bool HasLegacyOverrides(SerializedObject original, (string Source, string Target)[] fields)
    {
        foreach (var field in fields)
        {
            var property = original.FindProperty(field.Source);
            if (property != null && property.prefabOverride) return true;
        }
        return false;
    }

    // スカラー、レイヤーマスク、カーブを、それぞれのシリアライズ型のまま転送する。
    private static void CopyNumericProperty(SerializedProperty from, SerializedProperty to)
    {
        switch (from.propertyType)
        {
            case SerializedPropertyType.Float: to.floatValue = from.floatValue; break;
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask: to.intValue = from.intValue; break;
            case SerializedPropertyType.AnimationCurve: to.animationCurveValue = from.animationCurveValue; break;
            default: throw new InvalidOperationException($"未対応の移行元: {from.propertyPath}");
        }
    }
}
