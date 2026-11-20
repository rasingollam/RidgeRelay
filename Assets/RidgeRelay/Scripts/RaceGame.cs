using UnityEngine;

namespace RidgeRelay
{
    [DefaultExecutionOrder(100)]
    public sealed class RaceGame : MonoBehaviour
    {
        private const string BestTimeKey = "RidgeRelay.BestTime";
        private readonly RaceCommand[] commands = new RaceCommand[RaceSimulation.Entrants];
        private readonly RaceBot[] bots = new RaceBot[RaceSimulation.Entrants - 1];
        private readonly RunnerView[] views = new RunnerView[RaceSimulation.Entrants];
        private readonly RaceSimulation preview = new RaceSimulation();
        private TrackView track;
        private RaceHud hud;
        private RaceAudio audioFeedback;
        private RaceEffects effects;
        private PrivateRaceSession online;
        private Camera chase;
        private RaceSimulation race;
        private float accumulator;
        private float bestTime;
        private bool snapCamera;
        private bool displayingOnline;

        private void Awake()
        {
            Application.runInBackground = !Application.isMobilePlatform;
            Application.targetFrameRate = Application.isMobilePlatform &&
                SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 3800 ? 30 : 60;
            bestTime = PlayerPrefs.GetFloat(BestTimeKey, -1);
            RenderSettings.ambientLight = new Color(0.66f, 0.64f, 0.72f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.77f, 0.77f, 0.91f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 48;
            RenderSettings.fogEndDistance = 155;

            var sun = new GameObject("Canyon sunlight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.76f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -38, 0);

            chase = new GameObject("Chase camera").AddComponent<Camera>();
            chase.gameObject.AddComponent<AudioListener>();
            chase.clearFlags = CameraClearFlags.SolidColor;
            chase.backgroundColor = new Color(0.56f, 0.71f, 0.91f);
            chase.farClipPlane = 180;
            chase.nearClipPlane = 0.15f;
            chase.fieldOfView = 55;

            track = new GameObject("Windmill Gorge").AddComponent<TrackView>();
            track.Build();
            audioFeedback = gameObject.AddComponent<RaceAudio>();
            effects = gameObject.AddComponent<RaceEffects>();
            online = gameObject.AddComponent<PrivateRaceSession>();
            hud = new GameObject("Race HUD").AddComponent<RaceHud>();
            hud.StartRequested += StartRace;
            hud.RetryRequested += StartRace;
            hud.MenuRequested += ReturnToMenu;
            hud.HostRequested += HostPrivateRace;
            hud.JoinRequested += JoinPrivateRace;
            hud.LeaveRequested += LeavePrivateRace;
            hud.BeginOnlineRequested += BeginPrivateRace;
            online.RaceStarted += OnPrivateRaceStarted;
            hud.Present(null, bestTime);

            for (int i = 0; i < views.Length; i++) views[i] = new RunnerView(i);
            ShowRunners(preview);
            MoveCamera(preview.Runners[0], true);
        }

        private void StartRace()
        {
            if (online.Connected || online.Busy) return;
            race = new RaceSimulation();
            accumulator = 0;
            snapCamera = true;
            audioFeedback.ResetRace();
            for (int i = 0; i < bots.Length; i++) bots[i] = new RaceBot(167 + i * 37);
            hud.Present(race, bestTime);
        }

        private void ReturnToMenu()
        {
            race = null;
            accumulator = 0;
            snapCamera = true;
            audioFeedback.ResetRace();
            hud.Present(null, bestTime);
        }

        private async void HostPrivateRace() => await online.HostAsync();
        private async void JoinPrivateRace(string code) => await online.JoinAsync(code);
        private async void LeavePrivateRace() => await online.LeaveAsync();

        private void BeginPrivateRace()
        {
            if (online.CanStart) online.TryStartRace();
        }

        private void OnPrivateRaceStarted()
        {
            snapCamera = true;
            audioFeedback.ResetRace();
        }

        private void Update()
        {
            if (online.Connected || online.Busy)
            {
                displayingOnline = true;
                online.Advance(Mathf.Min(Time.unscaledDeltaTime, 0.1f),
                    online.CanControl && !hud.IsPaused ? hud.ReadCommand() : default);
                int slot = Mathf.Clamp(online.LocalSlot, 0, RaceSimulation.Entrants - 1);
                hud.SetOnlineStatus(online.JoinCode, online.HumanCount, online.IsHost,
                    online.Connected, online.Busy, online.Error, slot, online.CanStart, online.CanControl);
                RaceSimulation onlineRace = online.DisplayRace;
                if (onlineRace != null && online.Connected) audioFeedback.Present(onlineRace, slot);
                hud.Present(online.Connected ? onlineRace : null, bestTime);
                return;
            }

            hud.SetOnlineStatus(null, 0, false, false, false, online.Error, 0, false, false);
            if (displayingOnline)
            {
                displayingOnline = false;
                snapCamera = true;
                race = null;
                hud.Present(null, bestTime);
            }
            if (race == null || hud.IsPaused || race.Phase == RacePhase.Finished) return;

            accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            while (accumulator >= RaceSimulation.StepSeconds && race.Phase != RacePhase.Finished)
            {
                commands[0] = hud.ReadCommand();
                for (int i = 1; i < commands.Length; i++) commands[i] = bots[i - 1].Decide(race, i);
                race.Tick(commands);
                accumulator -= RaceSimulation.StepSeconds;
            }

            audioFeedback.Present(race);
            if (race.Phase == RacePhase.Finished)
            {
                float finish = race.Runners[0].FinishTime;
                if (bestTime <= 0 || finish < bestTime)
                {
                    bestTime = finish;
                    PlayerPrefs.SetFloat(BestTimeKey, bestTime);
                    PlayerPrefs.Save();
                }
            }

            hud.Present(race, bestTime);
        }

        private void LateUpdate()
        {
            bool paused = !displayingOnline && hud.IsPaused;
            foreach (RunnerView view in views) view.SetPaused(paused);
            if (paused) return;
            RaceSimulation visible = (displayingOnline ? online.DisplayRace : race) ?? preview;
            ShowRunners(visible);
            int slot = displayingOnline ? Mathf.Clamp(online.LocalSlot, 0, RaceSimulation.Entrants - 1) : 0;
            track.UpdateVisuals(visible, slot);
            effects.Present(visible == preview ? null : visible, slot, hud.ReducedMotion);
            RunnerState player = visible.Runners[slot];
            MoveCamera(player, snapCamera || player.Recovery >= 0.84f);
            snapCamera = false;
        }

        private void ShowRunners(RaceSimulation state)
        {
            for (int i = 0; i < views.Length; i++)
                views[i].Show(state.Runners[i], state.Phase, hud.ReducedMotion);
        }

        private void MoveCamera(RunnerState runner, bool snap)
        {
            float tunnel = TrackView.TunnelCameraBlend(runner.Distance);
            var rear = TrackView.Sample(runner.Distance - Mathf.Lerp(5.7f, 3.9f, tunnel),
                runner.Lateral * Mathf.Lerp(0.53f, 1f, tunnel));
            var anchor = TrackView.Sample(runner.Distance,
                runner.Lateral * Mathf.Lerp(0.25f, 1f, tunnel));
            var ahead = TrackView.Sample(runner.Distance + Mathf.Lerp(8f, 5f, tunnel),
                runner.Lateral * Mathf.Lerp(0.12f, 1f, tunnel));
            Vector3 targetPosition = rear.Position;
            targetPosition.y = Mathf.Max(rear.Position.y + Mathf.Lerp(2.5f, 1.9f, tunnel),
                anchor.Position.y + Mathf.Lerp(1.8f, 1.3f, tunnel));
            targetPosition.y += runner.Height * 0.16f * (1 - tunnel);
            Vector3 aim = ahead.Position + Vector3.up * 0.8f;
            float blend = snap ? 1 : 1 - Mathf.Exp(-14 * Time.deltaTime);
            chase.transform.position = Vector3.Lerp(chase.transform.position, targetPosition, blend);
            Quaternion heading = Quaternion.LookRotation(aim - chase.transform.position, Vector3.up);
            chase.transform.rotation = Quaternion.Slerp(chase.transform.rotation, heading, blend);
            bool accelerating = runner.Boost > 0 || runner.RouteBoost > 0 ||
                runner.Slipstream >= RaceSimulation.SlipstreamActiveThreshold;
            float fov = !hud.ReducedMotion && accelerating ? Mathf.Lerp(59, 55, tunnel) : 55;
            chase.fieldOfView = Mathf.Lerp(chase.fieldOfView, fov, blend);
        }
    }
}


