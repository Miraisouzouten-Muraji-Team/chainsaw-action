using UnityEngine;
using UnityEngine.UI;

namespace Project.Tutorial
{
    // Screen Space Overlay上のUI。RenderTextureや独自ピクセル描画を経由しない。
    internal sealed class TutorialView
    {
        internal RectTransform root;
        internal CanvasGroup group;
        internal Text title, description, footer;
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

        internal TutorialView(Transform parent, Font font, bool modal)
        {
            root = Rect("Tutorial Card", parent, new Vector2(modal ? 960 : 560, modal ? 600 : 350));
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0.015f, 0.075f, 0.13f, 0.96f);
            bg.raycastTarget = modal;
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = cyan; outline.effectDistance = new Vector2(2, -2);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = modal;
            title = Label(root, font, "Title", 30, pale);
            Place(title.rectTransform, new Vector2(.06f, .78f), new Vector2(.94f, .96f));
            description = Label(root, font, "Description", 23, Color.white);
            Place(description.rectTransform, new Vector2(.06f, .12f), new Vector2(.94f, .34f));
            description.alignment = TextAnchor.UpperLeft;
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = 14; description.resizeTextMaxSize = 23;
            control = Rect("Control", root, new Vector2(95, 95)).gameObject.AddComponent<Image>();
            control.color = new Color(.04f, .25f, .35f);
            control.rectTransform.anchorMin = control.rectTransform.anchorMax = new Vector2(.18f, .56f);
            controlLabel = Label(control.transform, font, "Input", 30, Color.white);
            Place(controlLabel.rectTransform, Vector2.zero, Vector2.one);
            knob = Rect("Stick", control.transform, new Vector2(18, 18));
            knob.gameObject.AddComponent<Image>().color = cyan;
            knob.anchoredPosition = new Vector2(0, -34);
            var demo = Rect("Demo", root, Vector2.zero);
            Place(demo, new Vector2(.35f, .37f), new Vector2(.94f, .77f));
            var demoBG = demo.gameObject.AddComponent<Image>();
            demoBG.color = new Color(.025f, .12f, .18f); demoBG.raycastTarget = false;
            schematic = Rect("Schematic", demo, Vector2.zero).gameObject;
            Place((RectTransform)schematic.transform, Vector2.zero, Vector2.one);
            var floor = Rect("Ground", schematic.transform, Vector2.zero).gameObject.AddComponent<Image>();
            Place(floor.rectTransform, new Vector2(.03f, .07f), new Vector2(.97f, .09f));
            floor.color = cyan;
            actor = Rect("Demo Character", schematic.transform, new Vector2(26, 44));
            actor.anchorMin = actor.anchorMax = new Vector2(.5f, .09f);
            actor.pivot = new Vector2(.5f, 0);
            actorImage = actor.gameObject.AddComponent<Image>(); actorImage.color = pale;
            var head = Rect("Head", actor, new Vector2(20, 20));
            head.anchoredPosition = new Vector2(0, 34);
            head.gameObject.AddComponent<Image>().color = pale;
            blade = Rect("Chainsaw", actor, new Vector2(42, 7));
            blade.pivot = new Vector2(0, .5f); blade.anchoredPosition = new Vector2(8, 20);
            blade.gameObject.AddComponent<Image>().color = new Color(1f, .65f, .15f);
            wall = Obstacle(schematic.transform, "Wall", new Vector2(.82f, .07f), new Vector2(.88f, .92f));
            ceiling = Obstacle(schematic.transform, "Ceiling", new Vector2(.4f, .85f), new Vector2(.97f, .94f));
            platform = Obstacle(schematic.transform, "Platform", new Vector2(.03f, .4f), new Vector2(.48f, .45f));
            demoCaption = Label(demo, font, "Demo Stage", 13, pale);
            Place(demoCaption.rectTransform, new Vector2(.02f, .87f), new Vector2(.78f, 1f));
            combatDemo = new TutorialCombatDemo(demo, font);
            artwork = Rect("Artwork", demo, Vector2.zero).gameObject.AddComponent<Image>();
            Place(artwork.rectTransform, Vector2.zero, Vector2.one);
            artwork.preserveAspect = true; artwork.raycastTarget = false;
            footer = Label(root, font, "Footer", 17, pale);
            Place(footer.rectTransform, new Vector2(.08f, .015f), new Vector2(.92f, .10f));
            if (modal)
            {
                previous = MakeButton(root, font, "◀", new Vector2(-430, 0), new Vector2(62, 72));
                next = MakeButton(root, font, "▶", new Vector2(430, 0), new Vector2(62, 72));
                close = MakeButton(root, font, "閉じる", new Vector2(382, 255), new Vector2(130, 42));
            }
            root.gameObject.SetActive(false);
        }

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

        private GameObject Obstacle(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, Vector2.zero);
            Place(rect, min, max);
            var image = rect.gameObject.AddComponent<Image>(); image.color = cyan; image.raycastTarget = false;
            return rect.gameObject;
        }

        internal static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); rt.sizeDelta = size;
            return rt;
        }
        internal static void Place(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        internal static Text Label(Transform parent, Font font, string name, int size, Color color)
        {
            var text = Rect(name, parent, Vector2.zero).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return text;
        }
        private static Button MakeButton(Transform parent, Font font, string label, Vector2 pos, Vector2 size)
        {
            var rt = Rect(label, parent, size); rt.anchoredPosition = pos;
            var image = rt.gameObject.AddComponent<Image>(); image.color = new Color(.04f, .25f, .35f);
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            var text = Label(rt, font, label, 20, Color.white); text.text = label;
            Place(text.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }
    }
}
