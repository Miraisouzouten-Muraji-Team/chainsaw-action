using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 共通の攻撃予告。指定部位の赤色表示、予告時間、接触攻撃情報の送信を担当する。
/// </summary>
/// <remarks>
/// 設定値はEnemyDataから取得し、可変状態はこのEnemy個体だけで保持する。
/// State切り替え、通常攻撃、移動AI、PlayerのHP変更は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyAlertStrategy : IEnemyAlertStrategy
{
    private const string PLAYER_TAG = "Player";
    private const string BASE_COLOR_PROPERTY = "_BaseColor";
    private const string COLOR_PROPERTY = "_Color";

    [Tooltip("赤くする身体の一部のRenderer。このEnemy自身または子を指定する。")]
    [SerializeField]
    private Renderer targetRenderer;

    [Tooltip("赤くするMaterialスロットの番号。先頭は0。")]
    [SerializeField, Min(0)]
    private int materialIndex;

    private EnemyData enemyData;
    private GameObject enemyObject;
    private Rigidbody enemyRigidbody;
    private Material alertMaterial;
    private int colorPropertyId;
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock alertProperties;
    private bool hasAppliedColor;
    private bool isAlertActive;
    private float elapsedTime;

    private Transform playerTransform;
    private MonoBehaviour playerReceiverComponent;
    private IAttackHitReceiver playerAttackHitReceiver;
    private bool hasWarnedMissingReceiver;

    // 複数Collider、Stay通知、Alert中の再接触でも受信先ごとに一度だけ送信する。
    private readonly HashSet<IAttackHitReceiver> damagedReceivers =
        new HashSet<IAttackHitReceiver>();

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject)
    {
        this.enemyData = enemyData
            ? enemyData
            : throw new ArgumentNullException(nameof(enemyData));
        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        enemyRigidbody = enemyObject.GetComponent<Rigidbody>();
        if (enemyRigidbody == null || enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(StandardEnemyAlertStrategy)}には非KinematicのRigidbodyが必要です。");
        }

        if (targetRenderer == null ||
            (targetRenderer.transform != enemyObject.transform &&
             !targetRenderer.transform.IsChildOf(enemyObject.transform)))
        {
            throw new InvalidOperationException(
                $"{nameof(StandardEnemyAlertStrategy)}の対象Rendererに、このEnemyの部位を設定してください。");
        }

        ValidateMaterial();
        originalProperties = new MaterialPropertyBlock();
        alertProperties = new MaterialPropertyBlock();
    }

    public void BeginAlert()
    {
        EndAlert();

        isAlertActive = true;
        elapsedTime = 0f;
        hasWarnedMissingReceiver = false;
        // Initialize時の通常Materialを基準にする。
        // Alert開始時に点滅中でも、その一時Materialを通常表示として記録しない。
        UpdateAlertColor();

        // Alertへ入る前から接触している場合も、次の物理更新でStay通知を受け取る。
        enemyRigidbody.WakeUp();
    }

    public bool UpdateAlert()
    {
        if (!isAlertActive)
        {
            return false;
        }

        UpdateAlertColor();
        elapsedTime += Time.fixedDeltaTime;

        return elapsedTime >= enemyData.AlertDuration ||
               Mathf.Approximately(elapsedTime, enemyData.AlertDuration);
    }

    public void HandleCollision(Collision collision)
    {
        if (!isAlertActive || collision == null)
        {
            return;
        }

        ResolvePlayerReferences();
        if (playerTransform == null || collision.collider == null)
        {
            return;
        }

        Transform collidedTransform = collision.collider.transform;
        if (collidedTransform != playerTransform &&
            !collidedTransform.IsChildOf(playerTransform))
        {
            return;
        }

        if (playerReceiverComponent == null)
        {
            if (!hasWarnedMissingReceiver)
            {
                Debug.LogWarning(
                    $"{nameof(StandardEnemyAlertStrategy)}: Playerに有効なIAttackHitReceiverの実装がありません。",
                    enemyObject);
                hasWarnedMissingReceiver = true;
            }

            return;
        }

        if (!playerReceiverComponent.isActiveAndEnabled ||
            !damagedReceivers.Add(playerAttackHitReceiver))
        {
            return;
        }

        float damage =
            enemyData.AttackPower * (enemyData.AlertContactDamagePercent / 100f);

        // 受信側からEnemyを無効化されても二重送信しないよう、送信前に記録する。
        playerAttackHitReceiver.ReceiveAttackHit(
            new EnemyAttackHitData(enemyData, damage));
    }

    public void EndAlert()
    {
        isAlertActive = false;
        RestoreAlertColor();
        elapsedTime = 0f;
        damagedReceivers.Clear();
        playerTransform = null;
        playerReceiverComponent = null;
        playerAttackHitReceiver = null;
        hasWarnedMissingReceiver = false;
    }

    private void ValidateMaterial()
    {
        if (targetRenderer == null)
        {
            throw new InvalidOperationException(
                $"{nameof(StandardEnemyAlertStrategy)}の対象Rendererがありません。");
        }

        Material[] materials = targetRenderer.sharedMaterials;
        if (materialIndex < 0 || materialIndex >= materials.Length ||
            materials[materialIndex] == null)
        {
            throw new InvalidOperationException(
                $"{nameof(StandardEnemyAlertStrategy)}のMaterialスロット設定が不正です。");
        }

        alertMaterial = materials[materialIndex];
        string colorProperty = alertMaterial.HasProperty(BASE_COLOR_PROPERTY)
            ? BASE_COLOR_PROPERTY
            : COLOR_PROPERTY;

        if (!alertMaterial.HasProperty(colorProperty))
        {
            throw new InvalidOperationException(
                $"{nameof(StandardEnemyAlertStrategy)}のMaterialには対応する色プロパティがありません。");
        }

        colorPropertyId = Shader.PropertyToID(colorProperty);
    }

    private void UpdateAlertColor()
    {
        if (targetRenderer == null)
        {
            return;
        }

        Material[] materials = targetRenderer.sharedMaterials;
        if (materialIndex >= materials.Length ||
            materials[materialIndex] != alertMaterial)
        {
            // EnemyDamageFlash等によるMaterial差し替え中は、その演出を優先する。
            // Materialそのものには触れず、Alertが設定した色の上書きだけを解除する。
            RestoreAlertColor();
            return;
        }

        ApplyAlertColor();
    }

    private void ApplyAlertColor()
    {
        if (hasAppliedColor || targetRenderer == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(originalProperties, materialIndex);
        targetRenderer.GetPropertyBlock(alertProperties, materialIndex);
        if (alertProperties.isEmpty)
        {
            // スロット単位の上書きがない場合は、Renderer全体の既存設定を引き継ぐ。
            targetRenderer.GetPropertyBlock(alertProperties);
        }

        alertProperties.SetColor(colorPropertyId, Color.red);
        targetRenderer.SetPropertyBlock(alertProperties, materialIndex);
        hasAppliedColor = true;
    }

    private void RestoreAlertColor()
    {
        if (!hasAppliedColor)
        {
            return;
        }

        if (targetRenderer != null &&
            materialIndex < targetRenderer.sharedMaterials.Length)
        {
            targetRenderer.SetPropertyBlock(
                originalProperties.isEmpty ? null : originalProperties,
                materialIndex);
        }

        hasAppliedColor = false;
    }

    private void ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(PLAYER_TAG);
            if (playerObject == null)
            {
                return;
            }

            playerTransform = playerObject.transform;
            playerReceiverComponent = null;
            playerAttackHitReceiver = null;
        }

        if (playerReceiverComponent != null &&
            playerReceiverComponent.isActiveAndEnabled)
        {
            return;
        }

        playerReceiverComponent = null;
        playerAttackHitReceiver = null;
        foreach (MonoBehaviour behaviour in
                 playerTransform.GetComponentsInChildren<MonoBehaviour>())
        {
            if (behaviour is IAttackHitReceiver receiver &&
                behaviour.isActiveAndEnabled)
            {
                playerReceiverComponent = behaviour;
                playerAttackHitReceiver = receiver;
                return;
            }
        }
    }
}
