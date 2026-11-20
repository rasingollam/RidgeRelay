using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RidgeRelay
{
    public sealed class RaceEffects : MonoBehaviour
    {
        private const int WarningTiles = 5;
        private const int WindStrands = 4;
        private const float ResultDuration = 0.52f;
        private readonly RunnerEffects[] runners = new RunnerEffects[RaceSimulation.Entrants];
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private RaceSimulation currentRace;
        private Material warning, impact, dodge, shield, wind, draft, shortcut;
        private bool initialized;

        public void Present(RaceSimulation race, int localSlot, bool reducedMotion)
        {
            if (race == null || race.Phase != RacePhase.Running)
            {
                if (currentRace != null) Clear();
                return;
            }

            if (!initialized && !CreateEffects()) return;
            if (race != currentRace)
            {
                Clear();
                currentRace = race;
                for (int i = 0; i < runners.Length; i++)
                    runners[i].Remember(race.Runners[i]);
            }

            float time = Time.unscaledTime;
            for (int i = 0; i < runners.Length; i++)
            {
                RunnerState state = race.Runners[i];
                RunnerEffects view = runners[i];
                bool visible = state.Recovery <= 0 && state.FinishTime < 0;
                TrackPose pose = TrackView.Sample(state.Distance, state.Lateral);
                PresentWarning(view, state, visible, i == localSlot, reducedMotion);
                PresentShield(view, state, pose, visible, reducedMotion, time);
                PresentWind(view, state, visible, reducedMotion, time);
                PresentResult(view, race, state, visible, reducedMotion, time);
            }

            // Read all previous owners before advancing any snapshots.
            for (int i = 0; i < runners.Length; i++) runners[i].Remember(race.Runners[i]);
        }

        private void PresentWarning(RunnerEffects view, RunnerState state, bool visible, bool local, bool reducedMotion)
        {
            bool active = visible && state.PulseWarning > 0;
            int count = local ? WarningTiles : 3;
            float urgency = 1 - Mathf.Clamp01(state.PulseWarning / RaceSimulation.PulseWindup);
            for (int i = 0; i < view.Warning.Length; i++)
            {
                MeshRenderer stripe = view.Warning[i];
                stripe.enabled = active && i < count;
                if (!stripe.enabled) continue;

                float distance = state.Distance - 0.55f + i * 1.15f;
                TrackPose pose = TrackView.Sample(distance, state.PulseLane);
                Transform symbol = stripe.transform;
                symbol.SetPositionAndRotation(pose.Offset(0, 0.055f),
                    Quaternion.LookRotation(pose.Forward, pose.Up));
                float width = reducedMotion ? 1.16f : 1.05f + 0.17f * urgency;
                float length = reducedMotion ? 0.98f : 0.84f + 0.15f * urgency;
                symbol.localScale = new Vector3(width, 1, length);
            }
        }

        private static void PresentShield(RunnerEffects view, RunnerState state, TrackPose pose,
            bool visible, bool reducedMotion, float time)
        {
            view.Shield.enabled = visible && state.Shield > 0;
            if (!view.Shield.enabled) return;
            Transform shell = view.Shield.transform;
            Quaternion rotation = Quaternion.LookRotation(pose.Forward, pose.Up);
            if (!reducedMotion) rotation *= Quaternion.AngleAxis(time * 34, Vector3.up);
            shell.SetPositionAndRotation(pose.Offset(0, 0.86f + state.Height), rotation);
        }

        private void PresentWind(RunnerEffects view, RunnerState state, bool visible, bool reducedMotion, float time)
        {
            bool powered = state.Boost > 0 || state.RouteBoost > 0;
            bool drafting = state.Slipstream >= RaceSimulation.SlipstreamActiveThreshold;
            Material style = state.Boost > 0 ? wind : state.RouteBoost > 0 ? shortcut : draft;
            int count = powered && !reducedMotion ? WindStrands : 2;
            for (int i = 0; i < view.Wind.Length; i++)
            {
                MeshRenderer strand = view.Wind[i];
                strand.enabled = visible && (powered || drafting) && i < count;
                if (!strand.enabled) continue;

                strand.sharedMaterial = style;
                float side = (i % 2 == 0 ? -1 : 1) * (i < 2 ? 0.64f : 0.45f);
                float offset = reducedMotion ? 0.52f : 0.52f + Mathf.Repeat(time * 2.5f + i * 0.41f, 1.4f);
                TrackPose pose = TrackView.Sample(state.Distance - offset, state.Lateral);
                Transform trail = strand.transform;
                trail.SetPositionAndRotation(pose.Offset(side, (i < 2 ? 0.35f : 0.64f) + state.Height * 0.72f),
                    Quaternion.LookRotation(pose.Forward, pose.Up));
                trail.localScale = new Vector3(1, 1, powered && !reducedMotion ? 1 : 0.46f);
            }
        }

        private void PresentResult(RunnerEffects view, RaceSimulation race, RunnerState state,
            bool visible, bool reducedMotion, float time)
        {
            if (state.PulseBlocks > view.PreviousBlocks)
                view.BeginResult(shield, view.PreviousLane, state.Distance, time);
            else if (state.PulseDodges > view.PreviousDodges)
                view.BeginResult(dodge, view.PreviousLane, state.Distance, time);
            else if (view.PreviousOwner >= 0 && state.PulseOwner < 0 &&
                race.Runners[view.PreviousOwner].PulseHits > runners[view.PreviousOwner].PreviousHits)
                view.BeginResult(impact, view.PreviousLane, state.Distance, time);

            float age = time - view.ResultStart;
            view.Result.enabled = visible && age < ResultDuration;
            if (!view.Result.enabled) return;

            // The ring stays in the threatened lane, so a dodge shows where the shot landed.
            TrackPose land = TrackView.Sample(view.ResultDistance, view.ResultLane);
            float scale = reducedMotion ? 0.92f : Mathf.Lerp(0.5f, 1.13f, age / ResultDuration);
            view.Result.transform.SetPositionAndRotation(land.Offset(0, 0.085f), Quaternion.LookRotation(land.Forward, land.Up));
            view.Result.transform.localScale = new Vector3(scale, 1, scale);
        }

        private bool CreateEffects()
        {
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null) return false;

            warning = CreateMaterial(shader, "Pulse warning", new Color(1, 0.26f, 0.13f), 0.65f);
            impact = CreateMaterial(shader, "Pulse hit", new Color(1, 0.38f, 0.18f), 0.70f);
            dodge = CreateMaterial(shader, "Pulse dodged", new Color(0.25f, 0.88f, 0.77f), 0.48f);
            shield = CreateMaterial(shader, "Shell Film", new Color(0.68f, 0.50f, 1), 0.50f);
            wind = CreateMaterial(shader, "Tailwind", new Color(0.26f, 0.92f, 1), 0.6f);
            draft = CreateMaterial(shader, "Slipstream", new Color(0.80f, 0.94f, 0.88f), 0.32f);
            shortcut = CreateMaterial(shader, "Route boost", new Color(1, 0.76f, 0.30f), 0.58f);

            Mesh warningMesh = CreateWarningMesh();
            Mesh shieldMesh = CreateRingMesh("Shield cage", 0.72f, 0.045f, true);
            Mesh resultMesh = CreateRingMesh("Pulse result", 0.58f, 0.10f, false);
            Mesh trailMesh = CreateWindMesh();
            for (int i = 0; i < runners.Length; i++)
            {
                var view = new RunnerEffects
                {
                    Shield = CreateRenderer("Shield outline", shieldMesh, shield),
                    Result = CreateRenderer("Pulse result", resultMesh, impact)
                };
                for (int stripe = 0; stripe < view.Warning.Length; stripe++)
                    view.Warning[stripe] = CreateRenderer("Warning stripe", warningMesh, warning);
                for (int strand = 0; strand < view.Wind.Length; strand++)
                    view.Wind[strand] = CreateRenderer("Wind strand", trailMesh, wind);
                runners[i] = view;
            }

            initialized = true;
            return true;
        }

        private Material CreateMaterial(Shader shader, string label, Color color, float emission)
        {
            var material = new Material(shader) { name = label, color = color };
            material.SetFloat("_Smoothness", 0);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            materials.Add(material);
            return material;
        }

        private MeshRenderer CreateRenderer(string label, Mesh mesh, Material material)
        {
            var item = new GameObject(label);
            item.transform.SetParent(transform, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return renderer;
        }

        private Mesh CreateWarningMesh()
        {
            var mesh = new EffectMesh();
            mesh.GroundQuad(-0.53f, -0.42f, -0.43f, 0.34f);
            mesh.GroundQuad(0.43f, -0.42f, 0.53f, 0.34f);
            mesh.Quad(new Vector3(-0.36f, 0, 0.17f), new Vector3(0, 0, -0.18f),
                new Vector3(0, 0, -0.34f), new Vector3(-0.36f, 0, 0.01f));
            mesh.Quad(new Vector3(0, 0, -0.18f), new Vector3(0.36f, 0, 0.17f),
                new Vector3(0.36f, 0, 0.01f), new Vector3(0, 0, -0.34f));
            return FinishMesh("Pulse brackets", mesh);
        }

        private Mesh CreateWindMesh()
        {
            var mesh = new EffectMesh();
            mesh.Quad(new Vector3(-0.05f, 0, 0.14f), new Vector3(0, 0.015f, -2.35f),
                new Vector3(0.05f, 0, 0.14f), new Vector3(0, 0.035f, 0.28f));
            mesh.Quad(new Vector3(0, -0.055f, 0.14f), new Vector3(0, 0.015f, -2.35f),
                new Vector3(0, 0.055f, 0.14f), new Vector3(0.035f, 0, 0.28f));
            return FinishMesh("Tapered wind", mesh);
        }

        private Mesh CreateRingMesh(string label, float radius, float width, bool cage)
        {
            var mesh = new EffectMesh();
            AppendRing(mesh, Vector3.right, Vector3.forward, radius, width, cage ? -0.42f : 0, !cage);
            if (cage)
            {
                AppendRing(mesh, Vector3.up, Vector3.right, radius, width, 0, false);
                AppendRing(mesh, Vector3.up, Vector3.forward, radius, width, 0, false);
            }
            else
            {
                // Short radial marks retain the result shape against sunlit sandstone.
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * 2 / 5;
                    Vector3 radial = Vector3.right * Mathf.Cos(angle) + Vector3.forward * Mathf.Sin(angle);
                    Vector3 tangent = Vector3.Cross(Vector3.up, radial) * 0.043f;
                    Vector3 lip = radial * (radius - 0.018f);
                    Vector3 tip = radial * (radius + 0.17f) + Vector3.up * 0.22f;
                    mesh.Quad(lip - tangent, tip - tangent * 0.38f, tip + tangent * 0.38f, lip + tangent);
                }
            }
            return FinishMesh(label, mesh);
        }

        private static void AppendRing(EffectMesh mesh, Vector3 axis1, Vector3 axis2, float radius,
            float width, float height, bool segmented)
        {
            const int sides = 22;
            for (int i = 0; i < sides; i++)
            {
                if (segmented && i % 6 >= 4) continue;
                float first = i * Mathf.PI * 2 / sides;
                float second = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 a = axis1 * Mathf.Cos(first) + axis2 * Mathf.Sin(first);
                Vector3 b = axis1 * Mathf.Cos(second) + axis2 * Mathf.Sin(second);
                Vector3 center = Vector3.up * height;
                mesh.Quad(center + a * radius, center + b * radius,
                    center + b * (radius - width), center + a * (radius - width));
            }
        }

        private Mesh FinishMesh(string label, EffectMesh source)
        {
            var mesh = new Mesh { name = label };
            mesh.SetVertices(source.Vertices);
            mesh.SetTriangles(source.Indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
        }

        private void Clear()
        {
            currentRace = null;
            foreach (RunnerEffects view in runners)
            {
                if (view == null) continue;
                view.Shield.enabled = view.Result.enabled = false;
                view.ResultStart = float.NegativeInfinity;
                foreach (MeshRenderer stripe in view.Warning) stripe.enabled = false;
                foreach (MeshRenderer strand in view.Wind) strand.enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in meshes) Destroy(mesh);
            foreach (Material material in materials) Destroy(material);
        }

        private sealed class RunnerEffects
        {
            public readonly MeshRenderer[] Warning = new MeshRenderer[WarningTiles];
            public readonly MeshRenderer[] Wind = new MeshRenderer[WindStrands];
            public MeshRenderer Shield, Result;
            public int PreviousOwner, PreviousLane;
            public int PreviousHits, PreviousDodges, PreviousBlocks;
            public int ResultLane;
            public float ResultDistance, ResultStart = float.NegativeInfinity;

            public void BeginResult(Material material, int lane, float distance, float time)
            {
                Result.sharedMaterial = material;
                ResultDistance = distance;
                ResultLane = lane;
                ResultStart = time;
            }

            public void Remember(RunnerState state)
            {
                PreviousOwner = state.PulseOwner;
                PreviousLane = state.PulseLane;
                PreviousHits = state.PulseHits;
                PreviousDodges = state.PulseDodges;
                PreviousBlocks = state.PulseBlocks;
            }
        }

        private sealed class EffectMesh
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Indices = new List<int>();

            public void GroundQuad(float left, float rear, float right, float front) =>
                Quad(new Vector3(left, 0, rear), new Vector3(left, 0, front),
                    new Vector3(right, 0, front), new Vector3(right, 0, rear));

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Vertices.Add(d);
                Indices.Add(start);
                Indices.Add(start + 1);
                Indices.Add(start + 2);
                Indices.Add(start);
                Indices.Add(start + 2);
                Indices.Add(start + 3);

                // Separate opposite faces for stable normals on both sides of the thin shapes.
                start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(d);
                Vertices.Add(c);
                Vertices.Add(b);
                Indices.Add(start);
                Indices.Add(start + 1);
                Indices.Add(start + 2);
                Indices.Add(start);
                Indices.Add(start + 2);
                Indices.Add(start + 3);
            }
        }
    }
}
