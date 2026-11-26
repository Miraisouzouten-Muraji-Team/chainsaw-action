using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace Project.Tutorial
{
    // チュートリアルの画像と、任意の操作画像をOverlay Canvas上に配置する。
    internal sealed class TutorialView
    {
        internal readonly RectTransform root;
        internal readonly CanvasGroup group;
        private readonly Button previous, next, close;
        private readonly RectTransform imageRoot;
        private readonly TutorialLegacyView legacyView;
        private bool imageOnly;
        private readonly Image artwork;
        private readonly SpriteRenderer clipSpriteSource;
        private Animator clipAnimator;
        private PlayableGraph clipGraph;
        private AnimationClipPlayable clipPlayable;
        private AnimationClip clip;
        private Sprite[] frames = System.Array.Empty<Sprite>();
        private float cycleSeconds;
        private RuntimeAnimatorController pageController;
        private CanvasGroup controllerVisibility;
        private Image controllerArtwork;
        private SpriteRenderer controllerSpriteSource;
        private Sprite lastControllerSprite;
        private float controllerClock;

        // 開閉用の親と、クリップが動かす画像を別に作る。
        internal TutorialView(Transform parent, Font font, bool modal)
        {
            Vector2 size = modal ? new Vector2(960, 600) : new Vector2(560, 350);
            root = Rect("Tutorial Card", parent, size);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = modal;

            imageRoot = Rect("Image and Clip", root, Vector2.zero);
            Place(imageRoot, Vector2.zero, Vector2.one);
            RectTransform imageRect = Rect("Artwork", imageRoot, Vector2.zero);
            Place(imageRect, Vector2.zero, Vector2.one);
            artwork = imageRect.gameObject.AddComponent<Image>();
            artwork.preserveAspect = true;
            artwork.raycastTarget = false;
            artwork.enabled = false;

            // SpriteRenderer向けの連番クリップでは、画像参照だけをUIへ渡す。
            clipSpriteSource = imageRect.gameObject.AddComponent<SpriteRenderer>();
            clipSpriteSource.forceRenderingOff = true;

            if (modal)
            {
                previous = MakeButton(imageRoot, "Previous Page", new Vector2(-430, 0), new Vector2(62, 72));
                next = MakeButton(imageRoot, "Next Page", new Vector2(430, 0), new Vector2(62, 72));
                close = MakeButton(imageRoot, "Close", new Vector2(382, 255), new Vector2(130, 42));
            }

            imageRoot.gameObject.SetActive(false);
            legacyView = new TutorialLegacyView(root, font, modal);
            root.gameObject.SetActive(false);
        }

        // 表示方式を切り替え、選択したページを先頭から表示する。
        internal void SetPage(TutorialPage page, bool useImageAndClip)
        {
            ReleaseClip();
            imageOnly = useImageAndClip;
            imageRoot.gameObject.SetActive(imageOnly);
            legacyView.root.gameObject.SetActive(!imageOnly);
            if (!imageOnly)
            {
                legacyView.SetPage(page);
                return;
            }

            // クリップが変更した前のページの画像設定を初期化する。
            AnimationClip assignedClip = page?.animationClip;
            clip = assignedClip != null && !assignedClip.empty ? assignedClip : null;
            frames = page?.animationFrames != null
                ? System.Array.FindAll(page.animationFrames, frame => frame != null)
                : System.Array.Empty<Sprite>();
            cycleSeconds = Mathf.Max(0.2f, page?.cycleSeconds ?? 2.4f);
            RectTransform imageRect = artwork.rectTransform;
            imageRect.gameObject.SetActive(true);
            imageRect.pivot = new Vector2(.5f, .5f);
            imageRect.localRotation = Quaternion.identity;
            imageRect.localScale = Vector3.one;
            imageRect.anchoredPosition3D = Vector3.zero;
            Place(imageRect, Vector2.zero, Vector2.one);

            artwork.color = Color.white;
            artwork.preserveAspect = true;
            artwork.overrideSprite = null;
            artwork.sprite = page?.illustration;
            if (artwork.sprite == null && frames.Length > 0)
            {
                artwork.sprite = frames[0];
            }

            clipSpriteSource.sprite = null;
            // サインはこの時点で非表示の場合がある。再生開始は有効になった最初のTickで行う。
            artwork.enabled = artwork.sprite != null;

            pageController = page?.animatorController;
            if (pageController != null)
            {
                CreateControllerArtwork(artwork.sprite);
                artwork.gameObject.SetActive(false);
            }
        }

        // Controller専用の再生先をページごとに作り、アニメーション値を次のページへ持ち越さない。
        private void CreateControllerArtwork(Sprite initialSprite)
        {
            RectTransform container = Rect("Controller Artwork", imageRoot, Vector2.zero);
            Place(container, Vector2.zero, Vector2.one);
            container.SetSiblingIndex(artwork.transform.GetSiblingIndex());
            controllerVisibility = container.gameObject.AddComponent<CanvasGroup>();
            controllerVisibility.alpha = initialSprite != null ? 1f : 0f;

            RectTransform imageRect = Rect("Artwork", container, Vector2.zero);
            Place(imageRect, Vector2.zero, Vector2.one);
            controllerArtwork = imageRect.gameObject.AddComponent<Image>();
            controllerArtwork.preserveAspect = true;
            controllerArtwork.raycastTarget = false;
            controllerArtwork.sprite = initialSprite;
            // 看板の遮蔽マテリアルが既に設定されている場合は、新しい画像にも引き継ぐ。
            controllerArtwork.material = artwork.material;

            // SpriteRendererに付けたスプライトキーも、既存のClipと同様にCanvasのImageに表示する。
            controllerSpriteSource = imageRect.gameObject.AddComponent<SpriteRenderer>();
            controllerSpriteSource.forceRenderingOff = true;
        }

        // ゲームが停止中でも進む時刻で、ページに設定されたアニメーションを再生する。
        internal void Tick(float clock)
        {
            if (!imageOnly)
            {
                legacyView.Tick(clock);
                return;
            }

            if (pageController != null)
            {
                TickController(clock);
                return;
            }

            if (clip != null)
            {
                float time = clip.isLooping && clip.length > 0f
                    ? Mathf.Repeat(clock, clip.length)
                    : Mathf.Min(clock, clip.length);

                if (clip.legacy)
                {
                    clip.SampleAnimation(artwork.gameObject, time);
                }
                else
                {
                    EnsureClipGraph();
                    clipPlayable.SetTime(time);
                    clipGraph.Evaluate(0f);
                }
                Sprite sampledSprite = clipSpriteSource.sprite;
                if (sampledSprite != null && artwork.sprite != sampledSprite)
                {
                    artwork.sprite = sampledSprite;
                }
            }
            else if (frames.Length > 0)
            {
                float progress = Mathf.Repeat(clock, cycleSeconds) / cycleSeconds;
                int frameIndex = Mathf.Min((int)(progress * frames.Length), frames.Length - 1);
                Sprite frame = frames[frameIndex];
                if (artwork.sprite != frame) artwork.sprite = frame;
            }

            artwork.enabled = artwork.overrideSprite != null || artwork.sprite != null;
        }

        // Controllerを現在のページのAnimatorに接続し、ページの経過時間の差分で遷移を進める。
        private void TickController(float clock)
        {
            if (!clipGraph.IsValid())
            {
                Animator animator = controllerArtwork.gameObject.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.fireEvents = false;

                clipGraph = PlayableGraph.Create("Tutorial Artwork Controller");
                clipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimatorControllerPlayable controller = AnimatorControllerPlayable.Create(clipGraph, pageController);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(clipGraph, "Tutorial Artwork", animator);
                output.SetSourcePlayable(controller);
                clipGraph.Play();
            }

            float deltaTime = Mathf.Max(0f, clock - controllerClock);
            controllerClock = clock;
            clipGraph.Evaluate(deltaTime);

            Sprite sampledSprite = controllerSpriteSource.sprite;
            if (sampledSprite != null)
            {
                if (controllerArtwork.sprite != sampledSprite) controllerArtwork.sprite = sampledSprite;
            }
            else if (lastControllerSprite != null && controllerArtwork.sprite == lastControllerSprite)
            {
                controllerArtwork.sprite = null;
            }
            lastControllerSprite = sampledSprite;

            // 画像なしの空白フレームを透明にし、Controllerが設定したImageの有効状態や色を保つ。
            controllerVisibility.alpha = controllerArtwork.overrideSprite != null || controllerArtwork.sprite != null
                ? 1f : 0f;
        }

        // Animatorへクリップを接続し、Presenterが供給する時刻だけで再生する。
        private void EnsureClipGraph()
        {
            if (clipGraph.IsValid()) return;

            if (clipAnimator == null)
            {
                clipAnimator = artwork.gameObject.AddComponent<Animator>();
                clipAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                clipAnimator.fireEvents = false;
            }

            clipGraph = PlayableGraph.Create("Tutorial Artwork Clip");
            clipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(clipGraph, clip);
            clipPlayable.SetSpeed(0);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                clipGraph, "Tutorial Artwork", clipAnimator);
            output.SetSourcePlayable(clipPlayable);
            clipGraph.Play();
        }

        // ページ切り替えや終了時に、再生グラフとController専用の画像を解放する。
        internal void ReleaseClip()
        {
            if (clipGraph.IsValid()) clipGraph.Destroy();
            clipGraph = default;
            clipPlayable = default;
            clip = null;
            pageController = null;
            controllerClock = 0f;
            lastControllerSprite = null;

            if (controllerVisibility != null)
            {
                controllerVisibility.gameObject.SetActive(false);
                Object.Destroy(controllerVisibility.gameObject);
                artwork.gameObject.SetActive(true);
            }

            controllerVisibility = null;
            controllerArtwork = null;
            controllerSpriteSource = null;
        }

        // 両方の表示方式のボタンを共通のページ操作につなぐ。
        internal void BindButtons(UnityAction onPrevious, UnityAction onNext, UnityAction onClose)
        {
            previous.onClick.AddListener(onPrevious);
            next.onClick.AddListener(onNext);
            close.onClick.AddListener(onClose);
            legacyView.previous.onClick.AddListener(onPrevious);
            legacyView.next.onClick.AddListener(onNext);
            legacyView.close.onClick.AddListener(onClose);
        }

        // 従来の看板・スライドに案内文を設定する。
        internal void SetFooter(string value)
        {
            if (!imageOnly) legacyView.footer.text = value;
        }

        // 選択中の表示方式に合わせて操作ボタンを更新する。
        internal void SetButtons(TutorialDefinition definition, int index)
        {
            if (!imageOnly)
            {
                legacyView.previous.interactable = index > 0;
                legacyView.next.interactable = index < definition.pages.Length - 1;
                return;
            }

            SetButton(previous, definition.previousPageSprite, index > 0);
            SetButton(next, definition.nextPageSprite, index < definition.pages.Length - 1);
            SetButton(close, definition.closeSprite, true);
        }

        // ボタン画像とクリックの可否を設定する。
        private static void SetButton(Button button, Sprite sprite, bool interactable)
        {
            button.image.sprite = sprite;
            button.interactable = interactable;
            button.gameObject.SetActive(sprite != null);
        }

        // 指定した親の下にUIの配置用RectTransformを作る。
        internal static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            return rect;
        }

        // 親の幅・高さに対する割合で、UIを配置する。
        internal static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 従来の表示方式で使うテキストを作る。
        internal static Text Label(Transform parent, Font font, string name, int size, Color color)
        {
            var label = Rect(name, parent, Vector2.zero).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        // 文字を生成せず、割り当てたスプライトで操作できるボタンを作る。
        private static Button MakeButton(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = Rect(name, parent, size);
            rect.anchoredPosition = position;
            Image image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            return button;
        }
    }

    // Screen Space Overlay上のUI。RenderTextureや独自ピクセル描画を経由しない。
    internal sealed class TutorialLegacyView
    {
        internal RectTransform root;
        internal Text footer;
        private Text title, description;
        internal Button previous, next, close;
        private Text controlLabel;
        private Image control, artwork;
        private RectTransform knob, actor, blade;
        private GameObject wall, ceiling, platform;
        private Text demoCaption;
        private Image actorImage;
        private GameObject schematic;
        private readonly Color cyan = new Color(0.12f, 0.9f, 1f);
        private readonly Color pale = new Color(0.75f, 0.95f, 1f);
        private TutorialPage page;
        private TutorialCombatDemo combatDemo;

        // 旧来のカードと操作デモを配置する。
        internal TutorialLegacyView(Transform parent, Font font, bool modal)
        {
            root = TutorialView.Rect("Tutorial Card", parent, new Vector2(modal ? 960 : 560, modal ? 600 : 350));
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.015f, 0.075f, 0.13f, 0.96f);
            bg.raycastTarget = modal;
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = cyan; outline.effectDistance = new Vector2(2, -2);
            title = TutorialView.Label(root, font, "Title", 30, pale);
            TutorialView.Place(title.rectTransform, new Vector2(.06f, .78f), new Vector2(.94f, .96f));
            description = TutorialView.Label(root, font, "Description", 23, Color.white);
            TutorialView.Place(description.rectTransform, new Vector2(.06f, .12f), new Vector2(.94f, .34f));
            description.alignment = TextAnchor.UpperLeft;
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = 14; description.resizeTextMaxSize = 23;
            control = TutorialView.Rect("Control", root, new Vector2(95, 95)).gameObject.AddComponent<Image>();
            control.color = new Color(.04f, .25f, .35f);
            control.rectTransform.anchorMin = control.rectTransform.anchorMax = new Vector2(.18f, .56f);
            controlLabel = TutorialView.Label(control.transform, font, "Input", 30, Color.white);
            TutorialView.Place(controlLabel.rectTransform, Vector2.zero, Vector2.one);
            knob = TutorialView.Rect("Stick", control.transform, new Vector2(18, 18));
            knob.gameObject.AddComponent<Image>().color = cyan;
            knob.anchoredPosition = new Vector2(0, -34);
            var demo = TutorialView.Rect("Demo", root, Vector2.zero);
            TutorialView.Place(demo, new Vector2(.35f, .37f), new Vector2(.94f, .77f));
            var demoBG = demo.gameObject.AddComponent<Image>();
            demoBG.color = new Color(.025f, .12f, .18f); demoBG.raycastTarget = false;
            schematic = TutorialView.Rect("Schematic", demo, Vector2.zero).gameObject;
            TutorialView.Place((RectTransform)schematic.transform, Vector2.zero, Vector2.one);
            var floor = TutorialView.Rect("Ground", schematic.transform, Vector2.zero).gameObject.AddComponent<Image>();
            TutorialView.Place(floor.rectTransform, new Vector2(.03f, .07f), new Vector2(.97f, .09f));
            floor.color = cyan;
            actor = TutorialView.Rect("Demo Character", schematic.transform, new Vector2(26, 44));
            actor.anchorMin = actor.anchorMax = new Vector2(.5f, .09f);
            actor.pivot = new Vector2(.5f, 0);
            actorImage = actor.gameObject.AddComponent<Image>(); actorImage.color = pale;
            var head = TutorialView.Rect("Head", actor, new Vector2(20, 20));
            head.anchoredPosition = new Vector2(0, 34);
            head.gameObject.AddComponent<Image>().color = pale;
            blade = TutorialView.Rect("Chainsaw", actor, new Vector2(42, 7));
            blade.pivot = new Vector2(0, .5f); blade.anchoredPosition = new Vector2(8, 20);
            blade.gameObject.AddComponent<Image>().color = new Color(1f, .65f, .15f);
            wall = Obstacle(schematic.transform, "Wall", new Vector2(.82f, .07f), new Vector2(.88f, .92f));
            ceiling = Obstacle(schematic.transform, "Ceiling", new Vector2(.4f, .85f), new Vector2(.97f, .94f));
            platform = Obstacle(schematic.transform, "Platform", new Vector2(.03f, .4f), new Vector2(.48f, .45f));
            demoCaption = TutorialView.Label(demo, font, "Demo Stage", 13, pale);
            TutorialView.Place(demoCaption.rectTransform, new Vector2(.02f, .87f), new Vector2(.78f, 1f));
            combatDemo = new TutorialCombatDemo(demo, font);
            artwork = TutorialView.Rect("Artwork", demo, Vector2.zero).gameObject.AddComponent<Image>();
            TutorialView.Place(artwork.rectTransform, Vector2.zero, Vector2.one);
            artwork.preserveAspect = true; artwork.raycastTarget = false;
            footer = TutorialView.Label(root, font, "Footer", 17, pale);
            TutorialView.Place(footer.rectTransform, new Vector2(.08f, .015f), new Vector2(.92f, .10f));
            if (modal)
            {
                previous = MakeButton(root, font, "◀", new Vector2(-430, 0), new Vector2(62, 72));
                next = MakeButton(root, font, "▶", new Vector2(430, 0), new Vector2(62, 72));
                close = MakeButton(root, font, "閉じる", new Vector2(382, 255), new Vector2(130, 42));
            }
            root.gameObject.SetActive(false);
        }

        // 既存のページ設定から説明文と入力デモを更新する。
        internal void SetPage(TutorialPage value)
        {
            page = value;
            combatDemo.root.gameObject.SetActive(false);
            title.text = value.title;
            description.text = value.description;
            bool dig = TutorialTimeline.IsDigDemo(value.demo);
            controlLabel.text = dig ? (value.demo == TutorialDemo.WallKick ? "A" : "ZL")
                : value.demo == TutorialDemo.Move ? "L" : value.buttonLabel;
            wall.SetActive(value.demo == TutorialDemo.WallDig || value.demo == TutorialDemo.WallKick);
            ceiling.SetActive(value.demo == TutorialDemo.CeilingDig);
            platform.SetActive(value.demo == TutorialDemo.WallKick);
            blade.gameObject.SetActive(dig);
            actor.localScale = Vector3.one * (dig ? .55f : 1f);
            actor.localRotation = Quaternion.identity;
            actorImage.color = pale;
            demoCaption.text = "";
            knob.gameObject.SetActive(value.demo == TutorialDemo.Move);
            bool hasArt = value.illustration != null || (value.animationFrames != null && value.animationFrames.Length > 0);
            artwork.gameObject.SetActive(hasArt);
            schematic.SetActive(!hasArt);
        }

        // 同じ周期で操作アイコンとデモを更新する。
        internal void Tick(float clock)
        {
            if (page == null) return;
            float phase = Mathf.Repeat(clock / Mathf.Max(.2f, page.cycleSeconds), 1f);
            TutorialTimeline.Evaluate(page, phase, out float x, out float y, out float stick, out bool pressed);
            bool alternate = page.demo == TutorialDemo.WallDig &&
                Mathf.FloorToInt(clock / Mathf.Max(.2f, page.cycleSeconds)) % 2 == 1;
            bool dig = TutorialTimeline.IsDigDemo(page.demo);
            if (dig)
            {
                TutorialTimeline.EvaluateDig(page, phase, alternate, out x, out y,
                    out float tilt, out float bladeAngle, out pressed, out bool attached);
                actor.anchorMin = actor.anchorMax = new Vector2(x, y);
                actor.anchoredPosition = Vector2.zero;
                actor.localRotation = Quaternion.Euler(0, 0, tilt);
                blade.localRotation = Quaternion.Euler(0, 0, bladeAngle);
                actorImage.color = attached ? cyan : pale;
                demoCaption.text = page.demo == TutorialDemo.WallDig
                    ? (alternate ? "ジャンプ → 壁食い込み" : "地上 → 壁食い込み")
                    : page.demo == TutorialDemo.WallKick ? "壁食い込み → 壁キック"
                    : "ZLを1回押して切替";
            }
            else
            {
                actor.anchorMin = actor.anchorMax = new Vector2(.5f, .09f);
                actor.anchoredPosition = new Vector2(x * 70f, y * 85f);
            }
            bool combat = TutorialCombatDemo.Supports(page.demo);
            if (combat)
            {
                int cycle = Mathf.FloorToInt(clock / Mathf.Max(.2f, page.cycleSeconds));
                combatDemo.Tick(page, phase, cycle, out pressed, out string label);
                controlLabel.text = label;
                demoCaption.text = "";
            }
            control.color = pressed ? cyan : new Color(.04f, .25f, .35f);
            controlLabel.color = pressed ? new Color(.01f, .08f, .12f) : Color.white;
            control.rectTransform.localScale = Vector3.one * (pressed ? .88f : 1f);
            knob.anchoredPosition = new Vector2(stick * 30f, -34);
            // キャラクター移動とスティック表示は同じ周期の位置・速度から計算。
            Sprite[] frames = alternate ? page.alternateAnimationFrames : page.animationFrames;
            bool hasFrames = frames != null && frames.Length > 0;
            bool hasArt = hasFrames || page.illustration != null;
            artwork.gameObject.SetActive(hasArt); schematic.SetActive(!hasArt && !combat);
            combatDemo.root.gameObject.SetActive(!hasArt && combat);
            if (hasFrames)
            {
                int i = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(phase * frames.Length));
                artwork.sprite = frames[i];
            }
            else artwork.sprite = page.illustration;
        }

        // デモの壁・床・天井を表す図形を作る。
        private GameObject Obstacle(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = TutorialView.Rect(name, parent, Vector2.zero);
            TutorialView.Place(rect, min, max);
            var image = rect.gameObject.AddComponent<Image>(); image.color = cyan; image.raycastTarget = false;
            return rect.gameObject;
        }

        // 従来のラベル付き操作ボタンを作る。
        private static Button MakeButton(Transform parent, Font font, string label, Vector2 pos, Vector2 size)
        {
            var rt = TutorialView.Rect(label, parent, size); rt.anchoredPosition = pos;
            var image = rt.gameObject.AddComponent<Image>(); image.color = new Color(.04f, .25f, .35f);
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            var text = TutorialView.Label(rt, font, label, 20, Color.white); text.text = label;
            TutorialView.Place(text.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }
    }
}
