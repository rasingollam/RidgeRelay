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
        private Material magnet, rocket, shock;
        private Mesh magnetField, shockField;
        private Material glowLine, glowMote, smoke;
        private bool initialized;
        private IReadOnlyList<Vector2> runnerOffsets;

        public void Present(RaceSimulation race, int localSlot, bool reducedMotion, IReadOnlyList<Vector2> presentationOffsets = null)
        {
            runnerOffsets = presentationOffsets;
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
                TrackPose pose = PresentationPose(state.Distance, state.Lateral, i);
                PresentWarning(view, state, visible, i == localSlot, reducedMotion);
                PresentShield(view, state, pose, visible, reducedMotion, time);
                PresentWind(view, state, i, visible, reducedMotion, time);
                PresentPower(view, race, state, i, pose, visible, race.HasIncomingMagnet(i), reducedMotion);
                PresentLinks(view, race, i, visible, reducedMotion, time, runnerOffsets);
                PresentResult(view, race, state, visible, reducedMotion, time);
            }

            // Read all previous owners before advancing any snapshots.
            for (int i = 0; i < runners.Length; i++) runners[i].Remember(race.Runners[i]);
        }

        private static Vector2 OffsetFor(IReadOnlyList<Vector2> offsets, int slot) =>
            offsets != null && slot >= 0 && slot < offsets.Count ? offsets[slot] : Vector2.zero;

        private TrackPose PresentationPose(float distance, float lateral, int slot)
        {
            TrackPose pose = TrackView.Sample(distance, lateral);
            Vector2 offset = OffsetFor(runnerOffsets, slot);
            return new TrackPose(pose.Offset(offset.x, 0, offset.y), pose.Forward, pose.Up, pose.Right);
        }

        private void PresentPower(RunnerEffects view, RaceSimulation race, RunnerState state, int slot,
            TrackPose pose, bool visible, bool magnetThreat, bool reducedMotion)
        {
            Gadget cast = state.CastPower;
            bool shockCast = cast == Gadget.Shock && state.CastTime > 0;
            bool field = visible && (shockCast || magnetThreat || (cast == Gadget.Magnet && state.CastTime > 0));
            view.Field.enabled = field;
            view.Range.enabled = field;
            if (field)
            {
                float radius = shockCast ? RaceSimulation.ShockRadius : 1.8f;
                float charge = 1 - Mathf.Clamp01(state.CastTime / RaceSimulation.PowerWindup);
                view.Field.sharedMaterial = shockCast ? shock : magnet;
                view.FieldMesh.sharedMesh = shockCast ? shockField : magnetField;
                view.Field.transform.SetPositionAndRotation(pose.Offset(0, 0.16f),
                    Quaternion.LookRotation(pose.Forward, pose.Up));
                view.Field.transform.localScale = new Vector3(radius, reducedMotion ? 0.25f : 0.7f + charge, radius);
                if (shockCast) view.Field.transform.localScale = Vector3.one * 0.7f;
                view.Range.startColor = view.Range.endColor = shockCast
                    ? new Color(0.12f,0.7f,1,0.85f) : new Color(1,0.12f,0.75f,0.8f);
                for(int point=0;point<view.Range.positionCount;point++)
                {
                    float angle=point*Mathf.PI*2/(view.Range.positionCount-1);
                    TrackPose boundary=TrackView.Sample(state.Distance+Mathf.Cos(angle)*radius,
                        state.Lateral+Mathf.Sin(angle)*radius/RaceSimulation.LaneWidth);
                    view.Range.SetPosition(point,boundary.Offset(0,0.09f));
                }
            }

            bool incomingRocket = visible && state.IncomingPower == Gadget.Rocket && state.PowerWarning > 0 &&
                state.PowerOwner >= 0 && state.PowerOwner < race.Runners.Count;
            view.Rocket.enabled = incomingRocket && view.RocketModel == null;
            if (view.RocketModel != null) view.RocketModel.SetActive(incomingRocket);
            view.RocketTrail.emitting = incomingRocket && !reducedMotion;
            view.RocketTrail.enabled = !reducedMotion;
            view.Target.enabled = incomingRocket;
            if (incomingRocket)
            {
                RunnerState caster = race.Runners[state.PowerOwner];
                float progress = 1 - Mathf.Clamp01(state.PowerWarning / RaceSimulation.RocketWindup);
                // The projectile follows the course even on long shots around bends.
                TrackPose flight = TrackView.Sample(Mathf.Lerp(caster.Distance, state.Distance, progress),
                    Mathf.Lerp(caster.Lateral, state.Lateral, progress));
                TrackPose landing = PresentationPose(state.Distance + 0.8f, state.Lateral, slot);
                Vector2 flightOffset = Vector2.Lerp(OffsetFor(runnerOffsets, state.PowerOwner), OffsetFor(runnerOffsets, slot), progress);
                Vector3 rocketPosition = reducedMotion ? landing.Offset(0, 1.8f) :
                    flight.Offset(flightOffset.x, 1.4f + Mathf.Sin(progress * Mathf.PI) * 0.8f +
                        Mathf.Lerp(caster.Height, state.Height, progress), flightOffset.y);
                view.Rocket.transform.SetPositionAndRotation(rocketPosition,
                    Quaternion.LookRotation(flight.Forward, flight.Up));
                if (!view.PreviousRocket)
                {
                    view.RocketTrail.Clear();
                }
                if (!reducedMotion && !view.Smoke.isPlaying) view.Smoke.Play();
                view.Target.transform.SetPositionAndRotation(landing.Offset(0, 0.08f),
                    Quaternion.LookRotation(landing.Forward, landing.Up));
            }
            if ((!incomingRocket || reducedMotion) && view.Smoke.isPlaying)
                view.Smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);

            view.PowerBurst.enabled = visible && state.ImpactTime > 0;
            if (view.PowerBurst.enabled)
            {
                float scale = reducedMotion ? 1.2f : Mathf.Lerp(2.2f, 0.8f, state.ImpactTime / 0.55f);
                view.PowerBurst.sharedMaterial = PowerMaterial(state.ImpactPower);
                view.PowerBurst.transform.SetPositionAndRotation(pose.Offset(0, 0.32f + state.Height),
                    Quaternion.LookRotation(pose.Forward, pose.Up));
                view.PowerBurst.transform.localScale = Vector3.one * scale;
            }
            if (!reducedMotion && state.ImpactTime > 0 &&
                (view.PreviousImpactTime <= 0 || state.ImpactTime > view.PreviousImpactTime + 0.05f ||
                 state.ImpactPower != view.PreviousImpactPower))
            {
                view.Sparks.transform.position = pose.Offset(0, 0.8f + state.Height);
                var main = view.Sparks.main;
                main.startColor = PowerMaterial(state.ImpactPower).color;
                main.startSpeed = state.ImpactPower == Gadget.Rocket ? 3.5f : 2;
                view.Sparks.Play();
                view.Sparks.Emit(state.ImpactPower == Gadget.Rocket ? 32 : 18);
            }
        }

        private static void PresentLinks(RunnerEffects view, RaceSimulation race, int owner,
            bool visible, bool reducedMotion, float time, IReadOnlyList<Vector2> offsets)
        {
            RunnerState caster = race.Runners[owner];
            bool magnetCast = caster.CastPower == Gadget.Magnet && caster.CastTime > 0;
            bool shockCast = caster.CastPower == Gadget.Shock && caster.CastTime > 0;
            for (int target = 0; target < view.Links.Length; target++)
            {
                LineRenderer beam = view.Links[target];
                RunnerState rival = race.Runners[target];
                beam.enabled = visible && rival.Recovery <= 0 && rival.FinishTime < 0 && owner != target &&
                    ((magnetCast && race.IsMagnetTarget(owner,target)) || (shockCast && race.IsWithinShock(owner, target)));
                if (!beam.enabled) continue;
                Color color = magnetCast ? new Color(1, 0.12f, 0.77f, 0.85f) : new Color(0.15f, 0.7f, 1, 0.9f);
                beam.startColor = beam.endColor = color;
                for (int point = 0; point < beam.positionCount; point++)
                {
                    float t = point / (float)(beam.positionCount - 1);
                    TrackPose pose = TrackView.Sample(Mathf.Lerp(caster.Distance, rival.Distance, t),
                        Mathf.Lerp(caster.Lateral, rival.Lateral, t));
                    float motion = reducedMotion ? 0 : Mathf.Sin(t * (magnetCast ? 7.5f : 120) + time * (magnetCast ? 9 : 22));
                    Vector2 separation = Vector2.Lerp(OffsetFor(offsets, owner), OffsetFor(offsets, target), t);
                    Vector3 position = pose.Offset(separation.x + motion * 0.18f * Mathf.Sin(t * Mathf.PI),
                        0.85f + Mathf.Lerp(caster.Height, rival.Height, t) +
                        Mathf.Sin(t * Mathf.PI) * (magnetCast ? 0.65f : 0.12f) + motion * 0.08f, separation.y);
                    beam.SetPosition(point, position);
                }
            }
        }

        private Material PowerMaterial(Gadget gadget) => gadget switch
        {
            Gadget.Magnet => magnet,
            Gadget.Rocket => rocket,
            Gadget.Shock => shock,
            _ => shield
        };

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
            view.Bubble.enabled = view.Shield.enabled;
            if (!view.Shield.enabled) return;
            Transform shell = view.Shield.transform;
            Quaternion rotation = Quaternion.LookRotation(pose.Forward, pose.Up);
            if (!reducedMotion) rotation *= Quaternion.AngleAxis(time * 34, Vector3.up);
            shell.SetPositionAndRotation(pose.Offset(0, 0.04f + state.Height), rotation);
            view.Bubble.transform.SetPositionAndRotation(pose.Offset(0, 0.04f + state.Height),
                Quaternion.LookRotation(pose.Forward, pose.Up));
        }

        private void PresentWind(RunnerEffects view, RunnerState state, int slot, bool visible, bool reducedMotion, float time)
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
                TrackPose pose = PresentationPose(state.Distance - offset, state.Lateral, slot);
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
            shield = CreateMaterial(shader, "Shell Film", new Color(0.2f, 0.9f, 0.7f), 0.80f);
            wind = CreateMaterial(shader, "Tailwind", new Color(0.26f, 0.92f, 1), 0.6f);
            draft = CreateMaterial(shader, "Slipstream", new Color(0.80f, 0.94f, 0.88f), 0.32f);
            shortcut = CreateMaterial(shader, "Route boost", new Color(1, 0.76f, 0.30f), 0.58f);
            magnet = CreateMaterial(shader, "Magnet field", new Color(0.97f, 0.25f, 0.94f), 1.0f);
            rocket = CreateMaterial(shader, "Rocket ember", new Color(1, 0.36f, 0.05f), 1.1f);
            shock = CreateMaterial(shader, "Shock arc", new Color(0.2f, 0.8f, 1), 1.25f);
            glowLine = new Material(Shader.Find("RidgeRelay/Power Glow")) { name = "Power ribbons" };
            glowMote = new Material(Shader.Find("RidgeRelay/Power Mote")) { name = "Power sparks" };
            smoke = new Material(Shader.Find("RidgeRelay/Soft Smoke")) { name = "Soft rocket smoke" };
            materials.Add(glowLine);
            materials.Add(glowMote);
            materials.Add(smoke);

            Mesh warningMesh = CreateWarningMesh();
            Mesh shieldMesh = CreateRingMesh("Shield rim", 1, 0.035f, false);
            Mesh bubbleMesh = CreateShieldBubble();
            var bubbleMaterial = new Material(Shader.Find("RidgeRelay/Shield Bubble")) { name = "Mint shield bubble" };
            materials.Add(bubbleMaterial);
            Mesh resultMesh = CreateRingMesh("Pulse result", 0.58f, 0.10f, false);
            Mesh trailMesh = CreateWindMesh();
            var fieldShape = new EffectMesh();
            AppendRing(fieldShape, Vector3.right, Vector3.forward, 1, 0.035f, 0, true);
            AppendRing(fieldShape, Vector3.right, Vector3.forward, 0.96f, 0.015f, 0.012f, false);
            magnetField = FinishMesh("Magnet boundary", fieldShape);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2 / 10;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(Vector3.up, radial) * 0.09f;
                Vector3 outer = radial * 0.98f;
                Vector3 inner = radial * 0.77f;
                fieldShape.Quad(outer - side, inner + side * 0.32f, inner - side * 0.48f, outer - side * 0.63f);
                fieldShape.Quad(inner - side * 0.36f, radial * 0.58f + side * 0.32f,
                    radial * 0.58f - side * 0.05f, inner + side * 0.05f);
            }
            shockField = FinishMesh("Shock boundary and lightning spokes", fieldShape);
            Mesh rocketMesh = CreateRocketMesh();
            GameObject rocketModel = Resources.Load<GameObject>("RidgeRelay/Environment/HomingRocket");
            for (int i = 0; i < runners.Length; i++)
            {
                var view = new RunnerEffects
                {
                    Shield = CreateRenderer("Shield outline", shieldMesh, shield),
                    Bubble = CreateRenderer("Protective shell", bubbleMesh, bubbleMaterial),
                    Result = CreateRenderer("Pulse result", resultMesh, impact),
                    Field = CreateRenderer("Power boundary", magnetField, magnet),
                    Rocket = CreateRenderer("Rocket projectile", rocketMesh, rocket),
                    Target = CreateRenderer("Rocket landing marker", warningMesh, rocket),
                    PowerBurst = CreateRenderer("Power impact", resultMesh, shock)
                };
                view.FieldMesh = view.Field.GetComponent<MeshFilter>();
                if (rocketModel != null)
                {
                    view.RocketModel = Instantiate(rocketModel, view.Rocket.transform, false);
                    view.RocketModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    foreach (Renderer renderer in view.RocketModel.GetComponentsInChildren<Renderer>())
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                    view.RocketModel.SetActive(false);
                }
                for (int target = 0; target < view.Links.Length; target++)
                {
                    var link = new GameObject("Power link").AddComponent<LineRenderer>();
                    link.transform.SetParent(transform, false);
                    link.sharedMaterial = glowLine;
                    link.positionCount = 36;
                    link.startWidth = 0.17f;
                    link.endWidth = 0.09f;
                    link.numCapVertices = 3;
                    link.shadowCastingMode = ShadowCastingMode.Off;
                    link.receiveShadows = false;
                    link.enabled = false;
                    view.Links[target] = link;
                }
                view.Range = new GameObject("Power range ring").AddComponent<LineRenderer>();
                view.Range.transform.SetParent(transform,false);
                view.Range.sharedMaterial=glowLine;
                view.Range.positionCount=65;
                view.Range.startWidth=view.Range.endWidth=0.12f;
                view.Range.shadowCastingMode=ShadowCastingMode.Off;
                view.Range.receiveShadows=false;
                view.Range.enabled=false;
                var trailObject = new GameObject("Rocket smoke trail");
                trailObject.transform.SetParent(view.Rocket.transform, false);
                trailObject.transform.localPosition = new Vector3(0, 0, -0.3f);
                view.RocketTrail = trailObject.AddComponent<TrailRenderer>();
                view.RocketTrail.sharedMaterial = glowLine;
                view.RocketTrail.time = 0.35f;
                view.RocketTrail.startWidth = 0.22f;
                view.RocketTrail.endWidth = 0;
                view.RocketTrail.startColor = new Color(1, 0.6f, 0.2f, 0.8f);
                view.RocketTrail.endColor = new Color(1, 0.3f, 0.06f, 0);
                view.RocketTrail.shadowCastingMode = ShadowCastingMode.Off;
                view.RocketTrail.emitting = false;
                view.Sparks = CreateSparks();
                view.Smoke = CreateSmoke(view.Rocket.transform);
                for (int stripe = 0; stripe < view.Warning.Length; stripe++)
                    view.Warning[stripe] = CreateRenderer("Warning stripe", warningMesh, warning);
                for (int strand = 0; strand < view.Wind.Length; strand++)
                    view.Wind[strand] = CreateRenderer("Wind strand", trailMesh, wind);
                runners[i] = view;
            }

            initialized = true;
            return true;
        }

        private Mesh CreateRocketMesh()
        {
            var shape = new EffectMesh();
            const int sides = 8;
            for (int i = 0; i < sides; i++)
            {
                float angleA = i * Mathf.PI * 2 / sides;
                float angleB = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 a = new Vector3(Mathf.Cos(angleA), Mathf.Sin(angleA), 0) * 0.18f;
                Vector3 b = new Vector3(Mathf.Cos(angleB), Mathf.Sin(angleB), 0) * 0.18f;
                Vector3 nose = Vector3.forward * 0.79f;
                shape.Quad(a, b, nose, nose);
                shape.Quad(b, a, a + Vector3.back * 0.56f, b + Vector3.back * 0.56f);
                Vector3 exhaust = Vector3.back * 1.9f;
                shape.Quad(a + Vector3.back * 0.56f, b + Vector3.back * 0.56f, exhaust, exhaust);
            }
            return FinishMesh("Rocket with exhaust", shape);
        }

        private Mesh CreateShieldBubble()
        {
            const int sides = 24;
            const int levels = 8;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            for (int level = 0; level <= levels; level++)
            {
                float angle = level * Mathf.PI * 0.5f / levels;
                for (int side = 0; side <= sides; side++)
                {
                    float around = side * Mathf.PI * 2 / sides;
                    vertices.Add(new Vector3(Mathf.Cos(around) * Mathf.Cos(angle),
                        Mathf.Sin(angle) * 1.8f, Mathf.Sin(around) * Mathf.Cos(angle)));
                    uvs.Add(new Vector2(side / (float)sides, level / (float)levels));
                }
            }
            for (int level = 0; level < levels; level++)
                for (int side = 0; side < sides; side++)
                {
                    int a = level * (sides + 1) + side;
                    int b = a + sides + 1;
                    indices.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                }
            var mesh = new Mesh { name = "Shield hemisphere" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            return mesh;
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

        private ParticleSystem CreateSparks()
        {
            var system = new GameObject("Power impact sparks").AddComponent<ParticleSystem>();
            system.transform.SetParent(transform, false);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = 2;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMote;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        public void SetPaused(bool paused)
        {
            if (!initialized) return;
            foreach (RunnerEffects view in runners)
            {
                if (paused && view.Sparks.isPlaying) view.Sparks.Pause();
                else if (!paused && view.Sparks.isPaused) view.Sparks.Play();
                if (paused && view.Smoke.isPlaying) view.Smoke.Pause();
                else if (!paused && view.Smoke.isPaused) view.Smoke.Play();
            }
        }

        private ParticleSystem CreateSmoke(Transform rocket)
        {
            ParticleSystem system = CreateSparks();
            system.name = "Rocket smoke";
            system.transform.SetParent(rocket, false);
            var main = system.main;
            main.loop = true;
            main.duration = 1;
            main.startColor = new Color(0.92f, 0.89f, 0.81f, 0.7f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startSpeed = 0.2f;
            main.gravityModifier = 0;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8;
            shape.radius = 0.04f;
            shape.position = new Vector3(0, 0, -0.35f);
            shape.rotation = new Vector3(0, 180, 0);
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 28;
            var size = system.sizeOverLifetime;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.35f, 1, 1.5f));
            system.GetComponent<ParticleSystemRenderer>().sharedMaterial = smoke;
            return system;
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
                view.Shield.enabled = view.Bubble.enabled = view.Result.enabled = false;
                view.Field.enabled = view.Rocket.enabled = view.Target.enabled = view.PowerBurst.enabled = false;
                view.Range.enabled=false;
                view.RocketTrail.emitting = false;
                view.RocketTrail.Clear();
                if (view.RocketModel != null) view.RocketModel.SetActive(false);
                view.Sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                view.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (LineRenderer link in view.Links) link.enabled = false;
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
            public MeshRenderer Shield, Bubble, Result;
            public MeshRenderer Field, Rocket, Target, PowerBurst;
            public MeshFilter FieldMesh;
            public readonly LineRenderer[] Links = new LineRenderer[RaceSimulation.Entrants];
            public LineRenderer Range;
            public TrailRenderer RocketTrail;
            public ParticleSystem Sparks;
            public ParticleSystem Smoke;
            public GameObject RocketModel;
            public bool PreviousRocket;
            public float PreviousImpactTime;
            public Gadget PreviousImpactPower;
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
                PreviousImpactTime = state.ImpactTime;
                PreviousImpactPower = state.ImpactPower;
                PreviousRocket = state.IncomingPower == Gadget.Rocket && state.PowerWarning > 0;
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
