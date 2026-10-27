using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Project.Tutorial
{
    [DefaultExecutionOrder(-10000), DisallowMultipleComponent]
    public sealed class TutorialPresenter : MonoBehaviour
    {
        [Tooltip("日本語対応のDynamic Font推奨。未指定時はUnity標準フォント。")]
        public Font uiFont;
        private static TutorialPresenter instance;
        public static bool IsModalOpen => instance != null && instance.paused;
        private Canvas canvas;
        private RectTransform canvasRect;
        private TutorialView sign, modal;
        private TutorialSignOcclusion signOcclusion;
        private Text prompt;
        private GameObject shade;
        private TutorialZone selected, shownSign, modalZone;
        private TutorialPlayer selectedPlayer, modalPlayer;
        private TutorialDefinition modalDefinition;
        private int pageIndex, closingFrame;
        private float signClock, modalClock, savedTimeScale;
        private bool paused, closing, navigationHeld;
        private CursorLockMode savedLock;
        private bool savedCursor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }
        internal static void EnsureExists()
        {
            if (instance != null) return;
            var found = FindFirstObjectByType<TutorialPresenter>();
            if (found != null) return;
            new GameObject("Tutorial Presenter").AddComponent<TutorialPresenter>();
        }
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            // 専用のルートオブジェクトに配置する。
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            BuildUI();
        }
        private void OnEnable()
        {
            if (canvas != null) canvas.gameObject.SetActive(true);
            SceneManager.activeSceneChanged += SceneChanged;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        private void SceneChanged(Scene oldScene, Scene newScene)
        {
            CloseImmediately(); ClearSelection(); EnsureEventSystem();
        }
        private void SceneUnloaded(Scene scene)
        {
            if (paused && (modalZone == null || modalZone.gameObject.scene == scene)) CloseImmediately();
        }
        private void ClearSelection()
        {
            selected = shownSign = null; selectedPlayer = null;
            if (sign != null) sign.root.gameObject.SetActive(false);
            if (prompt != null) prompt.gameObject.SetActive(false);
        }
        private void BuildUI()
        {
            Font font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRect = TutorialView.Rect("Tutorial Overlay", transform, Vector2.zero);
            canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            sign = new TutorialView(canvasRect, font, false); sign.group.alpha = 0f;
            signOcclusion = sign.root.gameObject.AddComponent<TutorialSignOcclusion>();
            prompt = TutorialView.Label(canvasRect, font, "Interact Prompt", 28, Color.cyan);
            prompt.rectTransform.sizeDelta = new Vector2(520, 56);
            prompt.text = "[ X / E ] 説明を見る"; prompt.gameObject.SetActive(false);
            var shadeRect = TutorialView.Rect("Modal Backdrop", canvasRect, Vector2.zero);
            TutorialView.Place(shadeRect, Vector2.zero, Vector2.one);
            var backdrop = shadeRect.gameObject.AddComponent<Image>(); backdrop.color = new Color(0, 0, 0, .72f);
            shade = shadeRect.gameObject; shade.SetActive(false);
            modal = new TutorialView(canvasRect, font, true);
            modal.previous.onClick.AddListener(() => ChangePage(-1));
            modal.next.onClick.AddListener(() => ChangePage(1));
            modal.close.onClick.AddListener(RequestClose);
            EnsureEventSystem();
        }
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("Tutorial EventSystem", typeof(EventSystem));
            // シーンごとに既存EventSystemを優先。自動生成物はそのシーンと一緒に破棄される。
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private struct InputState
        {
            internal bool open, close, actionHeld;
            internal float horizontal;
        }
        private static InputState ReadInput()
        {
            var state = new InputState();
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null)
            {
                state.open = pad.buttonWest.wasPressedThisFrame;
                state.close = pad.buttonEast.wasPressedThisFrame;
                state.actionHeld = pad.buttonWest.isPressed || pad.buttonEast.isPressed ||
                    pad.buttonSouth.isPressed || pad.buttonNorth.isPressed;
                state.horizontal = pad.leftStick.x.ReadValue();
            }
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                state.open |= keyboard.eKey.wasPressedThisFrame;
                state.close |= keyboard.escapeKey.wasPressedThisFrame;
                state.actionHeld |= keyboard.eKey.isPressed || keyboard.escapeKey.isPressed || keyboard.spaceKey.isPressed;
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) state.horizontal = -1;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) state.horizontal = 1;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            state.open = Input.GetKeyDown(KeyCode.E);
            state.close = Input.GetKeyDown(KeyCode.Escape);
            state.actionHeld = Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.Space);
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) state.horizontal = -1;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) state.horizontal = 1;
#endif
            return state;
        }
        private void Update()
        {
            if (instance != this || canvas == null) return;
            var input = ReadInput();
            if (paused)
            {
                if (modalZone == null || !modalZone.isActiveAndEnabled || modalPlayer == null || !modalPlayer.isActiveAndEnabled)
                { CloseImmediately(); return; }
                if (closing)
                {
                    // 閉じるボタンの押しっぱなしやスティックがゲーム側へ漏れるのを防ぐ。
                    if (Time.frameCount > closingFrame && !input.actionHeld && Mathf.Abs(input.horizontal) < .25f)
                        CloseImmediately();
                    return;
                }
                modalClock += Time.unscaledDeltaTime; modal.Tick(modalClock);
                if (input.close) { RequestClose(); return; }
                if (Mathf.Abs(input.horizontal) < .25f) navigationHeld = false;
                if (!navigationHeld && Mathf.Abs(input.horizontal) > .65f)
                { navigationHeld = true; ChangePage(input.horizontal > 0 ? 1 : -1); }
                return;
            }
            SelectZone();
            bool showSign = selected != null && selected.definition.mode == TutorialMode.Sign;
            if (showSign) signOcclusion.Configure(selectedPlayer, selected);
            if (showSign && shownSign != selected)
            {
                shownSign = selected; signClock = 0f; sign.group.alpha = 0f;
                sign.SetPage(selected.definition.pages[0]); sign.footer.text = "TUTORIAL";
            }
            float fade = shownSign != null ? Mathf.Max(.01f, shownSign.definition.fadeSeconds) : .5f;
            sign.group.alpha = Mathf.MoveTowards(sign.group.alpha, showSign ? 1f : 0f, Time.unscaledDeltaTime / fade);
            sign.root.gameObject.SetActive(sign.group.alpha > 0f && shownSign != null);
            if (shownSign != null && sign.root.gameObject.activeSelf)
            { signClock += Time.unscaledDeltaTime; sign.Tick(signClock); Position(sign.root, shownSign); }
            bool interactive = selected != null && selected.definition.mode == TutorialMode.InteractiveSlides;
            prompt.gameObject.SetActive(interactive);
            if (interactive)
            {
                Position(prompt.rectTransform, selected);
                if (input.open && Time.timeScale > 0f) Open();
            }
        }
        private void SelectZone()
        {
            selected = null; selectedPlayer = null;
            float distance = float.PositiveInfinity; int priority = int.MinValue;
            foreach (var zone in TutorialZone.Active)
            {
                if (zone == null || !zone.isActiveAndEnabled || zone.definition == null ||
                    zone.definition.pages == null || zone.definition.pages.Length == 0 || zone.definition.pages[0] == null) continue;
                var player = zone.FindPlayer(); if (player == null) continue;
                float d = (player.transform.position - zone.transform.position).sqrMagnitude;
                if (zone.priority < priority || (zone.priority == priority && d >= distance)) continue;
                selected = zone; selectedPlayer = player; priority = zone.priority; distance = d;
            }
        }
        private void Position(RectTransform target, TutorialZone zone)
        {
            var camera = zone.projectionCamera != null ? zone.projectionCamera : Camera.main;
            if (camera == null) { target.gameObject.SetActive(false); return; }
            Vector3 view = camera.WorldToViewportPoint(zone.AnchorPosition);
            if (view.z < camera.nearClipPlane || view.z > camera.farClipPlane)
            { target.gameObject.SetActive(false); return; }
            Rect viewport = zone.outputViewport;
            // RenderTexture合成は既定で全画面。通常カメラは自身のviewportを使う。
            if (camera.targetTexture == null && viewport == new Rect(0, 0, 1, 1)) viewport = camera.rect;
            Vector2 screen = new Vector2((viewport.x + view.x * viewport.width) * Screen.width,
                (viewport.y + view.y * viewport.height) * Screen.height);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            Vector2 half = target.rect.size * .5f;
            float limitX = Mathf.Max(0, canvasRect.rect.width * .5f - half.x - 16);
            float limitY = Mathf.Max(0, canvasRect.rect.height * .5f - half.y - 16);
            target.anchoredPosition = new Vector2(Mathf.Clamp(local.x, -limitX, limitX), Mathf.Clamp(local.y, -limitY, limitY));
        }
        private void Open()
        {
            modalZone = selected; modalPlayer = selectedPlayer; modalDefinition = selected.definition;
            savedTimeScale = Time.timeScale; savedLock = Cursor.lockState; savedCursor = Cursor.visible;
            paused = true; closing = false;
            modalPlayer.SuspendInput(); Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            sign.root.gameObject.SetActive(false); prompt.gameObject.SetActive(false);
            shade.SetActive(true); modal.root.gameObject.SetActive(true);
            pageIndex = 0; navigationHeld = true; RefreshPage();
        }
        private void ChangePage(int direction)
        {
            if (!paused || closing || modalDefinition == null) return;
            int next = Mathf.Clamp(pageIndex + direction, 0, modalDefinition.pages.Length - 1);
            if (next == pageIndex) return;
            pageIndex = next; RefreshPage();
        }
        private void RefreshPage()
        {
            modalClock = 0f;
            modal.SetPage(modalDefinition.pages[pageIndex] ?? new TutorialPage { title = "未設定", description = "このページの内容を設定してください。" });
            modal.Tick(0f);
            modal.footer.text = $"{pageIndex + 1} / {modalDefinition.pages.Length}　 Lスティック / ← →：切替　 B / Esc：閉じる";
            modal.previous.interactable = pageIndex > 0;
            modal.next.interactable = pageIndex < modalDefinition.pages.Length - 1;
        }
        private void RequestClose()
        {
            if (!paused || closing) return;
            closing = true; closingFrame = Time.frameCount;
            modal.footer.text = "ボタンを離し、スティックを中央に戻すと再開します";
        }
        private void CloseImmediately()
        {
            if (!paused) return;
            if (modalPlayer != null) modalPlayer.RestoreInput();
            // 別システムが既に時間を変更した場合は上書きしない。
            if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = savedTimeScale;
            Cursor.lockState = savedLock; Cursor.visible = savedCursor;
            paused = closing = false; modalPlayer = null; modalZone = null; modalDefinition = null;
            if (modal != null) modal.root.gameObject.SetActive(false);
            if (shade != null) shade.SetActive(false);
        }
        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            CloseImmediately();
            if (canvas != null) canvas.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            CloseImmediately(); if (instance == this) instance = null;
        }
    }
}
