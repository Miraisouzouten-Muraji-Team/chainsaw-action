using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵の攻撃判定中にプレイヤーとのTrigger接触を検出する。
/// ダメージ計算や行動遷移は担当しない。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EnemyPlayerContactSensor : MonoBehaviour
{
    private readonly HashSet<SensorPlayerController> hitPlayers = new();

    private bool hitWindowOpen;

    public event Action<SensorPlayerController> PlayerTouched;

    private void Awake()
    {
        Collider sensorCollider = GetComponent<Collider>();

        if (!sensorCollider.isTrigger)
        {
            Debug.LogError("EnemyPlayerContactSensor: Is TriggerをONにしてください。", this);
            enabled = false;
        }
    }

    /// <summary>
    /// 攻撃判定を開始する。
    /// </summary>
    public void OpenHitWindow()
    {
        hitPlayers.Clear();
        hitWindowOpen = true;
    }

    /// <summary>
    /// 攻撃判定を終了する。
    /// </summary>
    public void CloseHitWindow()
    {
        hitWindowOpen = false;
        hitPlayers.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ContactSensor] TriggerEnter: {other.name}", this);
        TryDetectPlayer(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDetectPlayer(other);
    }

    /// <summary>
    /// 一回の攻撃で同じプレイヤーを重複通知しない。
    /// </summary>
    private void TryDetectPlayer(Collider other)
    {
        if (!isActiveAndEnabled || !hitWindowOpen || other == null || other.isTrigger) { return; }

        SensorPlayerController player = other.GetComponentInParent<SensorPlayerController>();

        if (player == null || !hitPlayers.Add(player)) { return; }

        PlayerTouched?.Invoke(player);
    }

    private void OnDisable()
    {
        CloseHitWindow();
    }
}
