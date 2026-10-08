using System;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [Header("Audio Mixer")]
    [SerializeField]
    private AudioMixer audioMixer;

    [Header("Exposed Parameter Names")]
    [SerializeField]
    private string masterVolumeParameter = "MasterVolume";

    [SerializeField]
    private string bgmVolumeParameter = "BGMVolume";

    [SerializeField]
    private string seVolumeParameter = "SEVolume";

    public GameSettingsData Current { get; private set; }

    public event Action<GameSettingsData> OnSettingsChanged;

    private const string MasterVolumeKey =
        "Settings_MasterVolume";

    private const string BgmVolumeKey =
        "Settings_BgmVolume";

    private const string SeVolumeKey =
        "Settings_SeVolume";

    private const string ChainsawModeKey =
        "Settings_ChainsawMode";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        // AwakeÇ≈ÇÕê›íËílÇæÇØì«Ç›çûÇﬁ
        Load();
    }

    private void Start()
    {
        // AudioMixerÇ÷ÇÃîΩâfÇÕStartÇ≈çsÇ§
        ApplyAll();
    }

    // =========================
    // Master Volume
    // =========================

    public void SetMasterVolume(float value)
    {
        Current.MasterVolume =
            Mathf.Clamp01(value);

        ApplyMasterVolume();

        OnSettingsChanged?.Invoke(Current);
    }

    // =========================
    // BGM Volume
    // =========================

    public void SetBgmVolume(float value)
    {
        Current.BgmVolume =
            Mathf.Clamp01(value);

        ApplyBgmVolume();

        OnSettingsChanged?.Invoke(Current);
    }

    // =========================
    // SE Volume
    // =========================

    public void SetSeVolume(float value)
    {
        Current.SeVolume =
            Mathf.Clamp01(value);

        ApplySeVolume();

        OnSettingsChanged?.Invoke(Current);
    }

    // =========================
    // Aim
    // =========================

    public void SetChainsawMode(ChainsawInputMode mode)
    {
        Current.ChainsawMode = mode;

        OnSettingsChanged?.Invoke(Current);
    }

    // =========================
    // Save
    // =========================

    public void Save()
    {
        PlayerPrefs.SetFloat(
            MasterVolumeKey,
            Current.MasterVolume
        );

        PlayerPrefs.SetFloat(
            BgmVolumeKey,
            Current.BgmVolume
        );

        PlayerPrefs.SetFloat(
            SeVolumeKey,
            Current.SeVolume
        );

        PlayerPrefs.SetInt(
            ChainsawModeKey,
            (int)Current.ChainsawMode
        );

        PlayerPrefs.Save();
    }

    // =========================
    // Load
    // =========================

    private void Load()
    {
        Current = new GameSettingsData
        {
            MasterVolume =
                PlayerPrefs.GetFloat(
                    MasterVolumeKey,
                    0.8f
                ),

            BgmVolume =
                PlayerPrefs.GetFloat(
                    BgmVolumeKey,
                    0.8f
                ),

            SeVolume =
                PlayerPrefs.GetFloat(
                    SeVolumeKey,
                    0.8f
                ),

            ChainsawMode =
                (ChainsawInputMode)
                PlayerPrefs.GetInt(
                    ChainsawModeKey,
                    (int)ChainsawInputMode.Hold
                )
        };
    }

    // =========================
    // Apply
    // =========================

    private void ApplyAll()
    {
        ApplyMasterVolume();
        ApplyBgmVolume();
        ApplySeVolume();
    }

    private void ApplyMasterVolume()
    {
        SetMixerVolume(
            masterVolumeParameter,
            Current.MasterVolume
        );
    }

    private void ApplyBgmVolume()
    {
        SetMixerVolume(
            bgmVolumeParameter,
            Current.BgmVolume
        );
    }

    private void ApplySeVolume()
    {
        SetMixerVolume(
            seVolumeParameter,
            Current.SeVolume
        );
    }

    private void SetMixerVolume(
        string parameter,
        float value)
    {
        if (audioMixer == null)
            return;

        float db =
            value <= 0.0001f
                ? -80f
                : Mathf.Log10(value) * 20f;

        audioMixer.SetFloat(
            parameter,
            db
        );
    }
}