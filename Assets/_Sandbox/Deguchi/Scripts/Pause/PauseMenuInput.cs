using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseMenuInput : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField]
    private PauseMenuController pauseMenu;

    [Header("Virtual Cursor")]
    [SerializeField]
    private Canvas cursorCanvas;

    [SerializeField]
    private RectTransform cursorVisual;

    [SerializeField]
    private float cursorSpeed = 900f;

    [SerializeField]
    [Range(0f, 1f)]
    private float stickDeadZone = 0.2f;

    private Vector2 cursorScreenPosition;

    private bool cursorMode;

    private PointerEventData pointerData;

    private GameObject pointerPress;

    private GameObject pointerDrag;

    private Vector2 previousPointerPosition;

    private void Start()
    {
        cursorScreenPosition =
            new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f
            );

        UpdateCursorVisual();

        EnterNavigationMode();
    }

    private void Update()
    {
        HandleMenuButton();

        if (!pauseMenu.IsPaused)
        {
            EnterNavigationMode();
            return;
        }

        HandleBackButton();
        HandleShoulders();
        HandleNavigationMode();
        HandleStickCursor();
        HandleCrossButton();
    }

    // -------------------------
    // ESC / OPTIONS
    // -------------------------

    private void HandleMenuButton()
    {
        bool pressed = false;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            pressed = true;
        }

        if (Gamepad.current != null &&
            Gamepad.current.startButton.wasPressedThisFrame)
        {
            pressed = true;
        }

        if (!pressed)
            return;

        pauseMenu.HandleMenuButton();

        if (pauseMenu.IsPaused)
        {
            EnterNavigationMode();
        }
    }

    // -------------------------
    // ○ = 戻る
    // -------------------------

    private void HandleBackButton()
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        if (pad.buttonEast.wasPressedThisFrame)
        {
            pauseMenu.HandleBackButton();

            EnterNavigationMode();
        }
    }

    // -------------------------
    // L1 / R1
    // -------------------------

    private void HandleShoulders()
    {
        if (pauseMenu.State !=
            PauseMenuState.Settings)
        {
            return;
        }

        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        if (pad.leftShoulder.wasPressedThisFrame)
        {
            pauseMenu.ShowPreviousSettingsTab();

            EnterNavigationMode();
        }

        if (pad.rightShoulder.wasPressedThisFrame)
        {
            pauseMenu.ShowNextSettingsTab();

            EnterNavigationMode();
        }
    }

    // -------------------------
    // 十字キーを使ったら
    // Navigationモードへ戻す
    // -------------------------

    private void HandleNavigationMode()
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        Vector2 dpad =
            pad.dpad.ReadValue();

        if (dpad.sqrMagnitude > 0.25f)
        {
            EnterNavigationMode();
        }
    }

    // -------------------------
    // 左スティックカーソル
    // -------------------------

    private void HandleStickCursor()
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        Vector2 stick =
            pad.leftStick.ReadValue();

        if (stick.magnitude < stickDeadZone)
            return;

        EnterCursorMode();

        cursorScreenPosition +=
            stick *
            cursorSpeed *
            Time.unscaledDeltaTime;

        cursorScreenPosition.x =
            Mathf.Clamp(
                cursorScreenPosition.x,
                0f,
                Screen.width
            );

        cursorScreenPosition.y =
            Mathf.Clamp(
                cursorScreenPosition.y,
                0f,
                Screen.height
            );

        UpdateCursorVisual();

        SelectObjectUnderCursor();
    }

    private void UpdateCursorVisual()
    {
        if (cursorVisual == null ||
            cursorCanvas == null)
        {
            return;
        }

        RectTransform canvasRect =
            cursorCanvas.transform
                as RectTransform;

        if (canvasRect == null)
            return;

        if (RectTransformUtility
            .ScreenPointToLocalPointInRectangle(
                canvasRect,
                cursorScreenPosition,
                null,
                out Vector2 localPoint))
        {
            cursorVisual.localPosition =
                localPoint;
        }
    }

    // -------------------------
    // カーソル下のUIを選択
    // -------------------------

    private void SelectObjectUnderCursor()
    {
        if (!TryRaycast(
                out RaycastResult hit))
        {
            return;
        }

        Selectable selectable =
            hit.gameObject
                .GetComponentInParent<Selectable>();

        if (selectable == null)
            return;

        if (!selectable.IsInteractable())
            return;

        EventSystem.current
            .SetSelectedGameObject(
                selectable.gameObject
            );
    }

    // -------------------------
    // ×
    // -------------------------

    private void HandleCrossButton()
    {
        Gamepad pad = Gamepad.current;

        if (pad == null)
            return;

        // 十字キー操作
        if (!cursorMode)
        {
            if (pad.buttonSouth
                .wasPressedThisFrame)
            {
                SubmitSelected();
            }

            return;
        }

        // 仮想カーソル操作
        if (pad.buttonSouth.wasPressedThisFrame)
        {
            PointerDown();
        }

        if (pad.buttonSouth.isPressed)
        {
            PointerDrag();
        }

        if (pad.buttonSouth.wasReleasedThisFrame)
        {
            PointerUp();
        }
    }

    // -------------------------
    // 十字キー選択時の決定
    // -------------------------

    private void SubmitSelected()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected =
            EventSystem.current
                .currentSelectedGameObject;

        if (selected == null)
            return;

        BaseEventData data =
            new BaseEventData(
                EventSystem.current
            );

        ExecuteEvents.Execute(
            selected,
            data,
            ExecuteEvents.submitHandler
        );
    }

    // -------------------------
    // 仮想マウス Down
    // -------------------------

    private void PointerDown()
    {
        if (!TryRaycast(
                out RaycastResult hit))
        {
            return;
        }

        pointerData =
            CreatePointerData();

        pointerData.pointerCurrentRaycast =
            hit;

        pointerData.pointerPressRaycast =
            hit;

        pointerData.pressPosition =
            cursorScreenPosition;

        GameObject target =
            hit.gameObject;

        pointerPress =
            ExecuteEvents.ExecuteHierarchy(
                target,
                pointerData,
                ExecuteEvents.pointerDownHandler
            );

        if (pointerPress == null)
        {
            pointerPress =
                ExecuteEvents
                    .GetEventHandler<
                        IPointerClickHandler
                    >(target);
        }

        pointerData.pointerPress =
            pointerPress;

        pointerData.rawPointerPress =
            target;

        pointerDrag =
            ExecuteEvents
                .GetEventHandler<IDragHandler>(
                    target
                );

        pointerData.pointerDrag =
            pointerDrag;

        if (pointerDrag != null)
        {
            ExecuteEvents.Execute(
                pointerDrag,
                pointerData,
                ExecuteEvents.beginDragHandler
            );
        }

        previousPointerPosition =
            cursorScreenPosition;
    }

    // -------------------------
    // 仮想マウス Drag
    // -------------------------

    private void PointerDrag()
    {
        if (pointerData == null ||
            pointerDrag == null)
        {
            return;
        }

        pointerData.position =
            cursorScreenPosition;

        pointerData.delta =
            cursorScreenPosition -
            previousPointerPosition;

        ExecuteEvents.Execute(
            pointerDrag,
            pointerData,
            ExecuteEvents.dragHandler
        );

        previousPointerPosition =
            cursorScreenPosition;
    }

    // -------------------------
    // 仮想マウス Up
    // -------------------------

    private void PointerUp()
    {
        if (pointerData == null)
            return;

        pointerData.position =
            cursorScreenPosition;

        if (pointerPress != null)
        {
            ExecuteEvents.Execute(
                pointerPress,
                pointerData,
                ExecuteEvents.pointerUpHandler
            );
        }

        GameObject clickHandler = null;

        if (TryRaycast(
                out RaycastResult hit))
        {
            clickHandler =
                ExecuteEvents
                    .GetEventHandler<
                        IPointerClickHandler
                    >(hit.gameObject);
        }

        if (pointerPress != null &&
            pointerPress == clickHandler)
        {
            ExecuteEvents.Execute(
                pointerPress,
                pointerData,
                ExecuteEvents.pointerClickHandler
            );
        }

        if (pointerDrag != null)
        {
            ExecuteEvents.Execute(
                pointerDrag,
                pointerData,
                ExecuteEvents.endDragHandler
            );
        }

        pointerPress = null;
        pointerDrag = null;
        pointerData = null;
    }

    // -------------------------
    // Raycast
    // -------------------------

    private bool TryRaycast(
        out RaycastResult hit)
    {
        hit = default;

        if (EventSystem.current == null)
            return false;

        PointerEventData data =
            CreatePointerData();

        List<RaycastResult> results =
            new List<RaycastResult>();

        EventSystem.current.RaycastAll(
            data,
            results
        );

        if (results.Count == 0)
            return false;

        hit = results[0];

        return true;
    }

    private PointerEventData CreatePointerData()
    {
        PointerEventData data =
            new PointerEventData(
                EventSystem.current
            );

        data.position =
            cursorScreenPosition;

        data.button =
            PointerEventData.InputButton.Left;

        return data;
    }

    // -------------------------
    // モード切替
    // -------------------------

    private void EnterNavigationMode()
    {
        cursorMode = false;

        if (cursorVisual != null)
        {
            cursorVisual.gameObject
                .SetActive(false);
        }
    }

    private void EnterCursorMode()
    {
        cursorMode = true;

        if (cursorVisual != null)
        {
            cursorVisual.gameObject
                .SetActive(true);
        }
    }
}