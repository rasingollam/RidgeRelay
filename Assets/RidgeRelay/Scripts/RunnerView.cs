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
        private readonly Animator animator;
        private string animationState;
        private float previousHeight;
        private float previousSlow;
        private Gadget previousItem;

        public RunnerView(int index)
        {
            root = new GameObject(index == 0 ? "Player courier" : $"Courier {index + 1}").transform;
            if (Model != null)
            {
                var model = Object.Instantiate(Model, root, false);
                animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = Controller;
                animator.applyRootMotion = false;
                if (index > 0)
                    foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                        foreach (Material material in renderer.materials)
                            material.color *= TeamColors[index];
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

        public void SetPaused(bool paused)
        {
            if (animator != null) animator.speed = paused ? 0 : 1;
        }

        public void Show(RunnerState state, RacePhase phase)
        {
            var pose = TrackView.Sample(state.Distance, state.Lateral);
            root.SetPositionAndRotation(
                pose.Position + Vector3.up * state.Height,
                Quaternion.LookRotation(pose.Forward, Vector3.up));
            root.gameObject.SetActive(state.Recovery <= 0);
            if (state.Recovery > 0)
            {
                animationState = null;
                previousHeight = 0;
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

            previousHeight = state.Height;
            previousSlow = state.Slow;
            previousItem = state.Held;
        }
    }
}


