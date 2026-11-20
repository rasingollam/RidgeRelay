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
        private Gadget previousItem;
        private readonly System.Collections.Generic.List<Material> characterMaterials = new();
        private static Texture2D localPalette;
        private Texture originalPalette;
        private bool isLocal;
        private readonly TextMesh finishRank;
        private float finishStarted = -1;
        private Vector3 finishOrigin;
        private Quaternion finishHeading;

        public RunnerView(int index)
        {
            root = new GameObject(index == 0 ? "Player courier" : $"Courier {index + 1}").transform;
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
                animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = Controller;
                animator.applyRootMotion = false;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.materials)
                    {
                        if (index > 0) material.color *= TeamColors[index];
                        if (material.mainTexture == null) continue;
                        originalPalette = material.mainTexture;
                        characterMaterials.Add(material);
                    }
                if (localPalette == null && originalPalette != null)
                    localPalette = CreateLocalPalette(originalPalette);
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

        private static Texture2D CreateLocalPalette(Texture source)
        {
            // The Blender atlas puts the backpack canvas in the first cell of row two.
            var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var palette = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    name = "Local player blue backpack",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                palette.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                int cell = source.width / 4;
                for (int y = cell; y < cell * 2; y++)
                    for (int x = 0; x < cell; x++)
                        palette.SetPixel(x, y, new Color(0.12f, 0.42f, 1f));
                palette.Apply(false, true);
                return palette;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public void MarkLocal(bool local)
        {
            if (isLocal == local) return;
            isLocal = local;
            foreach (Material material in characterMaterials)
                material.mainTexture = local ? localPalette : originalPalette;
        }

        public void SetPaused(bool paused)
        {
            if (animator != null) animator.speed = paused ? 0 : 1;
        }

        public void Show(RunnerState state, RacePhase phase, bool reducedMotion)
        {
            var pose = TrackView.Sample(state.Distance, state.Lateral);
            root.SetPositionAndRotation(
                pose.Position + Vector3.up * state.Height,
                Quaternion.LookRotation(pose.Forward, Vector3.up));
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
                        celebration.Right * 0.85f - celebration.Forward * 0.53f, Vector3.up), arrival));
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
            else if (previousItem != Gadget.None && state.Held == Gadget.None) desired = "Gadget";
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
            float steering = phase == RacePhase.Running && state.FinishTime < 0
                ? Mathf.Clamp((state.Lane - state.Lateral) * 1.65f, -1, 1) : 0;
            float motion = reducedMotion ? 0.35f : 1f;
            float pitch = desired == "Run" && accelerating ? 7 * motion : 0;
            Quaternion lean = Quaternion.Euler(pitch, steering * 12 * motion, -steering * 16 * motion) *
                ImportedFacing;
            facing.localRotation = Quaternion.Slerp(facing.localRotation, lean,
                1 - Mathf.Exp(-15 * Time.deltaTime));
            if (desired == "Run")
                animator.speed = state.Slow > 0 ? 0.76f : accelerating ? 1.22f : 1;
            else if (desired == "Finish") animator.speed = reducedMotion ? 0.75f : 1;

            previousHeight = state.Height;
            previousSlow = state.Slow;
            previousItem = state.Held;
        }
    }
}


