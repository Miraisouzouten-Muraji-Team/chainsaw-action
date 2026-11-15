#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.InGame.Boss.Editor
{
    /// <summary>
    /// Boss-Testシーンへ仕様確認用のCube攻撃構成を再現可能な形で設定する。
    /// </summary>
    public static class BossPrototypeSceneSetup
    {
        private const string SCENE_PATH =
            "Assets/_Sandbox/Akane/Scenes/Boss-Test.unity";
        private const string PREFAB_FOLDER =
            "Assets/_Sandbox/Akane/Prefabs";
        private const string LASER_PREFAB_PATH =
            PREFAB_FOLDER + "/PF_BossLaserProjectile.prefab";
        private const string DISSOLVE_MATERIAL_PATH =
            "Assets/_Sandbox/Yonekura/Materials/M_EnemyDissolve.mat";

        /// <summary>
        /// バッチ実行とMenuの両方からシーンを設定する。
        /// </summary>
        [MenuItem("Project/Boss/Setup Boss Prototype Scene")]
        public static void Configure()
        {
            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            GameObject boss = GameObject.Find("TestBoss");

            if (boss == null)
            {
                throw new MissingReferenceException("Boss-TestにTestBossがありません。");
            }

            Transform visualRoot = FindOrCreateChild(boss.transform, "VisualRoot");
            Transform bodyVisual = visualRoot.Find("BossModel");
            Transform oldShield = visualRoot.Find("SieldModel") ??
                visualRoot.Find("ShieldRoot");

            if (bodyVisual == null || oldShield == null)
            {
                throw new MissingReferenceException("既存のBossModelまたは盾がありません。");
            }

            Material prototypeMaterial = bodyVisual.GetComponent<Renderer>()?.sharedMaterial;
            RemoveComponent<Collider>(bodyVisual.gameObject);
            RemoveComponent<BossShieldHitReceiver>(oldShield.gameObject);
            RemoveComponent<Collider>(oldShield.gameObject);

            Transform oldSensor = visualRoot.Find("PlayerContactSensor");

            if (oldSensor != null)
            {
                Object.DestroyImmediate(oldSensor.gameObject);
            }

            RemoveComponent<BossContactTestBridge>(boss);
            RemoveComponent<BossBodyHitReceiver>(boss);

            boss.layer = LayerMask.NameToLayer("BossOuter");
            Rigidbody bossBody = GetOrAdd<Rigidbody>(boss);
            bossBody.useGravity = true;
            bossBody.isKinematic = false;
            bossBody.interpolation = RigidbodyInterpolation.Interpolate;
            bossBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            bossBody.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotation;

            BoxCollider outerCollider = GetOrAdd<BoxCollider>(boss);
            outerCollider.isTrigger = false;
            outerCollider.size = new Vector3(5f, 4f, 3f);
            outerCollider.center = new Vector3(0f, 0.75f, 0f);

            BossController controller = GetOrAdd<BossController>(boss);
            BossHealth health = GetOrAdd<BossHealth>(boss);
            BossShieldController shield = GetOrAdd<BossShieldController>(boss);
            BossPhysicsMotor physicsMotor = GetOrAdd<BossPhysicsMotor>(boss);
            BossAttackHitboxController hitboxController =
                GetOrAdd<BossAttackHitboxController>(boss);
            BossAttackVisualController visualController =
                GetOrAdd<BossAttackVisualController>(boss);
            BossShieldMotionController shieldMotion =
                GetOrAdd<BossShieldMotionController>(boss);
            BossLaserController laserController = GetOrAdd<BossLaserController>(boss);
            BossAttackTelegraph telegraph = GetOrAdd<BossAttackTelegraph>(boss);

            Transform bodyContactRoot = RecreateChild(visualRoot, "BodyContactRoot");
            BossDamageFlash bodyFlash = GetOrAdd<BossDamageFlash>(bodyContactRoot.gameObject);
            BossBodyHitReceiver bodyReceiver =
                GetOrAdd<BossBodyHitReceiver>(bodyContactRoot.gameObject);

            BoxCollider bodyDamageCollider = CreateTrigger(
                bodyContactRoot,
                "BodyDamageZone",
                new Vector3(0f, 0.82f, 0f),
                new Vector3(3f, 4.3f, 2f));
            ConfigureContactZone(bodyDamageCollider.gameObject, false, true, false,
                Vector3.right, null);

            BoxCollider enemyDiggingCollider = CreateTrigger(
                bodyContactRoot,
                "EnemyDiggingZone",
                new Vector3(0f, 2.1f, 0f),
                new Vector3(2.6f, 2f, 2f));
            ConfigureContactZone(enemyDiggingCollider.gameObject, true, false, false,
                Vector3.right, null);

            Transform bodyEntryTarget = FindOrCreateChild(bodyContactRoot, "BodyWallEntryTarget");
            bodyEntryTarget.localPosition = new Vector3(2.6f, 0f, 0f);
            BoxCollider bodyWallCollider = CreateTrigger(
                bodyContactRoot,
                "BodyWallDiggingZone",
                new Vector3(1.65f, 0.8f, 0f),
                new Vector3(0.35f, 4.4f, 2.2f));
            ConfigureContactZone(bodyWallCollider.gameObject, false, false, true,
                Vector3.right, bodyEntryTarget);

            SetObjectReference(bodyFlash, "targetRenderers", new[]
            {
                bodyVisual.GetComponent<Renderer>()
            });
            SetObjectReference(bodyReceiver, "damageFlash", bodyFlash);

            Transform weaponPivot = RecreateChild(visualRoot, "WeaponPivot");
            weaponPivot.localPosition = new Vector3(-1.5f, 0.7f, 0f);
            GameObject bladeVisual = CreateCube(
                weaponPivot,
                "BladeVisual",
                new Vector3(-2f, 0f, 0f),
                new Vector3(4f, 0.35f, 0.45f),
                prototypeMaterial);
            RemoveComponent<Collider>(bladeVisual);

            BoxCollider thrustCollider = CreateAttackHitbox(
                weaponPivot,
                "ThrustHitbox",
                bladeVisual.transform.localPosition,
                Vector3.one,
                out BossAttackHitbox thrustHitbox);
            BoxCollider slashCollider = CreateAttackHitbox(
                visualRoot,
                "SlashHitbox",
                new Vector3(-4f, 0f, 0f),
                new Vector3(8f, 6f, 1.2f),
                out BossAttackHitbox slashHitbox);
            thrustHitbox.transform.localScale = bladeVisual.transform.localScale;
            slashHitbox.ConfigureDome(8f, 1.2f);
            thrustCollider.enabled = false;
            slashCollider.enabled = false;

            Vector3 shieldWorldPosition = oldShield.position;
            Quaternion shieldWorldRotation = oldShield.rotation;
            Vector3 shieldWorldScale = oldShield.lossyScale;
            Transform shieldSocket = RecreateChild(visualRoot, "ShieldSocket");
            shieldSocket.position = shieldWorldPosition;
            shieldSocket.rotation = shieldWorldRotation;
            oldShield.name = "ShieldRoot";
            oldShield.SetParent(shieldSocket, true);
            oldShield.localPosition = Vector3.zero;
            oldShield.localRotation = Quaternion.identity;
            oldShield.localScale = shieldWorldScale;
            oldShield.gameObject.layer = LayerMask.NameToLayer("EnemyContact");

            Rigidbody shieldBody = GetOrAdd<Rigidbody>(oldShield.gameObject);
            shieldBody.isKinematic = true;
            shieldBody.useGravity = false;
            shieldBody.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotation;

            BoxCollider shieldBodyCollider = GetOrAdd<BoxCollider>(oldShield.gameObject);
            shieldBodyCollider.isTrigger = true;
            shieldBodyCollider.size = Vector3.one;

            BossDamageFlash shieldFlash = GetOrAdd<BossDamageFlash>(oldShield.gameObject);
            BossShieldHitReceiver shieldReceiver =
                GetOrAdd<BossShieldHitReceiver>(oldShield.gameObject);
            ConfigureContactZone(oldShield.gameObject, false, true, false,
                Vector3.left, null);

            Transform shieldEntryTarget = FindOrCreateChild(oldShield, "ShieldWallEntryTarget");
            shieldEntryTarget.localPosition = new Vector3(-1.2f, 0f, 0f);
            BoxCollider shieldWallCollider = CreateTrigger(
                oldShield,
                "ShieldWallDiggingZone",
                new Vector3(-0.55f, 0f, 0f),
                new Vector3(0.25f, 1.1f, 1.1f));
            ConfigureContactZone(shieldWallCollider.gameObject, false, false, true,
                Vector3.left, shieldEntryTarget);

            CreateAttackHitbox(
                oldShield,
                "ShieldAttackHitbox",
                Vector3.zero,
                new Vector3(1.3f, 1.3f, 1.3f),
                out BossAttackHitbox shieldAttackHitbox);

            GameObject returnCue = CreateCube(
                shieldSocket,
                "ReturnCue",
                new Vector3(0.4f, 0.5f, 0f),
                Vector3.one * 0.35f,
                prototypeMaterial);
            RemoveComponent<Collider>(returnCue);
            returnCue.SetActive(false);

            Renderer shieldRenderer = oldShield.GetComponent<Renderer>();
            SetObjectReference(shieldFlash, "targetRenderers", new[] { shieldRenderer });
            SetObjectReference(shieldReceiver, "shield", shield);
            SetObjectReference(shieldReceiver, "damageFlash", shieldFlash);

            Transform attackRanges = RecreateChild(boss.transform, "AttackRanges");
            CreateAttackHitbox(
                attackRanges,
                "ShockwaveRange",
                new Vector3(0f, -1f, 0f),
                new Vector3(8f, 2f, 3f),
                out BossAttackHitbox shockwaveHitbox);

            Transform firePoint = RecreateChild(visualRoot, "LaserFirePoint");
            firePoint.localPosition = new Vector3(-2.5f, 2f, 0f);
            BossLaserProjectile projectilePrefab = CreateLaserPrefab(prototypeMaterial);

            Transform indicator = boss.transform.Find("AttackIndicator");
            SetObjectReference(telegraph, "indicator", indicator);
            SetObjectReference(physicsMotor, "bodyCollider", outerCollider);
            SetObjectReference(hitboxController, "shieldBashHitbox", null);
            SetObjectReference(hitboxController, "shockwaveHitbox", shockwaveHitbox);
            SetObjectReference(hitboxController, "thrustHitbox", thrustHitbox);
            SetObjectReference(hitboxController, "slashHitbox", slashHitbox);
            SetObjectReference(hitboxController, "weaponRangeSource", bladeVisual.transform);
            SetBool(hitboxController, "showHitboxGizmos", true);
            SetObjectReference(visualController, "weaponPivot", weaponPivot);
            SetObjectReference(
                visualController,
                "verticalSlashRangeSource",
                slashHitbox.transform);
            SetObjectReference(visualController, "shieldReturnCue", returnCue);
            SetVector3(visualController, "localAttackDirection", Vector3.left);
            SetFloat(visualController, "slashDropDistance", 2f);

            SetObjectReference(shieldMotion, "shieldRoot", oldShield);
            SetObjectReference(shieldMotion, "shieldSocket", shieldSocket);
            SetObjectReference(shieldMotion, "shieldBody", shieldBody);
            SetObjectReference(shieldMotion, "shieldBodyCollider", shieldBodyCollider);
            SetObjectReference(shieldMotion, "attackHitbox", shieldAttackHitbox);
            SetObjectReference(shieldMotion, "attackVisuals", visualController);
            SetLayerMask(shieldMotion, "stageMask", LayerMask.GetMask("StageCollider"));

            Transform player = GetObjectReference<Transform>(controller, "player");
            SetObjectReference(laserController, "projectilePrefab", projectilePrefab);
            SetObjectReference(laserController, "firePoint", firePoint);
            SetObjectReference(laserController, "player", player);
            SetLayerMask(laserController, "stageMask", LayerMask.GetMask("StageCollider"));

            SetObjectReference(shield, "shieldVisual", oldShield.gameObject);
            SetObjectReference(shield, "shieldColliders", new Collider[]
            {
                shieldBodyCollider,
                shieldWallCollider
            });
            SetObjectReference(shield, "motionController", shieldMotion);
            SetObjectReference(
                shield,
                "dissolveMaterial",
                AssetDatabase.LoadAssetAtPath<Material>(DISSOLVE_MATERIAL_PATH));

            SetObjectReference(controller, "visualRoot", visualRoot);
            SetObjectReference(controller, "attackHitboxes", hitboxController);
            SetObjectReference(controller, "attackVisuals", visualController);
            SetObjectReference(controller, "physicsMotor", physicsMotor);
            SetObjectReference(controller, "shieldMotion", shieldMotion);
            SetObjectReference(controller, "laserController", laserController);
            SetObjectReference(controller, "attackTelegraph", telegraph);

            EnsureDissolveManager();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("BossPrototypeSceneSetup: Boss-Testの設定を完了しました。", boss);
        }

        private static void EnsureDissolveManager()
        {
            if (Object.FindAnyObjectByType<EnemyDissolveManager>() != null) { return; }

            GameObject manager = new GameObject("EnemyDissolveManager");
            manager.AddComponent<EnemyDissolveManager>();
        }

        private static BossLaserProjectile CreateLaserPrefab(Material material)
        {
            if (!AssetDatabase.IsValidFolder(PREFAB_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/_Sandbox/Akane", "Prefabs");
            }

            GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temporary.name = "PF_BossLaserProjectile";
            temporary.layer = LayerMask.NameToLayer("EnemyProjectile");
            temporary.transform.localScale = new Vector3(0.8f, 0.15f, 0.15f);
            BoxCollider collider = temporary.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            Rigidbody body = temporary.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotation;
            temporary.AddComponent<BossLaserProjectile>();

            if (material != null)
            {
                temporary.GetComponent<Renderer>().sharedMaterial = material;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, LASER_PREFAB_PATH);
            Object.DestroyImmediate(temporary);
            return prefab.GetComponent<BossLaserProjectile>();
        }

        private static BoxCollider CreateAttackHitbox(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 size,
            out BossAttackHitbox hitbox)
        {
            Transform target = RecreateChild(parent, name);
            target.localPosition = localPosition;
            target.gameObject.layer = LayerMask.NameToLayer("EnemyContact");
            BoxCollider collider = GetOrAdd<BoxCollider>(target.gameObject);
            collider.isTrigger = true;
            collider.size = size;
            hitbox = GetOrAdd<BossAttackHitbox>(target.gameObject);
            return collider;
        }

        private static BoxCollider CreateTrigger(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 size)
        {
            Transform target = RecreateChild(parent, name);
            target.localPosition = localPosition;
            target.gameObject.layer = LayerMask.NameToLayer("EnemyContact");
            BoxCollider collider = GetOrAdd<BoxCollider>(target.gameObject);
            collider.isTrigger = true;
            collider.size = size;
            return collider;
        }

        private static void ConfigureContactZone(
            GameObject target,
            bool allowEnemyDigging,
            bool allowAttack,
            bool allowWallDigging,
            Vector3 wallNormal,
            Transform entryTarget)
        {
            ChainsawContactZone zone = GetOrAdd<ChainsawContactZone>(target);
            SerializedObject serialized = new SerializedObject(zone);
            serialized.FindProperty("allowEnemyDigging").boolValue = allowEnemyDigging;
            serialized.FindProperty("allowAttack").boolValue = allowAttack;
            serialized.FindProperty("allowWallDigging").boolValue = allowWallDigging;
            serialized.FindProperty("localWallNormal").vector3Value = wallNormal;
            serialized.FindProperty("useLocalWallNormal").boolValue = true;
            serialized.FindProperty("playerEntryTarget").objectReferenceValue = entryTarget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateCube(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            Transform old = parent.Find(name);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;

            if (material != null)
            {
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            return cube;
        }

        private static Transform RecreateChild(Transform parent, string name)
        {
            Transform old = parent.Find(name);

            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);

            if (child != null) { return child; }

            GameObject created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void RemoveComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();

            if (component != null)
            {
                Object.DestroyImmediate(component);
            }
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReference<T>(
            Object target,
            string propertyName,
            T[] values) where T : Object
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T GetObjectReference<T>(Object target, string propertyName)
            where T : Object
        {
            SerializedObject serialized = new SerializedObject(target);
            return serialized.FindProperty(propertyName).objectReferenceValue as T;
        }

        private static void SetLayerMask(Object target, string propertyName, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector3(
            Object target,
            string propertyName,
            Vector3 value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
