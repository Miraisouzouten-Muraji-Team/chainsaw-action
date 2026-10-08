using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanelUI : MonoBehaviour
{
    [Header("Master Volume")]
    [SerializeField]
    private Slider masterVolumeSlider;

    [SerializeField]
    private TMP_Text masterVolumeValueText;

    [Header("BGM Volume")]
    [SerializeField]
    private Slider bgmVolumeSlider;

    [SerializeField]
    private TMP_Text bgmVolumeValueText;

    [Header("SE Volume")]
    [SerializeField]
    private Slider seVolumeSlider;

    [SerializeField]
    private TMP_Text seVolumeValueText;

    [Header("Controller")]
    [SerializeField]
    private TMP_Dropdown ChainsawModeDropdown;

    private void Awake()
    {
        SetupSlider(
            masterVolumeSlider,
            OnMasterVolumeChanged
        );

        SetupSlider(
            bgmVolumeSlider,
            OnBgmVolumeChanged
        );

        SetupSlider(
            seVolumeSlider,
            OnSeVolumeChanged
        );

        if (ChainsawModeDropdown != null)
        {
            ChainsawModeDropdown.ClearOptions();

            ChainsawModeDropdown.AddOptions(
                new List<string>
                {
                    "í∑âüÇµ",
                    "êÿÇËë÷Ç¶"
                }
            );

            ChainsawModeDropdown.onValueChanged.AddListener(
                OnChainsawModeChanged
            );
        }
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void SetupSlider(
        Slider slider,
        UnityEngine.Events.UnityAction<float> callback)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        slider.onValueChanged.AddListener(
            callback
        );
    }

    private void Refresh()
    {
        if (SettingsManager.Instance == null)
            return;

        GameSettingsData settings =
            SettingsManager.Instance.Current;

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(
                settings.MasterVolume
            );
        }

        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.SetValueWithoutNotify(
                settings.BgmVolume
            );
        }

        if (seVolumeSlider != null)
        {
            seVolumeSlider.SetValueWithoutNotify(
                settings.SeVolume
            );
        }

        if (ChainsawModeDropdown != null)
        {
            ChainsawModeDropdown.SetValueWithoutNotify(
                (int)settings.ChainsawMode
            );
        }

        UpdateVolumeText(
            masterVolumeValueText,
            settings.MasterVolume
        );

        UpdateVolumeText(
            bgmVolumeValueText,
            settings.BgmVolume
        );

        UpdateVolumeText(
            seVolumeValueText,
            settings.SeVolume
        );
    }

    // =========================
    // Master
    // =========================

    private void OnMasterVolumeChanged(float value)
    {
        UpdateVolumeText(
            masterVolumeValueText,
            value
        );

        SettingsManager.Instance
            ?.SetMasterVolume(value);
    }

    // =========================
    // BGM
    // =========================

    private void OnBgmVolumeChanged(float value)
    {
        UpdateVolumeText(
            bgmVolumeValueText,
            value
        );

        SettingsManager.Instance
            ?.SetBgmVolume(value);
    }

    // =========================
    // SE
    // =========================

    private void OnSeVolumeChanged(float value)
    {
        UpdateVolumeText(
            seVolumeValueText,
            value
        );

        SettingsManager.Instance
            ?.SetSeVolume(value);
    }

    // =========================
    // Aim
    // =========================

    private void OnChainsawModeChanged(int value)
    {
        SettingsManager.Instance
            ?.SetChainsawMode(
                (ChainsawInputMode)value
            );
    }

    // =========================
    // Text
    // =========================

    private void UpdateVolumeText(
        TMP_Text text,
        float value)
    {
        if (text == null)
            return;

        int percent =
            Mathf.RoundToInt(
                value * 100f
            );

        text.text =
            percent + "%";
    }
}