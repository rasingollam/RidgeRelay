using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Screen = UnityEngine.Device.Screen;

namespace RidgeRelay
{
    [DefaultExecutionOrder(-10)]
    public sealed partial class RaceHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(15, 35, 50, 255);
        private static readonly Color Panel = new Color32(14, 31, 45, 228);
        private static readonly Color Paper = new Color32(255, 245, 217, 255);
        private static readonly Color Muted = new Color32(179, 207, 207, 255);
        private static readonly Color Mint = new Color32(119, 235, 194, 255);
        private static readonly Color Gold = new Color32(255, 190, 111, 255);
        private static readonly Color Coral = new Color32(255, 151, 122, 255);

        private static readonly string[] CountLabels = { string.Empty, "1", "2", "3" };

        private const string MotionKey = "RidgeRelay.ReducedMotion";
        private const string MirrorKey = "RidgeRelay.MirroredControls";

        private sealed class Control
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Action Invoke;
            public bool Enabled = true;
        }

        private struct Gesture
        {
            public Vector2 Origin;
            public bool Consumed;
        }

        private readonly List<Control> controls = new List<Control>();
        private readonly Dictionary<int, Gesture> gestures = new Dictionary<int, Gesture>(12);
        private RaceSimulation race;
        private RacePhase lastPhase;
        private RectTransform safe;
        private RectTransform modalSafe;
        private RectTransform playLayer;
        private RectTransform modalLayer;
        private RectTransform barFill;
        private Text place;
        private Text clock;
        private Text countdown;
        private Text notice;
        private Text modalTitle;
        private Text modalBody;
        private Control primary;
        private Control motion;
        private Control mirror;
        private Control jump;
        private Control item;
        private Font font;
        private Sprite rounded;
        private Texture2D roundedTexture;
        private Rect lastSafe;
        private Vector2Int lastSize;
        private bool mirrored;
        private bool jumpPending;
        private bool itemPending;
        private int lanePending;
        private int inputFrame = -1;
        private float bestTime;
        private int lastDecisecond = -1;
        private int lastPlace = -1;
        private bool pauseMenu;

        public event Action StartRequested;
        public event Action RetryRequested;
        public event Action MenuRequested;
        public event Action<bool> ReducedMotionChanged;
        public bool ReducedMotion { get; private set; }
        public bool IsPaused => pauseMenu && !onlineConnected;

        private void Awake()
        {
            ReducedMotion = PlayerPrefs.GetInt(MotionKey) != 0;
            mirrored = PlayerPrefs.GetInt(MirrorKey) != 0;
            Build();
            LayoutControls();
            RefreshMode();
        }

        private void OnDestroy()
        {
            StopCodeEntry();
            if (rounded != null) Destroy(rounded);
            if (roundedTexture != null) Destroy(roundedTexture);
        }

        public RaceCommand ReadCommand()
        {
            PollInput();
            int lane = Math.Sign(lanePending);
            RaceCommand command = new RaceCommand(lane, jumpPending, itemPending);
            lanePending -= lane;
            jumpPending = itemPending = false;
            return CanControlRace ? command : default;
        }

        public void Present(RaceSimulation nextRace, float localBestTime)
        {
            bool modeChanged = nextRace != race || (nextRace != null && nextRace.Phase != lastPhase);
            bool bestChanged = bestTime != localBestTime;
            bestTime = localBestTime;
            if (nextRace != race)
            {
                pauseMenu = false;
                ClearInput();
                lastDecisecond = lastPlace = -1;
            }
            race = nextRace;
            if (race != null) lastPhase = race.Phase;
            if (modeChanged || (bestChanged && modalLayer.gameObject.activeSelf)) RefreshMode();
            if (race != null && race.Phase != RacePhase.Finished) RefreshRace();
        }

        private void Update()
        {
            UpdateSafeArea();
            PollInput();
            UpdateCodeEntry();
        }

        private bool CanControlRace => race != null && !pauseMenu && race.Phase == RacePhase.Running &&
            race.Runners[PlayerSlot].FinishTime < 0 && (!onlineConnected || onlineCanControl);

        private void PollInput()
        {
            if (inputFrame == Time.frameCount) return;
            inputFrame = Time.frameCount;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (ShowPrivateMenu)
                    PollPrivateKeys(keyboard);
                else if (keyboard.escapeKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame)
                    TogglePause();
                if (!ShowPrivateMenu && modalLayer.gameObject.activeSelf && keyboard.enterKey.wasPressedThisFrame)
                    ActivatePrimary();
                else if (CanControlRace)
                {
                    if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                        Shift(-1);
                    if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                        Shift(1);
                    jumpPending |= keyboard.spaceKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame;
                    itemPending |= keyboard.eKey.wasPressedThisFrame;
                }
            }

            Touchscreen touchscreen = Touchscreen.current;
            bool touchActive = false;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    bool held = touch.press.isPressed;
                    bool pressed = touch.press.wasPressedThisFrame;
                    bool released = touch.press.wasReleasedThisFrame;
                    touchActive |= held || pressed || released;
                    PollPointer(i, touch.position.ReadValue(), held, pressed, released);
                }
            }
            if (touchActive)
            {
                gestures.Remove(-1);
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                PollPointer(-1, mouse.position.ReadValue(), mouse.leftButton.isPressed,
                    mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
            }
        }

        private void PollPointer(int id, Vector2 position, bool held, bool pressed, bool released)
        {
            if (pressed || (held && !gestures.ContainsKey(id)))
            {
                bool consumed = TryPressControl(position) || !CanControlRace;
                gestures[id] = new Gesture { Origin = position, Consumed = consumed };
            }
            if (!gestures.TryGetValue(id, out Gesture gesture)) return;
            if (CanControlRace && !gesture.Consumed && (held || released))
            {
                Vector2 offset = position - gesture.Origin;
                float threshold = Mathf.Max(16, Screen.height * 0.065f);
                if (Mathf.Abs(offset.x) >= threshold && Mathf.Abs(offset.x) > Mathf.Abs(offset.y) * 1.15f)
                {
                    Shift(Math.Sign(offset.x));
                    gesture.Consumed = true;
                    gestures[id] = gesture;
                }
            }
            if (!held || released) gestures.Remove(id);
        }

        private bool TryPressControl(Vector2 position)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                Control control = controls[i];
                if (!control.Rect.gameObject.activeInHierarchy ||
                    !RectTransformUtility.RectangleContainsScreenPoint(control.Rect, position)) continue;
                if (control.Enabled) control.Invoke();
                return true;
            }
            return false;
        }

        private void Shift(int direction) => lanePending = Mathf.Clamp(lanePending + direction, -2, 2);

        private void ClearInput()
        {
            lanePending = 0;
            jumpPending = itemPending = false;
            gestures.Clear();
        }

        private void TogglePause()
        {
            if (race == null || race.Phase == RacePhase.Finished) return;
            pauseMenu = !pauseMenu;
            lanePending = 0;
            jumpPending = itemPending = false;
            RefreshMode();
        }

        private void ActivatePrimary()
        {
            if (pauseMenu) TogglePause();
            else if (race == null) StartRequested?.Invoke();
            else if (race.Phase == RacePhase.Finished)
            {
                if (onlineConnected) RequestLeave();
                else RetryRequested?.Invoke();
            }
        }

        private void ToggleMotion()
        {
            ReducedMotion = !ReducedMotion;
            PlayerPrefs.SetInt(MotionKey, ReducedMotion ? 1 : 0);
            PlayerPrefs.Save();
            RefreshMode();
            ReducedMotionChanged?.Invoke(ReducedMotion);
        }

        private void ToggleMirror()
        {
            mirrored = !mirrored;
            PlayerPrefs.SetInt(MirrorKey, mirrored ? 1 : 0);
            PlayerPrefs.Save();
            LayoutControls();
            RefreshMode();
        }

        private void RefreshMode()
        {
            bool menu = race == null;
            bool finished = !menu && race.Phase == RacePhase.Finished;
            bool modal = menu || finished || pauseMenu;
            playLayer.gameObject.SetActive(!modal);
            modalLayer.gameObject.SetActive(modal);
            if (!modal) return;

            RefreshPrivateControls();
            if (ShowPrivateMenu) return;

            string record = bestTime > 0 ? "BEST  " + FormatTime(bestTime) : "WINDMILL GORGE";
            RunnerState runner = race?.Runners[PlayerSlot];
            bool dnf = finished && runner.FinishTime < 0;
            modalTitle.text = menu ? "RIDGE RELAY" : finished ? (dnf ? "DNF" : Ordinal(runner.Place) + " PLACE") : "PAUSED";
            if (menu)
                modalBody.text = record + "\nSwipe to change lanes. Tap JUMP or use your held gadget.\nKeyboard: A / D, Space, E";
            else if (finished)
            {
                string finish = dnf ? "TIME LIMIT REACHED" : "TIME  " + FormatTime(runner.FinishTime);
                modalBody.text = finish + "\n" + (onlineConnected ? "PRIVATE RACE" : record);
            }
            else
                modalBody.text = onlineConnected
                    ? "PRIVATE RACE\nThe race continues while this menu is open."
                    : "WINDMILL GORGE\nKeyboard: A / D, Space, E";
            primary.Label.text = menu ? "RACE" : finished ? (onlineConnected ? "LEAVE SESSION" : "RACE AGAIN") : "RESUME";
            motion.Label.text = ReducedMotion ? "MOTION: REDUCED" : "MOTION: STANDARD";
            mirror.Label.text = mirrored ? "BUTTONS: MIRRORED" : "BUTTONS: STANDARD";
        }

        private void RefreshRace()
        {
            RunnerState runner = race.Runners[PlayerSlot];
            if (runner.Place != lastPlace)
            {
                lastPlace = runner.Place;
                place.text = Ordinal(lastPlace) + "  /  4";
            }
            float displayedTime = runner.FinishTime >= 0 ? runner.FinishTime : race.Clock;
            int decisecond = Mathf.Max(0, Mathf.FloorToInt(displayedTime * 10));
            if (lastDecisecond != decisecond)
            {
                lastDecisecond = decisecond;
                clock.text = $"{decisecond / 600:00}:{decisecond / 10 % 60:00}.{decisecond % 10}";
            }

            float progress = Mathf.Clamp01(runner.Distance / RaceSimulation.CourseLength);
            barFill.anchorMax = new Vector2(progress, 1);
            countdown.text = race.Phase == RacePhase.Countdown
                ? CountLabels[Mathf.Clamp(Mathf.CeilToInt(-race.Clock), 1, 3)]
                : race.Clock < 0.65f ? "GO!" : string.Empty;

            string gadget = GadgetName(runner.Held);
            if (item.Label.text != gadget) item.Label.text = gadget;
            item.Background.color = runner.Held == Gadget.None ? Panel : Gold;
            item.Label.color = runner.Held == Gadget.None ? Muted : Ink;

            if (runner.FinishTime >= 0) SetNotice("FINISHED / WAITING FOR RACERS", Mint);
            else if (runner.Recovery > 0) SetNotice("BACK ON COURSE", Gold);
            else if (TryFindTrap(runner, out int warningLane))
                SetNotice(warningLane == 0 ? "▲  BRISTLES AHEAD" : warningLane < 0 ? "◀  BRISTLES LEFT" : "BRISTLES RIGHT  ▶", Coral);
            else if (runner.Slow > 0) SetNotice("SLOWED", Coral);
            else if (runner.Shield > 0) SetNotice("SHIELD ACTIVE", Mint);
            else if (runner.Boost > 0) SetNotice("TAILWIND ACTIVE", Mint);
            else SetNotice(string.Empty, Muted);
        }

        private bool TryFindTrap(RunnerState runner, out int laneDelta)
        {
            float nearest = 13;
            laneDelta = 0;
            bool found = false;
            IReadOnlyList<RaceSimulation.Trap> traps = race.Traps;
            for (int i = 0; i < traps.Count; i++)
            {
                RaceSimulation.Trap trap = traps[i];
                float ahead = trap.Distance - runner.Distance;
                if (trap.Owner == PlayerSlot || ahead <= 0 || ahead >= nearest) continue;
                nearest = ahead;
                laneDelta = trap.Lane - runner.Lane;
                found = true;
            }
            return found;
        }

        private void SetNotice(string value, Color color)
        {
            if (notice.text != value) notice.text = value;
            notice.color = color;
        }

        private void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateRoundedSprite();
            GameObject canvasObject = new GameObject("Race Overlay", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1152, 648);
            scaler.matchWidthOrHeight = 1;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            safe = Fill(canvasRect, "Safe Area");
            playLayer = Fill(safe, "Race HUD");
            modalLayer = Fill(canvasRect, "Menu Overlay");
            Image veil = modalLayer.gameObject.AddComponent<Image>();
            veil.color = new Color32(8, 22, 33, 155);
            veil.raycastTarget = false;
            modalSafe = Fill(modalLayer, "Safe Menu Area");

            RectTransform rankCard = Spot(playLayer, "Rank", new Vector2(0, 1),
                new Vector2(107, -50), new Vector2(174, 60));
            AddPanel(rankCard, Panel);
            place = AddText(Fill(rankCard, "Place"), string.Empty, 27, Paper, FontStyle.Bold);

            RectTransform timerCard = Spot(playLayer, "Timer", new Vector2(0.5f, 1),
                new Vector2(0, -44), new Vector2(230, 46));
            AddPanel(timerCard, Panel);
            clock = AddText(Fill(timerCard, "Time"), "00:00.0", 25, Paper, FontStyle.Bold);
            RectTransform progress = Spot(playLayer, "Course Progress", new Vector2(0.5f, 1),
                new Vector2(0, -78), new Vector2(230, 8));
            AddPanel(progress, Panel);
            barFill = Fill(progress, "Progress Fill");
            AddPanel(barFill, Mint);

            AddControl(playLayer, "Pause", "PAUSE", new Vector2(1, 1),
                new Vector2(-69, -50), new Vector2(98, 56), Panel, Paper, 18, TogglePause);
            countdown = AddText(Spot(playLayer, "Countdown", new Vector2(0.5f, 0.57f),
                Vector2.zero, new Vector2(320, 142)), string.Empty, 94, Paper, FontStyle.Bold);
            AddOutline(countdown, Ink, new Vector2(3, -3));
            notice = AddText(Spot(playLayer, "Status", new Vector2(0.5f, 0.14f),
                Vector2.zero, new Vector2(368, 42)), string.Empty, 23, Mint, FontStyle.Bold);
            AddOutline(notice, Ink, new Vector2(2, -2));
            jump = AddControl(playLayer, "Jump", "JUMP", Vector2.zero, new Vector2(102, 65),
                new Vector2(164, 88), Panel, Paper, 26, () => { if (CanControlRace) jumpPending = true; });
            item = AddControl(playLayer, "Gadget", "GADGET", Vector2.right, new Vector2(-102, 65),
                new Vector2(164, 88), Panel, Muted, 21, () => { if (CanControlRace) itemPending = true; });

            RectTransform card = Spot(modalSafe, "Menu Card", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(586, 366));
            AddPanel(card, Panel);
            modalTitle = AddText(Spot(card, "Title", new Vector2(0.5f, 1),
                new Vector2(0, -65), new Vector2(530, 70)), string.Empty, 54, Paper, FontStyle.Bold);
            modalBody = AddText(Spot(card, "Details", new Vector2(0.5f, 1),
                new Vector2(0, -151), new Vector2(530, 92)), string.Empty, 23, Muted, FontStyle.Normal);
            primary = AddControl(card, "Primary", "RACE", new Vector2(0.5f, 0),
                new Vector2(0, 119), new Vector2(268, 66), Mint, Ink, 28, ActivatePrimary);
            Color toggleColor = new Color32(38, 66, 79, 255);
            motion = AddControl(card, "Motion", string.Empty, new Vector2(0.5f, 0),
                new Vector2(-130, 45), new Vector2(245, 48), toggleColor, Paper, 15, ToggleMotion);
            mirror = AddControl(card, "Mirror", string.Empty, new Vector2(0.5f, 0),
                new Vector2(130, 45), new Vector2(245, 48), toggleColor, Paper, 15, ToggleMirror);
            BuildPrivateControls(card, toggleColor);
            UpdateSafeArea();
        }

        private void UpdateSafeArea()
        {
            Rect area = Screen.safeArea;
            Vector2Int size = new Vector2Int(Screen.width, Screen.height);
            if (size.x == 0 || size.y == 0 || (area == lastSafe && size == lastSize)) return;
            lastSafe = area;
            lastSize = size;
            Vector2 low = new Vector2(area.xMin / size.x, area.yMin / size.y);
            Vector2 high = new Vector2(area.xMax / size.x, area.yMax / size.y);
            safe.anchorMin = modalSafe.anchorMin = low;
            safe.anchorMax = modalSafe.anchorMax = high;
        }

        private void LayoutControls()
        {
            MoveControl(jump, mirrored);
            MoveControl(item, !mirrored);
        }

        private static void MoveControl(Control control, bool onRight)
        {
            Vector2 anchor = onRight ? Vector2.right : Vector2.zero;
            control.Rect.anchorMin = control.Rect.anchorMax = anchor;
            control.Rect.anchoredPosition = new Vector2(onRight ? -102 : 102, 65);
        }

        private Control AddControl(RectTransform parent, string name, string label, Vector2 anchor,
            Vector2 position, Vector2 size, Color background, Color foreground, int fontSize, Action action)
        {
            RectTransform rect = Spot(parent, name, anchor, position, size);
            var control = new Control
            {
                Rect = rect,
                Background = AddPanel(rect, background),
                Label = AddText(Fill(rect, "Label"), label, fontSize, foreground, FontStyle.Bold),
                Invoke = action
            };
            controls.Add(control);
            return control;
        }

        private Image AddPanel(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = rounded;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text AddText(RectTransform rect, string value, int fontSize, Color color, FontStyle style)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.fontStyle = style;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        private static void AddOutline(Text text, Color color, Vector2 distance)
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
        }

        private void CreateRoundedSprite()
        {
            const int size = 32;
            const int radius = 10;
            roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - size / 2f) - size / 2f + radius);
                float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - size / 2f) - size / 2f + radius);
                byte alpha = (byte)(255 * Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            roundedTexture.SetPixels32(pixels);
            roundedTexture.Apply(false, true);
            rounded = Sprite.Create(roundedTexture, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        private static RectTransform Fill(RectTransform parent, string name)
        {
            RectTransform rect = Spot(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform Spot(RectTransform parent, string name, Vector2 anchor,
            Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static string GadgetName(Gadget gadget)
        {
            switch (gadget)
            {
                case Gadget.Tailwind: return "TAILWIND";
                case Gadget.BristlePad: return "BRISTLE PAD";
                case Gadget.ShellFilm: return "SHELL FILM";
                default: return "GADGET";
            }
        }

        private static string Ordinal(int rank)
        {
            switch (rank)
            {
                case 1: return "1ST";
                case 2: return "2ND";
                case 3: return "3RD";
                default: return "4TH";
            }
        }

        private static string FormatTime(float seconds)
        {
            int centiseconds = Mathf.Max(0, Mathf.FloorToInt(seconds * 100));
            return $"{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}";
        }
    }
}
