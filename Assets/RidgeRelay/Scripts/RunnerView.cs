using UnityEngine;

namespace RidgeRelay
{
    public sealed class RunnerView
    {
        private static readonly GameObject Model = Resources.Load<GameObject>("RidgeRelay/Jerboa");
        private static readonly RuntimeAnimatorController Controller =
            Resources.Load<RuntimeAnimatorController>("RidgeRelay/RunnerAnimator");
        private static readonly Color[] TeamColors =
        {
            new Color(1, 1, 1), new Color(0.58f, 0.86f, 1),
            new Color(1, 0.83f, 0.51f), new Color(0.78f, 0.66f, 1)
        };

        private readonly Transform root;
        private readonly Transform facing;
        private readonly Animator animator;
        private static readonly Quaternion ImportedFacing = Quaternion.Euler(0, 180, 0);
        private string animationState;
        private float previousHeight;
        private float previousSlow;
        private float previousCooldown;
        private readonly System.Collections.Generic.List<Material> characterMaterials = new();
        private static readonly Texture2D[,] appearancePalettes = new Texture2D[RaceSimulation.FurColorCount, 2];
        private Texture originalPalette;
        private bool isLocal;
        private int furColor = -1;
        private bool appliedLocal;
        private readonly TextMesh finishRank;
        private float finishStarted = -1;
        private Vector3 finishOrigin;
        private Quaternion finishHeading;
        private float previousImpactTime;
        private float pullStarted = -10;
        private Vector3 pullOrigin;
        private bool shown;
        private readonly SkinnedMeshRenderer skin;
        private readonly int blinkLeft = -1;
        private readonly int blinkRight = -1;
        private readonly float blinkOffset;
        private readonly int powerLayer = -1;
        private float powerStarted = -10;
        private float previousCastTime;

        public RunnerView(int index)
        {
            root = new GameObject(index == 0 ? "Player courier" : $"Courier {index + 1}").transform;
            blinkOffset = index * 0.71f;
            finishRank = new GameObject("Finish position").AddComponent<TextMesh>();
            finishRank.transform.SetParent(root, false);
            finishRank.transform.localPosition = new Vector3(0, 2.05f, 0);
            finishRank.anchor = TextAnchor.MiddleCenter;
            finishRank.alignment = TextAlignment.Center;
            finishRank.fontSize = 64;
            finishRank.characterSize = 0.045f;
            finishRank.color = new Color(1, 0.87f, 0.3f);
            finishRank.gameObject.SetActive(false);
            if (Model != null)
            {
                // The imported jerboa faces local -Z, while the track heading is +Z.
                // Keep the correction above the animated model so clips cannot overwrite it.
                facing = new GameObject("Model facing").transform;
                facing.SetParent(root, false);
                facing.localRotation = ImportedFacing;
                var model = Object.Instantiate(Model, facing, false);
                skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin != null)
                    for (int shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++)
                    {
                        string name = skin.sharedMesh.GetBlendShapeName(shape);
                        if (name.EndsWith("Blink.L")) blinkLeft = shape;
                        if (name.EndsWith("Blink.R")) blinkRight = shape;
                    }
                animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = Controller;
                powerLayer = animator.GetLayerIndex("Upper Body Power");
                animator.applyRootMotion = false;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.materials)
                    {
                        if (material.mainTexture == null) continue;
                        originalPalette = material.mainTexture;
                        Texture2D furDetail=Resources.Load<Texture2D>("RidgeRelay/JerboaFurNormal");
                        if(furDetail!=null)
                        {
                            material.SetTexture("_BumpMap",furDetail);
                            material.SetFloat("_BumpScale",0.65f);
                            material.EnableKeyword("_NORMALMAP");
                        }
                        characterMaterials.Add(material);
                    }
            }
            else
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.transform.SetParent(root, false);
                placeholder.transform.localPosition = Vector3.up * 0.62f;
                placeholder.transform.localScale = new Vector3(0.55f, 0.62f, 0.55f);
                Object.Destroy(placeholder.GetComponent<Collider>());
                placeholder.GetComponent<Renderer>().material.color =
                    index == 0 ? new Color(0.93f, 0.3f, 0.21f) : TeamColors[index];
            }
        }

        private static Texture2D CreatePalette(Texture source, int fur, bool local)
        {
            // The Blender atlas puts the backpack canvas in the first cell of row two.
            var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var palette = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true)
                {
                    name = local ? "Local player blue backpack" : "Courier fur " + fur,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                palette.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                int cell = source.width / 4;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        float bodyGrain = palette.GetPixel(x, y).r / 0.67f;
                        float earGrain = palette.GetPixel(x, y + cell * 2).r / 0.39f;
                        palette.SetPixel(x, y, CourierAppearance.FurColors[fur] * bodyGrain);
                        palette.SetPixel(x, y + cell * 2, CourierAppearance.FurColors[fur] * (0.88f * earGrain));
                    }
                if (local)
                    for (int y = cell; y < cell * 2; y++)
                        for (int x = 0; x < cell; x++)
                            palette.SetPixel(x, y, new Color(0.12f, 0.42f, 1f) *
                                (palette.GetPixel(x, y).r / 0.93f));
                palette.Apply(true, true);
                return palette;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public void MarkLocal(bool local)
        {
            if (isLocal == local) return;
            isLocal = local;
            ApplyAppearance(furColor);
        }

        private void ApplyAppearance(int choice)
        {
            choice = Mathf.Clamp(choice, 0, RaceSimulation.FurColorCount - 1);
            if (furColor == choice && appliedLocal == isLocal) return;
            furColor = choice;
            appliedLocal = isLocal;
            if (originalPalette == null) return;
            int identity = isLocal ? 1 : 0;
            if (appearancePalettes[choice, identity] == null)
                appearancePalettes[choice, identity] = CreatePalette(originalPalette, choice, isLocal);
            foreach (Material material in characterMaterials)
                material.mainTexture = appearancePalettes[choice, identity];
        }

        public void SetPaused(bool paused)
        {
            if (animator != null) animator.speed = paused ? 0 : 1;
        }

        public void SetVisible(bool visible) => root.gameObject.SetActive(visible);

        public void FaceCamera(Camera camera)
        {
            Vector3 direction = camera.transform.position - root.position;
            direction.y = 0;
            root.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        public void Show(RunnerState state, RacePhase phase, bool reducedMotion)
        {
            ApplyAppearance(state.FurColor);
            var pose = TrackView.Sample(state.Distance, state.Lateral);
            if (shown && state.ImpactPower == Gadget.Magnet && state.ImpactTime > 0 &&
                (previousImpactTime <= 0 || state.ImpactTime > previousImpactTime + 0.05f))
            {
                pullOrigin = root.position;
                pullStarted = Time.time;
            }
            if (phase == RacePhase.Countdown) pullStarted = -10;
            Vector3 position = pose.Position + Vector3.up * state.Height;
            if (state.FinishTime < 0 && Time.time - pullStarted < 0.28f)
                position = Vector3.Lerp(pullOrigin, position, Mathf.SmoothStep(0, 1, (Time.time - pullStarted) / 0.28f));
            root.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(pose.Forward, Vector3.up));
            shown = true;
            previousImpactTime = state.ImpactTime;
            finishRank.gameObject.SetActive(state.FinishTime >= 0);
            if (state.FinishTime >= 0)
            {
                if (finishStarted < 0)
                {
                    finishStarted = Time.time;
                    finishOrigin = root.position;
                    finishHeading = root.rotation;
                }
                TrackPose celebration = TrackView.FinishPose(state.Place);
                float arrival = Mathf.SmoothStep(0, 1, (Time.time - finishStarted) / 1.1f);
                root.SetPositionAndRotation(Vector3.Lerp(finishOrigin, celebration.Position, arrival),
                    Quaternion.Slerp(finishHeading, Quaternion.LookRotation(
                        celebration.Right * 0.7f + celebration.Forward * 0.72f, Vector3.up), arrival));
                finishRank.text = state.Place == 1 ? "1st" : state.Place == 2 ? "2nd" :
                    state.Place == 3 ? "3rd" : "4th";
                if (Camera.main != null) finishRank.transform.rotation = Camera.main.transform.rotation;
            }
            else finishStarted = -1;
            root.gameObject.SetActive(state.Recovery <= 0);
            if (state.Recovery > 0)
            {
                animationState = null;
                previousHeight = 0;
                if (facing != null) facing.localRotation = ImportedFacing;
                return;
            }
            if (animator == null || Controller == null) return;
            bool newPower = state.ItemCooldown > previousCooldown + 0.05f && state.LastUsedPower != Gadget.None;
            newPower |= state.CastTime > previousCastTime + 0.05f;
            if (powerLayer >= 0 && newPower && phase == RacePhase.Running && state.FinishTime < 0)
            {
                Gadget power = state.LastUsedPower;
                string action = power == Gadget.Magnet ? "CastMagnet" : power == Gadget.Rocket ? "CastRocket" :
                    power == Gadget.Shock ? "CastShock" : "PowerAction";
                powerStarted = Time.time;
                animator.CrossFadeInFixedTime(action, 0.05f, powerLayer, 0);
            }

            string desired;
            if (phase == RacePhase.Countdown) desired = "Idle";
            else if (state.FinishTime >= 0) desired = "Finish";
            else if (state.Height > 0)
            {
                bool falling = state.Height < previousHeight ||
                    (state.Height == previousHeight && animationState == "Fall");
                desired = falling ? "Fall" : "Jump";
            }
            else if (previousHeight > 0) desired = "Land";
            else if (previousSlow <= 0 && state.Slow > 0) desired = "Stumble";
            else if (powerLayer < 0 && newPower) desired = "Gadget";
            else if (animationState == "Land" || animationState == "Stumble" || animationState == "Gadget")
            {
                AnimatorStateInfo clip = animator.GetCurrentAnimatorStateInfo(0);
                desired = !clip.IsName(animationState) || clip.normalizedTime < 0.8f
                    ? animationState : "Run";
            }
            else desired = "Run";

            if (desired != animationState)
            {
                animator.CrossFadeInFixedTime(desired, 0.08f);
                animationState = desired;
            }

            bool accelerating = state.Boost > 0 || state.RouteBoost > 0 ||
                state.Slipstream >= RaceSimulation.SlipstreamActiveThreshold;
            animator.SetFloat("Speed", state.Slow > 0 ? 0.58f : accelerating ? 1.35f : 1);
            animator.SetFloat("Grade", pose.Forward.y);
            float steering = phase == RacePhase.Running && state.FinishTime < 0
                ? Mathf.Clamp((state.Lane - state.Lateral) * 1.65f, -1, 1) : 0;
            float motion = reducedMotion ? 0.35f : 1f;
            float pitch = desired == "Run" && accelerating ? 7 * motion : 0;
            Quaternion lean = Quaternion.Euler(pitch, steering * 12 * motion, -steering * 16 * motion) *
                ImportedFacing;
            if (state.Slow > 0 && state.ImpactPower == Gadget.Shock)
                lean = Quaternion.AngleAxis(Mathf.Sin(Time.time * 48) * 4 * motion, Vector3.forward) * lean;
            facing.localRotation = Quaternion.Slerp(facing.localRotation, lean,
                1 - Mathf.Exp(-15 * Time.deltaTime));
            if (desired == "Run")
                animator.speed = state.Slow > 0 ? 1.2f : accelerating ? 2.8f : 2.2f;
            else if (desired == "Finish") animator.speed = reducedMotion ? 0.75f : 1;
            if (powerLayer >= 0)
            {
                float target = phase == RacePhase.Running && state.FinishTime < 0 && Time.time - powerStarted < 0.53f ? 1 : 0;
                animator.SetLayerWeight(powerLayer, Mathf.MoveTowards(animator.GetLayerWeight(powerLayer),
                    target, Time.deltaTime * 12));
                animator.SetFloat("PowerTempo", 1 / Mathf.Max(0.1f, animator.speed));
            }
            if (skin != null && blinkLeft >= 0 && blinkRight >= 0)
            {
                float phaseTime = (Time.time + blinkOffset) % 4.1f;
                float blink = Mathf.Clamp01(1 - Mathf.Abs(phaseTime - 3.9f) / 0.12f) * 100;
                float wink = state.FinishTime >= 0 && Mathf.Sin((Time.time - finishStarted) * 3) > 0.88f ? 80 : 0;
                skin.SetBlendShapeWeight(blinkLeft, blink);
                skin.SetBlendShapeWeight(blinkRight, Mathf.Max(blink, wink));
            }

            previousHeight = state.Height;
            previousSlow = state.Slow;
            previousCooldown = state.ItemCooldown;
            previousCastTime = state.CastTime;
        }
    }
}


