using System.Collections.Generic;
using UnityEngine;

// Controllerと同じGameObjectに配置する。
// 衝突情報はControllerから受け取る。
public class PlayeMidCollider : MonoBehaviour
{
    private const int BLOCK_LEFT = 1;
    private const int BLOCK_RIGHT = 2;

    [Tooltip("移動を止める地形のレイヤー。Nothingなら食い込み判定の地形設定を使用します。敵は含めないでください。")]
    [SerializeField]
    private LayerMask wallLayers;

    [Tooltip("面の法線のY成分がこの値以上なら床か天井として扱います。食い込み判定と同じ値にしてください。")]
    [SerializeField, Range(0.1f, 0.95f)]
    private float surfaceNormalThreshold = 0.7f;

    // 地形ごとに記録し、複数の壁との接触に対応する。
    private readonly Dictionary<Collider, int> contacts =
        new Dictionary<Collider, int>();

    private readonly List<Collider> removalBuffer =
        new List<Collider>();

    private int blockedDirections;

    public void RefreshContacts(int fallbackLayers)
    {
        blockedDirections = 0;
        removalBuffer.Clear();

        foreach (KeyValuePair<Collider, int> pair in contacts)
        {
            if (!IsValidTerrain(pair.Key, fallbackLayers))
            {
                removalBuffer.Add(pair.Key);
            }
            else
            {
                blockedDirections |= pair.Value;
            }
        }

        // 無効化・削除された地形の記録を取り除く。
        foreach (Collider target in removalBuffer)
        {
            contacts.Remove(target);
        }
    }

    public void RecordContact(
        Collision collision,
        int fallbackLayers)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        Collider target = collision.collider;

        if (!IsValidTerrain(target, fallbackLayers))
        {
            RemoveContact(target, fallbackLayers);
            return;
        }

        int directions = 0;

        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;

            // 横スクロールに関係しない奥行き方向の面を除外する。
            if (Mathf.Abs(normal.z) > 0.5f)
            {
                continue;
            }

            normal = new Vector3(
                normal.x,
                normal.y,
                0f
            ).normalized;

            // 床・天井・歩ける斜面を壁として扱わない。
            if (Mathf.Abs(normal.y) >= surfaceNormalThreshold)
            {
                continue;
            }

            // 法線は壁からプレイヤー側へ向く。
            if (normal.x > 0f)
            {
                directions |= BLOCK_LEFT;
            }
            else if (normal.x < 0f)
            {
                directions |= BLOCK_RIGHT;
            }
        }

        // 同じ地形でも、壁面から床面へ接触が変わったら解除する。
        if (directions == 0)
        {
            contacts.Remove(target);
        }
        else
        {
            contacts[target] = directions;
        }

        RefreshContacts(fallbackLayers);
    }

    public void RemoveContact(
        Collider target,
        int fallbackLayers)
    {
        if (target != null)
        {
            contacts.Remove(target);
        }

        RefreshContacts(fallbackLayers);
    }

    public bool IsBlocked(float horizontalSpeed)
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }

        if (horizontalSpeed > 0f)
        {
            return (blockedDirections & BLOCK_RIGHT) != 0;
        }

        if (horizontalSpeed < 0f)
        {
            return (blockedDirections & BLOCK_LEFT) != 0;
        }

        return false;
    }

    private bool IsValidTerrain(
        Collider target,
        int fallbackLayers)
    {
        int layers = wallLayers.value != 0
            ? wallLayers.value
            : fallbackLayers;

        return target != null &&
            target.enabled &&
            !target.isTrigger &&
            target.gameObject.activeInHierarchy &&
            target.transform != transform &&
            !target.transform.IsChildOf(transform) &&
            (layers & (1 << target.gameObject.layer)) != 0;
    }

    public void ClearContacts()
    {
        contacts.Clear();
        removalBuffer.Clear();
        blockedDirections = 0;
    }

    private void OnDisable()
    {
        ClearContacts();
    }
}
