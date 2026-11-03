using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 両デバッグUIの表示とカーソルを共有する。GameObjectへの追加は不要。
public static class DebugDisplayVisibility
{
    private static readonly HashSet<Object> openMenus = new HashSet<Object>();
    private static int lastInputFrame = -1;
    private static int lastToggleFrame = -1;
    private static bool cursorCaptured;
    private static CursorLockMode previousLock;
    private static bool previousVisible;

    public static bool IsVisible { get; private set; } = true;
    public static bool ToggledThisFrame => lastToggleFrame == Time.frameCount;
#if ENABLE_INPUT_SYSTEM
    public static Key ToggleKey { get; set; } = Key.F3;
#else
    public static KeyCode ToggleKey { get; set; } = KeyCode.F3;
#endif

    // ドメインリロードを省略した再生でも、前回の表示状態とメニュー参照を引き継がない。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        openMenus.Clear();
        IsVisible = true;
        lastInputFrame = -1;
        lastToggleFrame = -1;
        cursorCaptured = false;
#if ENABLE_INPUT_SYSTEM
        ToggleKey = Key.F3;
#else
        ToggleKey = KeyCode.F3;
#endif
    }

    // 両メニューから呼ばれても、同じフレームのキー入力は1回だけ処理する。
    public static void ProcessInput()
    {
        if (lastInputFrame == Time.frameCount) return;
        lastInputFrame = Time.frameCount;
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed = ToggleKey != Key.None && Keyboard.current != null &&
            Keyboard.current[ToggleKey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        pressed = ToggleKey != KeyCode.None && Input.GetKeyDown(ToggleKey);
#endif
        if (!pressed) return;
        IsVisible = !IsVisible;
        lastToggleFrame = Time.frameCount;
        RefreshCursor();
    }

    // 開いているメニューを共有管理し、両方を開いた場合も最後の1つが閉じるまでカーソルを維持する。
    public static void SetMenuOpen(Object menu, bool isOpen)
    {
        if (isOpen) openMenus.Add(menu);
        else openMenus.Remove(menu);
        RefreshCursor();
    }

    // デバッグUIを隠した場合は元のカーソルへ戻し、再表示時に開いていたメニューへ復帰する。
    private static void RefreshCursor()
    {
        openMenus.RemoveWhere(menu => menu == null);
        if (IsVisible && openMenus.Count > 0)
        {
            if (!cursorCaptured)
            {
                previousLock = Cursor.lockState;
                previousVisible = Cursor.visible;
                cursorCaptured = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (cursorCaptured)
        {
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisible;
            cursorCaptured = false;
        }
    }
}
