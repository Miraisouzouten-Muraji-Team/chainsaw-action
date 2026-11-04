using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>実描画フレーム単位の画像列。Time.timeScale=0でも進行する。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30000)]
public sealed class ImpactImageSequence : MonoBehaviour
{
    [Serializable]
    public sealed class Frame
    {
        public Sprite image;
        [Min(1)] public int frames = 1;
    }

    [Tooltip("先頭から順番に表示。未設定の画像はスキップ")]
    [SerializeField] private Frame[] sequence = new Frame[0];
    [SerializeField] private int sortingOrder = 30000;
    [SerializeField] private bool preserveAspect;
    private GameObject root;
    private Image output;
    private int index;
    private int remaining;
    private int changedFrame;
    private bool playing;

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        EnsureOutput();
        index = -1;
        playing = true;
        Advance();
    }

    private void EnsureOutput()
    {
        if (output != null) return;
        root = new GameObject("Impact Image Overlay", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        var child = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(root.transform, false);
        output = child.GetComponent<Image>();
        output.raycastTarget = false;
        output.color = Color.white;
        output.preserveAspect = preserveAspect;
        RectTransform rect = output.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void LateUpdate()
    {
        if (!playing || changedFrame == Time.frameCount) return;
        if (--remaining <= 0) Advance();
    }

    private void Advance()
    {
        while (sequence != null && ++index < sequence.Length)
        {
            Frame entry = sequence[index];
            if (entry == null || entry.image == null) continue;
            output.sprite = entry.image;
            output.enabled = true;
            remaining = Mathf.Max(1, entry.frames);
            changedFrame = Time.frameCount;
            return;
        }
        Stop();
    }

    public void Stop()
    {
        playing = false;
        if (output != null)
        {
            output.enabled = false;
            output.sprite = null;
        }
    }

    private void OnDisable() => Stop();
    private void OnDestroy()
    {
        if (root != null) Destroy(root);
    }
}

