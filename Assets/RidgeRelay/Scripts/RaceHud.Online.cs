using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RidgeRelay
{
    public sealed partial class RaceHud
    {
        private const int CodeLengthLimit = 12;
        private RectTransform privateActions;
        private Control onlineEntry;
        private Control leavePause;
        private Control returnToTitle;
        private Control codeEntry;
        private Control join;
        private Control host;
        private Control back;
        private Control startOnline;
        private Control copyCode;
        private Control leaveLobby;
        private string typedCode = string.Empty;
        private string onlineCode = string.Empty;
        private string onlineError = string.Empty;
        private int onlineHumans;
        private int localSlot;
        private bool showOnline;
        private bool onlineHost;
        private bool onlineConnected;
        private bool onlineBusy;
        private bool onlineCanStart;
        private bool onlineCanControl;
        private bool editingCode;
        private bool copied;
        private bool hideOnlineError;
        private Keyboard codeKeyboard;
        private TouchScreenKeyboard phoneKeyboard;

        public event Action HostRequested;
        public event Action<string> JoinRequested;
        public event Action LeaveRequested;
        public event Action BeginOnlineRequested;

        private bool ShowPrivateMenu => race == null && (showOnline || onlineConnected || onlineBusy);
        private int PlayerSlot => Mathf.Clamp(localSlot, 0, RaceSimulation.Entrants - 1);

        public void SetOnlineStatus(string joinCode, int humanCount, bool isHost, bool connected,
            bool busy, string error, int localSlot, bool canStart = true, bool canControl = true)
        {
            joinCode = joinCode ?? string.Empty;
            error = error ?? string.Empty;
            if (!connected) localSlot = 0;
            if (onlineCode == joinCode && onlineHumans == humanCount && onlineHost == isHost &&
                onlineConnected == connected && onlineBusy == busy && onlineError == error &&
                this.localSlot == localSlot && onlineCanStart == canStart && onlineCanControl == canControl)
                return;

            showOnline |= connected || busy;
            if (onlineCode != joinCode) copied = false;
            if (onlineError != error) hideOnlineError = false;
            if (this.localSlot != localSlot)
            {
                lastPlace = lastDecisecond = -1;
                ClearInput();
            }

            onlineCode = joinCode;
            onlineHumans = humanCount;
            onlineHost = isHost;
            onlineConnected = connected;
            onlineBusy = busy;
            onlineError = error;
            this.localSlot = localSlot;
            onlineCanStart = canStart;
            onlineCanControl = canControl;
            if (connected || busy) StopCodeEntry();
            RefreshMode();
        }

        private void BuildPrivateControls(RectTransform card, Color secondary)
        {
            var bottomCenter = new Vector2(0.5f, 0);
            var smallSize = new Vector2(206, 54);
            var actionSize = new Vector2(206, 66);
            onlineEntry = AddControl(card, "Private Races", "PRIVATE RACE", bottomCenter,
                new Vector2(114, 119), actionSize, secondary, Paper, 22, OpenPrivateMenu);
            leavePause = AddControl(card, "Leave Race", "LEAVE SESSION", bottomCenter,
                new Vector2(114, 119), actionSize, secondary, Coral, 19, RequestLeave);
            returnToTitle = AddControl(card, "Return To Title", "MAIN MENU", bottomCenter,
                new Vector2(114, 119), actionSize, secondary, Paper, 20, () => MenuRequested?.Invoke());

            privateActions = Fill(card, "Private Race Actions");
            codeEntry = AddControl(privateActions, "Code Entry", "ENTER JOIN CODE", bottomCenter,
                new Vector2(-90, 123), new Vector2(252, 56), secondary, Paper, 22, EditCode);
            join = AddControl(privateActions, "Join", "JOIN", bottomCenter,
                new Vector2(138, 123), new Vector2(156, 56), Mint, Ink, 24, RequestJoin);
            host = AddControl(privateActions, "Host", "HOST RACE", bottomCenter,
                new Vector2(-114, 53), smallSize, secondary, Paper, 21, RequestHost);
            back = AddControl(privateActions, "Back", "BACK", bottomCenter,
                new Vector2(114, 53), smallSize, secondary, Paper, 21, ClosePrivateMenu);
            startOnline = AddControl(privateActions, "Start Online", "START RACE", bottomCenter,
                new Vector2(0, 119), new Vector2(268, 66), Mint, Ink, 25, RequestStartOnline);
            copyCode = AddControl(privateActions, "Copy Code", "COPY CODE", bottomCenter,
                new Vector2(-114, 53), smallSize, secondary, Paper, 20, CopyCode);
            leaveLobby = AddControl(privateActions, "Leave Lobby", "LEAVE SESSION", bottomCenter,
                new Vector2(114, 53), smallSize, secondary, Coral, 18, RequestLeave);
        }

        private void RefreshPrivateControls()
        {
            bool privateMenu = ShowPrivateMenu;
            bool offlineHome = race == null && !privateMenu;
            bool onlinePause = race != null && pauseMenu && onlineConnected;
            bool onlineResults = race != null && race.Phase == RacePhase.Finished && onlineConnected;
            bool canReturnToTitle = !onlineConnected && race != null &&
                (pauseMenu || race.Phase == RacePhase.Finished);
            privateActions.gameObject.SetActive(privateMenu);
            SetVisible(onlineEntry, offlineHome);
            SetVisible(leavePause, onlinePause);
            SetVisible(returnToTitle, canReturnToTitle);
            SetVisible(primary, !privateMenu);
            SetVisible(motion, !privateMenu);
            SetVisible(mirror, !privateMenu);
            SetActionEnabled(primary, !onlineResults || !onlineBusy, Mint, Ink);
            SetActionEnabled(leavePause, !onlineBusy, Panel, Coral);

            bool twoActions = offlineHome || onlinePause || canReturnToTitle;
            primary.Rect.anchoredPosition = new Vector2(twoActions ? -114 : 0, 119);
            primary.Rect.sizeDelta = new Vector2(twoActions ? 206 : 268, 66);
            if (!privateMenu) return;

            modalTitle.text = "PRIVATE RACE";
            bool canEdit = !onlineBusy && !onlineConnected;
            SetVisible(codeEntry, !onlineConnected);
            SetVisible(join, !onlineConnected);
            SetVisible(host, !onlineConnected);
            SetVisible(back, !onlineConnected);
            SetVisible(startOnline, onlineConnected && onlineHost);
            SetVisible(copyCode, onlineConnected);
            SetVisible(leaveLobby, onlineConnected);
            codeEntry.Label.text = CodeEntryLabel();
            copyCode.Label.text = copied ? "CODE COPIED" : "COPY CODE";
            SetActionEnabled(codeEntry, canEdit, editingCode ? Gold : Panel, editingCode ? Ink : Paper);
            SetActionEnabled(join, canEdit && typedCode.Length > 0, Mint, Ink);
            SetActionEnabled(host, canEdit, Panel, Paper);
            SetActionEnabled(back, canEdit, Panel, Paper);
            SetActionEnabled(startOnline, !onlineBusy && onlineCanStart, Mint, Ink);
            SetActionEnabled(copyCode, !onlineBusy && onlineCode.Length > 0, Panel, Paper);
            SetActionEnabled(leaveLobby, !onlineBusy, Panel, Coral);

            if (onlineError.Length > 0 && !hideOnlineError) modalBody.text = onlineError;
            else if (onlineBusy) modalBody.text = "CONNECTING...";
            else if (!onlineConnected) modalBody.text = "Host a race and share its code, or enter a friend's code.";
            else
            {
                string waiting = onlineHost ? "Share the code with friends." : "Waiting for the host to start.";
                modalBody.text = $"CODE  {onlineCode}\n{onlineHumans} / {RaceSimulation.Entrants} PLAYERS CONNECTED\n{waiting}";
            }
        }

        private void OpenPrivateMenu()
        {
            showOnline = true;
            hideOnlineError = true;
            RefreshMode();
        }

        private void ClosePrivateMenu()
        {
            if (onlineConnected || onlineBusy) return;
            StopCodeEntry();
            showOnline = false;
            hideOnlineError = true;
            RefreshMode();
        }

        private void EditCode()
        {
            if (onlineConnected || onlineBusy || editingCode) return;
            editingCode = true;
            if (TouchScreenKeyboard.isSupported)
                phoneKeyboard = TouchScreenKeyboard.Open(typedCode, TouchScreenKeyboardType.ASCIICapable,
                    false, false, false, false, "Join code", CodeLengthLimit);
            else
                UpdateCodeKeyboard();
            RefreshMode();
        }

        private void PollPrivateKeys(Keyboard keyboard)
        {
            if (onlineBusy) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (editingCode)
                {
                    StopCodeEntry();
                    RefreshMode();
                }
                else if (onlineConnected) RequestLeave();
                else ClosePrivateMenu();
                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame)
            {
                if (onlineConnected) RequestStartOnline();
                else if (typedCode.Length > 0) RequestJoin();
                else EditCode();
                return;
            }

            if (!editingCode) return;
            if (keyboard.backspaceKey.wasPressedThisFrame && typedCode.Length > 0)
            {
                typedCode = typedCode.Substring(0, typedCode.Length - 1);
                RefreshMode();
            }
            if (keyboard.ctrlKey.isPressed && keyboard.vKey.wasPressedThisFrame)
                SetTypedCode(typedCode + GUIUtility.systemCopyBuffer);
        }

        private void UpdateCodeEntry()
        {
            if (!editingCode) return;
            if (phoneKeyboard == null)
            {
                UpdateCodeKeyboard();
                return;
            }

            if (phoneKeyboard.status == TouchScreenKeyboard.Status.Canceled)
            {
                StopCodeEntry();
                RefreshMode();
                return;
            }

            SetTypedCode(phoneKeyboard.text);
            if (phoneKeyboard.status != TouchScreenKeyboard.Status.Visible)
            {
                StopCodeEntry();
                RefreshMode();
            }
        }

        private void UpdateCodeKeyboard()
        {
            Keyboard current = Keyboard.current;
            if (current == codeKeyboard) return;
            if (codeKeyboard != null) codeKeyboard.onTextInput -= AppendCodeCharacter;
            codeKeyboard = current;
            if (codeKeyboard != null) codeKeyboard.onTextInput += AppendCodeCharacter;
        }

        private void AppendCodeCharacter(char character)
        {
            if (typedCode.Length < CodeLengthLimit && IsCodeCharacter(character) && !codeKeyboard.ctrlKey.isPressed)
                SetTypedCode(typedCode + character);
        }

        private void StopCodeEntry()
        {
            if (phoneKeyboard != null)
            {
                phoneKeyboard.active = false;
                phoneKeyboard = null;
            }
            if (codeKeyboard != null)
            {
                codeKeyboard.onTextInput -= AppendCodeCharacter;
                codeKeyboard = null;
            }
            editingCode = false;
        }

        private void SetTypedCode(string value)
        {
            if (typedCode == value) return;
            var normalized = new StringBuilder(CodeLengthLimit);
            foreach (char character in value)
            {
                if (IsCodeCharacter(character)) normalized.Append(char.ToUpperInvariant(character));
                if (normalized.Length == CodeLengthLimit) break;
            }
            string nextCode = normalized.ToString();
            if (typedCode == nextCode) return;
            typedCode = nextCode;
            hideOnlineError = true;
            RefreshMode();
        }

        private string CodeEntryLabel()
        {
            if (editingCode) return typedCode + "_";
            return typedCode.Length > 0 ? typedCode : "ENTER JOIN CODE";
        }

        private void RequestHost()
        {
            if (onlineConnected || onlineBusy) return;
            StopCodeEntry();
            hideOnlineError = false;
            HostRequested?.Invoke();
            RefreshMode();
        }

        private void RequestJoin()
        {
            if (onlineConnected || onlineBusy || typedCode.Length == 0) return;
            StopCodeEntry();
            hideOnlineError = false;
            JoinRequested?.Invoke(typedCode);
            RefreshMode();
        }

        private void RequestLeave()
        {
            if (onlineConnected && !onlineBusy) LeaveRequested?.Invoke();
        }

        private void RequestStartOnline()
        {
            if (onlineConnected && onlineHost && onlineCanStart && !onlineBusy)
                BeginOnlineRequested?.Invoke();
        }

        private void CopyCode()
        {
            if (onlineCode.Length == 0 || onlineBusy) return;
            GUIUtility.systemCopyBuffer = onlineCode;
            copied = true;
            RefreshMode();
        }

        private static bool IsCodeCharacter(char value) =>
            (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') ||
            (value >= '0' && value <= '9');

        private static void SetVisible(Control control, bool visible) => control.Rect.gameObject.SetActive(visible);

        private static void SetActionEnabled(Control control, bool enabled, Color background, Color foreground)
        {
            control.Enabled = enabled;
            control.Background.color = enabled ? background : Panel;
            control.Label.color = enabled ? foreground : Muted;
        }
    }
}
