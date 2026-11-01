using System;
using UnityEngine;

/// <summary>1台の平行投影カメラで、背景・前景の表示物だけを動かす。</summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class ParallaxLayerRig : MonoBehaviour
{
    [Serializable]
    public sealed class LayerSettings
    {
        [Tooltip("背景・前景の表示物をまとめた親。足場・敵・プレイヤーは含めません。")]
        public Transform root;
        [Tooltip("画面内で流れる量の倍率。0=画面に固定、1=通常の地形と同じ、1超=速く流れる。X/Y別に設定できます。")]
        public Vector2 scrollMultiplier = Vector2.one;
        [NonSerialized] internal Vector3 origin;
        [NonSerialized] internal Transform capturedRoot;

        public LayerSettings(float amount)
        {
            scrollMultiplier = new Vector2(amount, amount);
        }
    }

    [Header("描画するカメラ（1台）")]
    [Tooltip("PlayerCameraのある平行投影カメラ。空欄なら同じGameObjectから取得します。")]
    [SerializeField] private Camera targetCamera;
    [Header("遠景：ほとんど流れない")]
    [SerializeField] private LayerSettings far = new LayerSettings(0.08f);
    [Header("中景：ゆっくり流れる")]
    [SerializeField] private LayerSettings middle = new LayerSettings(0.4f);
    [Header("前景：地形より速く流れる")]
    [SerializeField] private LayerSettings foreground = new LayerSettings(1.15f);

    private Vector3 cameraOrigin;
    private Camera capturedCamera;
    private bool initialized;

    // PlayerCamera.Awakeの初期配置後に、表示物の現在位置を基準として記録する。
    private void Start()
    {
        Initialize();
    }

    private void LateUpdate()
    {
        if (!initialized || capturedCamera != targetCamera
            || BindingChanged(far) || BindingChanged(middle) || BindingChanged(foreground))
        {
            if (!Initialize())
                return;
        }

        Vector3 movement = targetCamera.transform.position - cameraOrigin;
        Apply(far, movement);
        Apply(middle, movement);
        Apply(foreground, movement);
    }

    private bool Initialize()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera == null || !targetCamera.orthographic)
        {
            Debug.LogError("ParallaxLayerRig: 平行投影のTarget Cameraを指定してください。", this);
            enabled = false;
            return false;
        }

        if (!ValidateRoot(far) || !ValidateRoot(middle) || !ValidateRoot(foreground)
            || !ValidatePair(far, middle) || !ValidatePair(far, foreground)
            || !ValidatePair(middle, foreground))
        {
            enabled = false;
            return false;
        }

        cameraOrigin = targetCamera.transform.position;
        capturedCamera = targetCamera;
        Capture(far);
        Capture(middle);
        Capture(foreground);
        initialized = true;
        return true;
    }

    private bool ValidateRoot(LayerSettings layer)
    {
        if (layer == null || layer.root == null)
            return true;

        Transform cameraTransform = targetCamera.transform;
        if (layer.root == cameraTransform || layer.root.IsChildOf(cameraTransform)
            || cameraTransform.IsChildOf(layer.root))
        {
            Debug.LogError("ParallaxLayerRig: Rootにはカメラ自身・親・子を登録せず、カメラと独立した背景用の親を登録してください。", this);
            return false;
        }
        return true;
    }

    private bool ValidatePair(LayerSettings a, LayerSettings b)
    {
        if (a == null || b == null || a.root == null || b.root == null)
            return true;

        if (a.root == b.root || a.root.IsChildOf(b.root) || b.root.IsChildOf(a.root))
        {
            Debug.LogError("ParallaxLayerRig: 各Rootは別々に指定し、互いに親子にせず兄弟として配置してください。", this);
            return false;
        }
        return true;
    }

    private static bool BindingChanged(LayerSettings layer)
    {
        return layer != null && layer.capturedRoot != layer.root;
    }

    private static void Capture(LayerSettings layer)
    {
        if (layer == null)
            return;
        layer.capturedRoot = layer.root;
        if (layer.root != null)
            layer.origin = layer.root.position;
    }

    private static void Apply(LayerSettings layer, Vector3 movement)
    {
        if (layer == null || layer.root == null)
            return;

        // カメラが10、遠景が9.2動けば、画面内では左へ0.8だけ流れる。
        // 毎フレーム加算せず、初期位置から求める。往復や復活でも誤差が累積しない。
        Vector3 position = layer.origin;
        position.x += movement.x * (1f - Mathf.Max(0f, layer.scrollMultiplier.x));
        position.y += movement.y * (1f - Mathf.Max(0f, layer.scrollMultiplier.y));
        layer.root.position = position;
    }
}
