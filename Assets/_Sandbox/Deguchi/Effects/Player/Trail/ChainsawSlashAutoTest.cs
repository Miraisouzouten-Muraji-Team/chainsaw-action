
using UnityEngine;

public class ChainsawSlashAutoTest : MonoBehaviour
{
    [Header("Particle")]
    [SerializeField] private ParticleSystem slashParticle;

    [Header("Timing")]
    [SerializeField] private float slashDuration = 0.25f;
    [SerializeField] private float interval = 1.2f;
    [SerializeField] private bool repeat = true;

    [Header("Motion")]
    [SerializeField] private bool smoothMotion = false;

    private Material runtimeMaterial;
    private Material originalMaterial;
    private ParticleSystemRenderer particleRenderer;

    private float timer;
    private bool playing;
    private bool finished;

    private static readonly int ProgressID =
        Shader.PropertyToID("_Progress");

    private static readonly int GlobalAlphaID =
        Shader.PropertyToID("_GlobalAlpha");

    private void Start()
    {
        if (slashParticle == null)
        {
            Debug.LogError("Slash Particleが未設定です。");
            enabled = false;
            return;
        }

        particleRenderer =
            slashParticle.GetComponent<ParticleSystemRenderer>();

        if (particleRenderer == null ||
            particleRenderer.sharedMaterial == null)
        {
            Debug.LogError("ParticleのMaterialがありません。");
            enabled = false;
            return;
        }

        originalMaterial = particleRenderer.sharedMaterial;

        runtimeMaterial = new Material(originalMaterial);
        particleRenderer.sharedMaterial = runtimeMaterial;

        slashParticle.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        runtimeMaterial.SetFloat(ProgressID, 0f);
        runtimeMaterial.SetFloat(GlobalAlphaID, 1f);

        PlaySlash();
    }

    private void Update()
    {
        if (runtimeMaterial == null)
            return;

        if (playing)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(
                timer / Mathf.Max(0.01f, slashDuration)
            );

            float progress = smoothMotion
                ? Mathf.SmoothStep(0f, 1f, t)
                : t;

            runtimeMaterial.SetFloat(ProgressID, progress);

            if (t >= 1f)
            {
                playing = false;
                finished = true;
                timer = 0f;
            }
        }
        else if (finished && repeat)
        {
            timer += Time.deltaTime;

            if (timer >= interval)
            {
                PlaySlash();
            }
        }
    }

    [ContextMenu("Play Slash")]
    public void PlaySlash()
    {
        if (runtimeMaterial == null)
            return;

        timer = 0f;
        playing = true;
        finished = false;

        runtimeMaterial.SetFloat(ProgressID, 0f);
        runtimeMaterial.SetFloat(GlobalAlphaID, 1f);

        slashParticle.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        slashParticle.Emit(1);
    }

    private void OnDestroy()
    {
        if (particleRenderer != null)
            particleRenderer.sharedMaterial = originalMaterial;

        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
