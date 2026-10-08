using System;

//ゲーム内の設定数値だけ記録しているスクリプト
//設定で変更するデータをここで管理しておく
public enum ChainsawInputMode
{
    Hold = 0,
    Toggle = 1
}

[Serializable]
public class GameSettingsData
{
    //ゲームの主音量
    public float MasterVolume = 0.5f;
    public float BgmVolume = 0.5f;

    public float SeVolume = 0.5f;


    // ゲームコントローラー感度　1 ～ 10
    //public float ControllerSensitivity = 5.0f;

    // チェーンソーの実行モード
    public ChainsawInputMode ChainsawMode = ChainsawInputMode.Hold;
}