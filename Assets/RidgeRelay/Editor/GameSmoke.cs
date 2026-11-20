using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Screen = UnityEngine.Device.Screen;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace RidgeRelay.Editor
{
    // Run with -batchmode -projectPath <path> -executeMethod RidgeRelay.Editor.GameSmoke.Run.
    // The smoke check exits after Play Mode finishes, so omit -quit and keep graphics enabled.
    [InitializeOnLoad]
    public static class GameSmoke
    {
        private const string RunningKey = "RidgeRelay.SmokeRunning";
        private const string ResultKey = "RidgeRelay.SmokeResult";
        private const string BestTimeKey = "RidgeRelay.BestTime";
        private const string MotionKey = "RidgeRelay.ReducedMotion";
        private const string MirrorKey = "RidgeRelay.MirroredControls";
        private const string ScenePath = "Assets/RidgeRelay/Race.unity";
        private static readonly FieldInfo RaceField = typeof(RaceGame).GetField("race",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static IEnumerator<Func<bool>> scenario;
        private static Func<bool> ready;
        private static Keyboard keyboard;
        private static Touchscreen touchscreen;
        private static int lastFrame;
        private static double deadline;
        private static string runtimeError;
        private static string currentStep;
        private static float originalTimescale;
        private static bool originalRunInBackground;
        private static InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
        private static InputSettings.BackgroundBehavior originalInputBackgroundBehavior;

        static GameSmoke() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        [MenuItem("Ridge Relay/Run Play Mode Smoke Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start the Ridge Relay smoke check from Edit Mode.");
            ProjectSetup.Prepare();
            EditorSceneManager.OpenScene(ScenePath);
            SnapshotPreferences();
            SessionState.SetBool(RunningKey, true);
            SessionState.SetInt(ResultKey, 1);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                originalTimescale = Time.timeScale;
                originalRunInBackground = Application.runInBackground;
                Application.runInBackground = true;
                // Virtual devices otherwise follow Game View focus rules in the editor.
                originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
                originalInputBackgroundBehavior = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                runtimeError = null;
                currentStep = "loading the race scene";
                lastFrame = -1;
                ready = null;
                deadline = EditorApplication.timeSinceStartup + 90;
                scenario = Check().GetEnumerator();
                Application.logMessageReceived += ObserveLog;
                EditorApplication.update += Advance;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.update -= Advance;
                Application.logMessageReceived -= ObserveLog;
                scenario?.Dispose();
                scenario = null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (touchscreen != null && touchscreen.added) InputSystem.RemoveDevice(touchscreen);
                keyboard = null;
                touchscreen = null;
                Time.timeScale = originalTimescale;
                Application.runInBackground = originalRunInBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
                InputSystem.settings.backgroundBehavior = originalInputBackgroundBehavior;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                RestorePreferences();
                SessionState.SetBool(RunningKey, false);
                if (Application.isBatchMode)
                    EditorApplication.Exit(SessionState.GetInt(ResultKey, 1));
            }
        }

        private static void Advance()
        {
            try
            {
                Require(runtimeError == null, runtimeError);
                if (EditorApplication.timeSinceStartup >= deadline)
                    throw new TimeoutException("Ridge Relay smoke timed out while " + currentStep + ".");
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (ready != null && !ready()) return;

                if (scenario.MoveNext())
                {
                    ready = scenario.Current;
                    return;
                }

                Debug.Log("Ridge Relay smoke passed: menus, accessibility, keyboard, pause, two-finger input, gadget, camera, full race, results, and restart.");
                SessionState.SetInt(ResultKey, 0);
            }
            catch (Exception error)
            {
                Application.logMessageReceived -= ObserveLog;
                Debug.LogException(error);
            }
            EditorApplication.update -= Advance;
            EditorApplication.ExitPlaymode();
        }

        private static void ObserveLog(string message, string stackTrace, LogType type)
        {
            // Unity 6.6 can run editor search indexing as Play Mode initializes.
            if (type == LogType.Exception && stackTrace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup"))
                return;

            if (runtimeError == null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                runtimeError = message + "\n" + stackTrace;
        }

        private static IEnumerable<Func<bool>> Check()
        {
            yield return null;
            var game = UnityEngine.Object.FindAnyObjectByType<RaceGame>();
            var hud = UnityEngine.Object.FindAnyObjectByType<RaceHud>();
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            Require(game != null && hud != null && camera != null, "Race scene did not initialize.");
            Require(RaceField != null, "Could not observe the active race.");
            GameObject courier = GameObject.Find("Player courier");
            Animator animator = courier == null ? null : courier.GetComponentInChildren<Animator>();
            Require(animator != null && animator.avatar != null && animator.avatar.isValid &&
                animator.runtimeAnimatorController != null &&
                courier.GetComponentInChildren<SkinnedMeshRenderer>() != null,
                "The player must use the imported Blender courier rig and animation controller.");
            Require(HasLabel(hud, "RIDGE RELAY"), "Title screen is missing.");
            Require(Screen.width > 0 && Screen.height > 0, "Touch checks require a graphical Game view.");
            Application.runInBackground = true;

            keyboard = InputSystem.AddDevice<Keyboard>();
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            keyboard.MakeCurrent();
            touchscreen.MakeCurrent();

            currentStep = "toggling reduced motion";
            bool originalMotionPreference = hud.ReducedMotion;
            Vector2 motion = ControlPosition(hud, originalMotionPreference ? "MOTION: REDUCED" : "MOTION: STANDARD");
            SetTouch(1, TouchPhase.Began, motion);
            yield return () => hud.ReducedMotion != originalMotionPreference;
            SetTouch(1, TouchPhase.Ended, motion);
            yield return null;

            currentStep = "opening the private menu and editing a code";
            Vector2 online = ControlPosition(hud, "PRIVATE RACE");
            SetTouch(1, TouchPhase.Began, online);
            yield return () => HasLabel(hud, "HOST RACE") && HasLabel(hud, "ENTER JOIN CODE");
            SetTouch(1, TouchPhase.Ended, online);
            yield return null;

            Vector2 code = ControlPosition(hud, "ENTER JOIN CODE");
            SetTouch(1, TouchPhase.Began, code);
            yield return () => HasLabel(hud, "_");
            SetTouch(1, TouchPhase.Ended, code);
            foreach (char character in "ab-1") InputSystem.QueueTextEvent(keyboard, character);
            yield return () => HasLabel(hud, "AB1_");
            Press(Key.Escape);
            yield return () => HasLabel(hud, "AB1");
            Press();
            yield return null;

            Press(Key.Escape);
            yield return () => HasLabel(hud, "RIDGE RELAY");
            Press();
            yield return null;

            currentStep = "starting the race and testing pause";
            Press(Key.Enter);
            yield return () => GetRace(game) != null;
            RaceSimulation race = GetRace(game);
            Require(race.Phase == RacePhase.Countdown, "Return should start a countdown.");
            Press();
            yield return null;
            Press(Key.P);
            yield return () => hud.IsPaused;
            float pausedAt = race.Clock;
            Press();
            yield return null;
            yield return null;
            yield return null;
            Require(race.Clock == pausedAt && HasLabel(hud, "PAUSED"), "Pause should freeze the race clock.");

            Vector2 resume = ControlPosition(hud, "RESUME");
            SetTouch(1, TouchPhase.Began, resume);
            yield return () => !hud.IsPaused;
            SetTouch(1, TouchPhase.Ended, resume);
            Time.timeScale = 2;
            yield return () => race.Phase == RacePhase.Running;

            RunnerState runner = race.Runners[0];
            Vector3 coursePosition = TrackView.Sample(runner.Distance, runner.Lateral).Position;
            Vector3 toRunner = coursePosition - camera.transform.position;
            Require(toRunner.magnitude > 3 && Vector3.Dot(camera.transform.forward, toRunner) > 0,
                "The chase camera should look forward from behind the runner.");
            Vector3 courierOnScreen = camera.WorldToViewportPoint(coursePosition + Vector3.up * 0.6f);
            Require(courierOnScreen.z > 0 && courierOnScreen.x > 0.1f && courierOnScreen.x < 0.9f &&
                courierOnScreen.y > 0.1f && courierOnScreen.y < 0.9f,
                "The chase camera should keep the courier inside the playable view.");

            currentStep = "moving lanes with the keyboard";
            int startingLane = runner.Lane;
            Key inward = startingLane > 0 ? Key.LeftArrow : Key.RightArrow;
            Key outward = inward == Key.LeftArrow ? Key.RightArrow : Key.LeftArrow;
            Press(inward);
            int inwardLane = startingLane + (inward == Key.LeftArrow ? -1 : 1);
            yield return () => runner.Lane == inwardLane;
            Press();
            yield return null;
            Press(outward);
            yield return () => runner.Lane == startingLane;
            Press();
            yield return null;

            currentStep = "swiping sideways and up with simultaneous touches";
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 swipe = center + Vector2.right * (Screen.height * 0.22f * (inwardLane - startingLane));
            Vector2 jumpStart = center + Vector2.left * (Screen.width * 0.15f);
            Vector2 jumpEnd = jumpStart + Vector2.up * (Screen.height * 0.22f);
            SetTouch(1, TouchPhase.Began, center);
            SetTouch(2, TouchPhase.Began, jumpStart);
            yield return null;
            SetTouch(1, TouchPhase.Moved, swipe);
            SetTouch(2, TouchPhase.Moved, jumpEnd);
            yield return () => runner.Lane == inwardLane && runner.Height > 0;
            SetTouch(1, TouchPhase.Ended, swipe);
            SetTouch(2, TouchPhase.Ended, jumpEnd);
            yield return null;
            Press(outward);
            yield return () => runner.Lane == startingLane;
            Press();

            currentStep = "collecting and using Tailwind";
            // The left start lane reaches the first Tailwind before the other racers.
            yield return () => runner.Held == Gadget.Tailwind || runner.Distance > 49;
            Require(runner.Held == Gadget.Tailwind && HasLabel(hud, "TAILWIND") &&
                HasLabel(hud, "Tap to sprint faster"),
                "The first pickup should show the gadget and explain its effect.");
            Press(Key.E);
            yield return () => runner.Boost > 0;
            Press();
            Require(runner.Held == Gadget.None, "Using Tailwind should empty the gadget slot.");
            yield return null;
            RaceEffects effects = game.GetComponent<RaceEffects>();
            Require(effects != null && Array.Exists(effects.GetComponentsInChildren<MeshRenderer>(),
                renderer => renderer.enabled && renderer.name == "Wind strand" &&
                    renderer.sharedMaterial.name == "Tailwind"),
                "Tailwind use should show an active trail.");

            currentStep = "completing the course";
            Time.timeScale = 8;
            int pulseFrame = -1;
            bool pulsing = false;
            while (race.Phase != RacePhase.Finished)
            {
                // Keep actual keyboard input and let the game tick normally for the full course.
                if (pulsing && Time.frameCount > pulseFrame)
                {
                    Press();
                    pulsing = false;
                    yield return null;
                    continue;
                }

                if (FindNextLaneChange(runner, out Key avoidance))
                {
                    Press(avoidance);
                    pulsing = true;
                    pulseFrame = Time.frameCount;
                }
                yield return null;
            }

            Require(runner.FinishTime >= 60 && runner.FinishTime <= 100, "Race duration is outside the slice target range.");
            Require(runner.Place >= 1 && runner.Place <= RaceSimulation.Entrants, "Result placement is invalid.");
            Require(HasLabel(hud, "RACE AGAIN") && HasLabel(hud, " PLACE", exact: false),
                "Results screen did not show placement and a retry action.");
            float previousRecord = SessionState.GetFloat(BestTimeKey, -1);
            float expected = previousRecord > 0 ? Mathf.Min(previousRecord, runner.FinishTime) : runner.FinishTime;
            Require(Mathf.Abs(PlayerPrefs.GetFloat(BestTimeKey) - expected) < 0.01f, "Best time was not persisted.");

            currentStep = "retrying from results";
            Time.timeScale = 1;
            Press(Key.Enter);
            yield return () => GetRace(game) != race;
            RaceSimulation retry = GetRace(game);
            Require(retry.Phase == RacePhase.Countdown && retry.Runners[0].Held == Gadget.None &&
                retry.Runners[0].Distance < 1, "Retry did not reset race state.");
            Press();
        }

        private static bool FindNextLaneChange(RunnerState runner, out Key key)
        {
            foreach (CourseFeature feature in RaceSimulation.Features)
            {
                float ahead = feature.Start - runner.Distance;
                if (feature.Lane != runner.Lane || feature.Item != Gadget.None || ahead <= 0 || ahead > 8)
                    continue;
                key = runner.Lane == 1 ? Key.LeftArrow : Key.RightArrow;
                return true;
            }
            key = Key.None;
            return false;
        }

        private static RaceSimulation GetRace(RaceGame game) => (RaceSimulation)RaceField.GetValue(game);

        private static void Press(params Key[] keys)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        }

        private static void SetTouch(int id, TouchPhase phase, Vector2 position)
        {
            touchscreen.MakeCurrent();
            InputSystem.QueueStateEvent(touchscreen, new TouchState
            {
                touchId = id, phase = phase, position = position
            });
        }

        private static bool HasLabel(RaceHud hud, string label, bool exact = true)
        {
            foreach (Text text in hud.GetComponentsInChildren<Text>())
                if (exact ? text.text == label : text.text.Contains(label)) return true;
            return false;
        }

        private static Vector2 ControlPosition(RaceHud hud, string label)
        {
            Canvas.ForceUpdateCanvases();
            foreach (Text text in hud.GetComponentsInChildren<Text>())
                if (text.text == label) return RectTransformUtility.WorldToScreenPoint(null, text.rectTransform.position);
            throw new InvalidOperationException("Missing control: " + label);
        }

        private static void SnapshotPreferences()
        {
            SessionState.SetBool(BestTimeKey + ".HasValue", PlayerPrefs.HasKey(BestTimeKey));
            SessionState.SetFloat(BestTimeKey, PlayerPrefs.GetFloat(BestTimeKey, -1));
            SnapshotInteger(MotionKey);
            SnapshotInteger(MirrorKey);
        }

        private static void SnapshotInteger(string key)
        {
            SessionState.SetBool(key + ".HasValue", PlayerPrefs.HasKey(key));
            SessionState.SetInt(key, PlayerPrefs.GetInt(key));
        }

        private static void RestorePreferences()
        {
            if (SessionState.GetBool(BestTimeKey + ".HasValue", false))
                PlayerPrefs.SetFloat(BestTimeKey, SessionState.GetFloat(BestTimeKey, -1));
            else
                PlayerPrefs.DeleteKey(BestTimeKey);
            RestoreInteger(MotionKey);
            RestoreInteger(MirrorKey);
            PlayerPrefs.Save();
        }

        private static void RestoreInteger(string key)
        {
            if (SessionState.GetBool(key + ".HasValue", false))
                PlayerPrefs.SetInt(key, SessionState.GetInt(key, 0));
            else
                PlayerPrefs.DeleteKey(key);
        }

        private static void Require(bool passed, string reason)
        {
            if (!passed) throw new InvalidOperationException("Ridge Relay smoke check failed. " + reason);
        }
    }
}

