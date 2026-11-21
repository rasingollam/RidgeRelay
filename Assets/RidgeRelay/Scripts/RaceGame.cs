using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RidgeRelay
{
    [DefaultExecutionOrder(100)]
    public sealed class RaceGame : MonoBehaviour
    {
        private const string BestTimeKey = "RidgeRelay.BestTime";
        private readonly RaceCommand[] commands = new RaceCommand[RaceSimulation.Entrants];
        private readonly RaceBot[] bots = new RaceBot[RaceSimulation.Entrants - 1];
        private readonly RunnerView[] views = new RunnerView[RaceSimulation.Entrants];
        private readonly Vector2[] runnerOffsets = new Vector2[RaceSimulation.Entrants];
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
        private float cameraShoulder;
        private float shoulderTarget;
        private float cameraCrowd;
        private VolumeProfile lookProfile;
        private Material skyMaterial;

        private void Awake()
        {
            Application.runInBackground = !Application.isMobilePlatform;
            Application.targetFrameRate = Application.isMobilePlatform &&
                SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 3800 ? 30 : 60;
            bestTime = PlayerPrefs.GetFloat(BestTimeKey, -1);
            RenderSettings.ambientLight = new Color(0.66f, 0.64f, 0.72f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.65f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.54f, 0.61f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.40f, 0.44f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.90f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 85;
            RenderSettings.fogEndDistance = 280;

            var sun = new GameObject("Canyon sunlight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.76f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sun.transform.rotation = Quaternion.Euler(48, -38, 0);
            var fill = new GameObject("Cool sky fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.68f, 0.80f, 1);
            fill.intensity = 0.25f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(30, 145, 0);

            chase = new GameObject("Chase camera").AddComponent<Camera>();
            chase.tag = "MainCamera";
            chase.gameObject.AddComponent<AudioListener>();
            Shader skyShader = Shader.Find("RidgeRelay/Canyon Sky");
            if (skyShader != null)
            {
                skyMaterial = new Material(skyShader);
                Texture2D sky=Resources.Load<Texture2D>("RidgeRelay/Surfaces/DaySky");
                if(sky!=null){skyMaterial.SetTexture("_DaySky",sky);skyMaterial.SetFloat("_UseTexture",1);}
                RenderSettings.skybox = skyMaterial;
            }
            chase.clearFlags = CameraClearFlags.Skybox;
            chase.backgroundColor = new Color(0.56f, 0.71f, 0.91f);
            chase.farClipPlane = 350;
            chase.nearClipPlane = 0.15f;
            chase.fieldOfView = 55;
            chase.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            lookProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemapping = lookProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = lookProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.18f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.65f);
            var color = lookProfile.Add<ColorAdjustments>(true);
            color.contrast.Override(2);
            color.saturation.Override(3);
            var volume = new GameObject("Canyon color grade").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.sharedProfile = lookProfile;

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
            MovePreviewCamera(true);
        }

        private void OnDestroy()
        {
            if (lookProfile != null) Destroy(lookProfile);
            if (skyMaterial != null) Destroy(skyMaterial);
        }

        private void StartRace()
        {
            if (online.Connected || online.Busy) return;
            race = new RaceSimulation(waitForAll: true, relaxedBots: true);
            race.Runners[0].FurColor = CourierAppearance.Selected;
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
            online.FurColor = CourierAppearance.Selected;
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
            effects.SetPaused(paused);
            if (paused) return;
            RaceSimulation visible = (displayingOnline ? online.DisplayRace : race) ?? preview;
            preview.Runners[0].FurColor = CourierAppearance.Selected;
            int slot = displayingOnline ? Mathf.Clamp(online.LocalSlot, 0, RaceSimulation.Entrants - 1) : 0;
            ShowRunners(visible);
            for (int i = 0; i < views.Length; i++) views[i].MarkLocal(i == slot);
            track.UpdateVisuals(visible, slot);
            effects.Present(visible == preview ? null : visible, slot, hud.ReducedMotion, runnerOffsets);
            if (visible == preview)
            {
                for (int i = 1; i < views.Length; i++) views[i].SetVisible(false);
                MovePreviewCamera(snapCamera);
                views[0].FaceCamera(chase);
                snapCamera = false;
                return;
            }
            RunnerState player = visible.Runners[slot];
            if (player.FinishTime >= 0)
            {
                MoveFinishCamera(visible, snapCamera);
                snapCamera = false;
                return;
            }
            float nearest = float.PositiveInfinity;
            int closeRival = -1;
            int crowdedRivals = 0;
            for (int i = 0; i < views.Length; i++)
            {
                RunnerState rival = visible.Runners[i];
                float separation = Mathf.Abs(rival.Distance - player.Distance);
                if (i != slot && rival.Recovery <= 0 && rival.FinishTime < 0 && separation < 1.6f &&
                    Mathf.Abs(rival.Lateral - player.Lateral) < 0.8f) crowdedRivals++;
                if (i != slot && rival.Recovery <= 0 && rival.FinishTime < 0 && separation < 4 &&
                    separation < nearest && Mathf.Abs(rival.Lateral - player.Lateral) < 1.2f)
                {
                    nearest = separation;
                    closeRival = i;
                }
            }
            if (closeRival < 0) shoulderTarget = 0;
            else if (Mathf.Abs(shoulderTarget) < 0.1f)
                shoulderTarget = visible.Runners[closeRival].Lateral > player.Lateral ? -1.25f : 1.25f;
            cameraShoulder = Mathf.Lerp(cameraShoulder, shoulderTarget,
                1 - Mathf.Exp(-4 * Time.deltaTime));
            cameraCrowd = Mathf.Lerp(cameraCrowd, Mathf.Clamp01(crowdedRivals * 0.5f),
                1 - Mathf.Exp(-5 * Time.deltaTime));
            MoveCamera(player, snapCamera || player.Recovery >= 0.84f);
            snapCamera = false;
        }

        private void ShowRunners(RaceSimulation state)
        {
            RacerPresentationLayout.Resolve(state.Runners, runnerOffsets);
            for (int i = 0; i < views.Length; i++)
            {
                views[i].Show(state.Runners[i], state.Phase, hud.ReducedMotion, runnerOffsets[i]);
                runnerOffsets[i] = views[i].PresentationOffset;
            }
        }

        private void MoveCamera(RunnerState runner, bool snap)
        {
            float tunnel = TrackView.TunnelCameraBlend(runner.Distance);
            float crowd = cameraCrowd * (1 - tunnel);
            var rear = TrackView.Sample(runner.Distance - Mathf.Lerp(Mathf.Lerp(3.6f, 4.4f, crowd), 3.9f, tunnel),
                runner.Lateral * Mathf.Lerp(0.53f, 1f, tunnel));
            var anchor = TrackView.Sample(runner.Distance,
                runner.Lateral * Mathf.Lerp(0.25f, 1f, tunnel));
            var ahead = TrackView.Sample(runner.Distance + Mathf.Lerp(Mathf.Lerp(7f, 4f, crowd), 5f, tunnel),
                runner.Lateral * Mathf.Lerp(0.12f, 1f, tunnel));
            Vector3 targetPosition = rear.Position + rear.Up * Mathf.Lerp(Mathf.Lerp(1.85f, 2.1f, crowd), 1.9f, tunnel);
            targetPosition += rear.Right * cameraShoulder * (1 - tunnel);
            targetPosition.y += runner.Height * 0.2f;
            Vector3 aim = Vector3.Lerp(ahead.Position, anchor.Position,
                Mathf.Abs(cameraShoulder) * 0.13f) + Vector3.up * 0.8f;
            float blend = snap ? 1 : 1 - Mathf.Exp(-14 * Time.deltaTime);
            chase.transform.position = Vector3.Lerp(chase.transform.position, targetPosition, blend);
            Quaternion heading = Quaternion.LookRotation(aim - chase.transform.position, Vector3.up);
            chase.transform.rotation = Quaternion.Slerp(chase.transform.rotation, heading, blend);
            bool accelerating = runner.Boost > 0 || runner.RouteBoost > 0 ||
                runner.Slipstream >= RaceSimulation.SlipstreamActiveThreshold;
            float fov = !hud.ReducedMotion && accelerating ? Mathf.Lerp(57, 55, tunnel) :
                Mathf.Lerp(Mathf.Lerp(52, 56, crowd), 55, tunnel);
            chase.fieldOfView = Mathf.Lerp(chase.fieldOfView, fov, blend);
        }

        private void MoveFinishCamera(RaceSimulation state, bool snap)
        {
            int finishers = 0;
            foreach (RunnerState runner in state.Runners)
                if (runner.FinishTime >= 0) finishers++;
            float group = Mathf.Clamp01((finishers - 1) / 3f);
            float center = 5 + (finishers - 1) * 0.8f;
            TrackPose finish = TrackView.Sample(RaceSimulation.CourseLength + 8);
            Vector3 position = finish.Offset(center + Mathf.Lerp(3.6f, 5, group),
                2.4f, Mathf.Lerp(3.4f, 5, group));
            Vector3 aim = finish.Offset(center, 0.9f);
            float blend = snap ? 1 : 1 - Mathf.Exp(-2.8f * Time.deltaTime);
            chase.transform.position = Vector3.Lerp(chase.transform.position, position, blend);
            chase.transform.rotation = Quaternion.Slerp(chase.transform.rotation,
                Quaternion.LookRotation(aim - chase.transform.position, Vector3.up), blend);
            chase.fieldOfView = Mathf.Lerp(chase.fieldOfView, Mathf.Lerp(42, 49, group), blend);
        }

        private void MovePreviewCamera(bool snap)
        {
            TrackPose hero = TrackView.Sample(0, -1);
            Vector3 position = hero.Offset(-0.5f, 1.25f, -3.4f);
            Vector3 aim = hero.Offset(0.65f, 0.78f);
            float blend = snap ? 1 : 1 - Mathf.Exp(-4 * Time.deltaTime);
            chase.transform.position = Vector3.Lerp(chase.transform.position, position, blend);
            chase.transform.rotation = Quaternion.Slerp(chase.transform.rotation,
                Quaternion.LookRotation(aim - chase.transform.position, Vector3.up), blend);
            chase.fieldOfView = Mathf.Lerp(chase.fieldOfView, 38, blend);
        }
    }
}


