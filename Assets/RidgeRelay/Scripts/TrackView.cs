using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RidgeRelay
{
    public readonly struct TrackPose
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly Vector3 Up;
        public readonly Vector3 Right;

        public TrackPose(Vector3 position, Vector3 forward, Vector3 up, Vector3 right)
        {
            Position = position;
            Forward = forward;
            Up = up;
            Right = right;
        }

        public Vector3 Offset(float across, float up, float along = 0) =>
            Position + Right * across + Up * up + Forward * along;
    }

    public sealed class TrackView : MonoBehaviour
    {
        private const float Start = -14;
        private const float End = RaceSimulation.CourseLength + 24;
        private const float Spacing = 0.5f;
        private const float DeckHalfWidth = RaceSimulation.LaneWidth * 1.5f;
        private const float ChunkLength = 48;
        private static readonly TrackPose[] Path = CreatePath();
        private readonly Dictionary<int, Transform> pickups = new Dictionary<int, Transform>();
        private readonly List<Transform> traps = new List<Transform>();
        private readonly List<Transform> sails = new List<Transform>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private Material stone, sandstone, inset, bright, rail, teal, metal, hazard, water, ripple;
        private Material redCliff, mauveCliff, sandCliff, green, timber, fabric, boost, pad, shield;
        private bool built;

        public static TrackPose Sample(float distance, float lateral = 0)
        {
            float sample = Mathf.Clamp((distance - Start) / Spacing, 0, Path.Length - 1);
            int index = Mathf.Min(Mathf.FloorToInt(sample), Path.Length - 2);
            float t = sample - index;
            TrackPose a = Path[index];
            TrackPose b = Path[index + 1];
            Vector3 forward = Vector3.Lerp(a.Forward, b.Forward, t).normalized;
            Vector3 up = Vector3.Lerp(a.Up, b.Up, t).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 position = Vector3.Lerp(a.Position, b.Position, t) + right * (lateral * RaceSimulation.LaneWidth);
            return new TrackPose(position, forward, up, right);
        }

        private static TrackPose[] CreatePath()
        {
            var result = new TrackPose[Mathf.RoundToInt((End - Start) / Spacing) + 1];
            Vector3 position = new Vector3(0, 0, Start);
            for (int i = 0; i < result.Length; i++)
            {
                float s = Start + i * Spacing;
                Vector3 forward = Tangent(s);
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float curvature = (Heading(s + 1) - Heading(s - 1)) * 0.5f;
                float bank = Mathf.Clamp(curvature * 390, -11, 11);
                right = Quaternion.AngleAxis(-bank, forward) * right;
                Vector3 up = Vector3.Cross(forward, right).normalized;
                result[i] = new TrackPose(position, forward, up, right);
                position += Tangent(s + Spacing / 2) * Spacing;
            }

            return result;
        }

        private static Vector3 Tangent(float s)
        {
            float yaw = Heading(s);
            float pitch = 0.115f * Mathf.Sin(s * 0.047f) + 0.085f * Mathf.Sin(s * 0.098f);
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        private static float Heading(float s)
        {
            float entry = Mathf.SmoothStep(0, 1, (s - 26) / 52);
            return entry * (0.67f * Mathf.Sin((s - 41) * 0.029f) + 0.24f * Mathf.Sin(s * 0.057f));
        }

        public void Build()
        {
            if (built) return;
            built = true;
            CreatePalette();
            BuildCourse();
            BuildEnvironment();
            BuildFeatures();
        }

        private void CreatePalette()
        {
            stone = MaterialFor("Chalk sandstone", 0xd8a680);
            sandstone = MaterialFor("Warm paving", 0xeac39b);
            inset = MaterialFor("Sandstone seams", 0x9c654f);
            bright = MaterialFor("Cream paint", 0xffe8bd);
            rail = MaterialFor("Sandstone coping", 0xbd8360);
            teal = MaterialFor("Teal enamel", 0x208f88);
            metal = MaterialFor("Deep teal fittings", 0x194c57);
            hazard = MaterialFor("Coral hazard", 0xe26142);
            water = MaterialFor("Turquoise water", 0x318b94);
            ripple = MaterialFor("Quiet shallows", 0x4ba8a8);
            redCliff = MaterialFor("Iron sandstone", 0xa25e59);
            mauveCliff = MaterialFor("Lavender rock", 0x895f77);
            sandCliff = MaterialFor("Pale cliff", 0xd99972);
            green = MaterialFor("Canyon scrub", 0x647c67);
            timber = MaterialFor("Ochre wood", 0x7c5947);
            fabric = MaterialFor("Windmill sails", 0xf4d2a1);
            boost = MaterialFor("Tailwind turquoise", 0x11c7bf, true);
            pad = MaterialFor("Bristle orange", 0xff7940, true);
            shield = MaterialFor("Shell purple", 0xad79ec, true);
        }

        private Material MaterialFor(string materialName, int packedColor, bool emission = false)
        {
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            var color = new Color32((byte)(packedColor >> 16), (byte)(packedColor >> 8), (byte)packedColor, 255);
            var material = new Material(shader) { name = materialName, color = color };
            material.SetFloat("_Smoothness", 0.08f);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", (Color)color * 0.42f);
            }

            ownedMaterials.Add(material);
            return material;
        }

        private void BuildCourse()
        {
            var cuts = new SortedSet<float>();
            for (float s = Start; s <= End; s += 2) cuts.Add(s);
            for (float s = Start + ChunkLength; s < End; s += ChunkLength) cuts.Add(s);
            cuts.Add(End);
            cuts.Add(0);
            cuts.Add(RaceSimulation.CourseLength);
            foreach (CourseFeature feature in RaceSimulation.Features)
            {
                if (!feature.Gap) continue;
                cuts.Add(feature.Start);
                cuts.Add(feature.End);
            }

            using var sections = cuts.GetEnumerator();
            sections.MoveNext();
            float a = sections.Current;
            Batch chunk = new Batch();
            float chunkEnd = Start + ChunkLength;
            int chunkNumber = 0;
            while (sections.MoveNext())
            {
                float b = sections.Current;
                BuildDeckSection(chunk, a, b);
                if (b >= chunkEnd || b == End)
                {
                    Emit($"Deck {++chunkNumber:00}", chunk);
                    chunk = new Batch();
                    chunkEnd += ChunkLength;
                }

                a = b;
            }

            for (float from = -3; from < End - 5; from += 54)
            {
                var pier = new Batch();
                for (float s = from; s < Mathf.Min(from + 54, End - 5); s += 9)
                {
                    TrackPose pose = Sample(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (GapAt(s, side)) continue;
                        Vector3 top = pose.Offset(2.12f * side, -0.42f);
                        float height = top.y + 10.5f;
                        AddWorldBox(pier, stone, top - Vector3.up * (height * 0.5f), pose.Forward, 0.85f, height, 1.2f);
                        AddWorldBox(pier, rail, top - Vector3.up * 0.18f, pose.Forward, 1.3f, 0.36f, 1.55f);
                    }
                }

                Emit("Bridge supports", pier);
            }

            GameObject windGate = Resources.Load<GameObject>("RidgeRelay/Environment/WindGate");
            AddGate(0, true);
            foreach (float checkpoint in RaceSimulation.Checkpoints)
                if (checkpoint > 0) AddGate(checkpoint, false, windGate);
            AddGate(RaceSimulation.CourseLength, true);
        }

        private void BuildDeckSection(Batch batch, float a, float b)
        {
            for (int lane = -1; lane <= 1; lane++)
            {
                float left = (lane - 0.5f) * RaceSimulation.LaneWidth;
                float right = (lane + 0.5f) * RaceSimulation.LaneWidth;
                if (GapAt((a + b) * 0.5f, lane))
                {
                    if (!GapAt(a - 0.001f, lane)) Strip(batch, hazard, a - 0.28f, a, left, right, 0.026f);
                    if (!GapAt(b + 0.001f, lane)) Strip(batch, hazard, b, b + 0.28f, left, right, 0.026f);
                    continue;
                }

                Material paving = Mathf.FloorToInt((a + 14) * 0.5f + lane) % 3 == 0 ? stone : sandstone;
                Strip(batch, paving, a, b, left, right, 0);
                if (lane < 1 && !GapAt((a + b) * 0.5f, lane + 1))
                {
                    Strip(batch, inset, a, b, right - 0.023f, right + 0.023f, 0.008f);
                    if (Mathf.FloorToInt(a + 14) % 4 < 2)
                        Strip(batch, bright, a + 0.06f, b - 0.06f, right - 0.055f, right + 0.055f, 0.012f);
                }

                if (b - a > 0.3f)
                    Strip(batch, inset, a + 0.015f, a + 0.034f, left + 0.02f, right - 0.02f, 0.009f);
                if (lane == -1 || GapAt((a + b) * 0.5f, lane - 1))
                    AddDeckWall(batch, stone, a, b, left, true);
                if (lane == 1 || GapAt((a + b) * 0.5f, lane + 1))
                    AddDeckWall(batch, stone, a, b, right, false);

                if (GapAt(a - 0.001f, lane)) AddDeckEnd(batch, rail, a, left, right, true);
                if (GapAt(b + 0.001f, lane)) AddDeckEnd(batch, rail, b, left, right, false);

                if (lane == 0) continue;
                float edge = lane * DeckHalfWidth;
                float inner = edge - lane * 0.24f;
                Strip(batch, rail, a, b, Mathf.Min(inner, edge), Mathf.Max(inner, edge), 0.155f);
                Strip(batch, teal, a, b, Mathf.Min(inner, edge), Mathf.Max(inner, edge), 0.166f);
                AddDeckWall(batch, rail, a, b, inner, lane > 0, 0, 0.155f);
                AddDeckWall(batch, rail, a, b, edge, lane < 0, 0, 0.155f);
                if (Mathf.FloorToInt(a + 14) % 6 == 0)
                {
                    TrackPose p = Sample(a + 0.08f);
                    AddBox(batch, rail, p, edge - lane * 0.12f, 0.20f, 0, 0.3f, 0.4f, 0.3f);
                    AddBox(batch, bright, p, edge - lane * 0.12f, 0.42f, 0, 0.34f, 0.07f, 0.34f);
                }
            }
        }

        private static bool GapAt(float distance, int lane)
        {
            foreach (CourseFeature feature in RaceSimulation.Features)
                if (feature.Gap && feature.Lane == lane && distance >= feature.Start && distance < feature.End)
                    return true;
            return false;
        }

        private static void Strip(Batch batch, Material material, float from, float to, float left, float right, float y)
        {
            if (to <= from) return;
            TrackPose a = Sample(from);
            TrackPose b = Sample(to);
            batch.Quad(material, a.Offset(left, y), b.Offset(left, y), b.Offset(right, y), a.Offset(right, y));
        }

        private static void AddDeckWall(Batch batch, Material material, float from, float to, float side,
            bool facesLeft, float bottom = -0.4f, float top = 0)
        {
            TrackPose a = Sample(from);
            TrackPose b = Sample(to);
            Vector3 p = a.Offset(side, top);
            Vector3 q = a.Offset(side, bottom);
            Vector3 r = b.Offset(side, bottom);
            Vector3 s = b.Offset(side, top);
            if (facesLeft) batch.Quad(material, p, q, r, s);
            else batch.Quad(material, s, r, q, p);
        }

        private static void AddDeckEnd(Batch batch, Material material, float distance, float left, float right, bool facesBack)
        {
            TrackPose pose = Sample(distance);
            Vector3 a = pose.Offset(left, 0);
            Vector3 b = pose.Offset(right, 0);
            Vector3 c = pose.Offset(right, -0.4f);
            Vector3 d = pose.Offset(left, -0.4f);
            if (facesBack) batch.Quad(material, a, b, c, d);
            else batch.Quad(material, d, c, b, a);
        }

        private void AddGate(float s, bool major, GameObject model = null)
        {
            var gate = new Batch();
            TrackPose p = Sample(s);
            Strip(gate, bright, s - 0.16f, s + 0.16f, -DeckHalfWidth, DeckHalfWidth, 0.024f);
            if (model != null)
            {
                Instantiate(model, p.Position, Quaternion.LookRotation(-p.Forward, p.Up), transform);
                Emit("Checkpoint line", gate);
                return;
            }

            float height = major ? 4.6f : 3.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 3.29f;
                AddBox(gate, rail, p, x, 0.46f, 0, 0.7f, 0.92f, 0.8f);
                AddBox(gate, teal, p, x, height * 0.5f, 0, 0.32f, height, 0.42f);
                AddBox(gate, bright, p, x, height, 0, 0.64f, 0.25f, 0.64f);
            }

            AddBox(gate, teal, p, 0, height, 0, 6.7f, 0.2f, 0.29f);
            if (major)
            {
                AddBox(gate, metal, p, 0, height + 0.46f, 0, 3.85f, 0.76f, 0.22f);
                for (int column = 0; column < 12; column++)
                for (int row = 0; row < 2; row++)
                {
                    if ((column + row) % 2 == 0)
                        AddBox(gate, bright, p, (column - 5.5f) * 0.31f, height + 0.29f + row * 0.31f,
                            -0.13f, 0.31f, 0.31f, 0.035f);
                }
            }

            Emit(major ? (s == 0 ? "Starting gate" : "Finish gate") : "Checkpoint gate", gate);
        }

        private void BuildEnvironment()
        {
            var canyon = new Batch();
            canyon.Quad(water, new Vector3(-240, -8.7f, -55), new Vector3(-240, -8.7f, 780),
                new Vector3(240, -8.7f, 780), new Vector3(240, -8.7f, -55));
            Emit("Gorge water", canyon);

            var random = new System.Random(8721);
            GameObject cliffModel = Resources.Load<GameObject>("RidgeRelay/Environment/Cliff");
            GameObject millModel = Resources.Load<GameObject>("RidgeRelay/Environment/Windmill");
            for (float segment = -9; segment < End; segment += ChunkLength)
            {
                var sector = new Batch();
                for (float s = segment + 2; s < Mathf.Min(segment + ChunkLength, End); s += 7)
                {
                    TrackPose p = Sample(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * (8 + Range(random, 0, 11));
                        Vector3 basePoint = p.Offset(x, 0, Range(random, -3, 3));
                        basePoint.y = -9.8f;
                        float width = Range(random, 5, 10);
                        float height = Range(random, 7, 13);
                        AddRock(sector, sandCliff, redCliff, basePoint, width, height, random);
                        if (cliffModel != null && ((int)s + side) % 3 == 0)
                        {
                            var cliff = Instantiate(cliffModel, basePoint + Vector3.up * (height - 1.7f),
                                Quaternion.Euler(0, Range(random, 0, 360), 0), transform);
                            cliff.transform.localScale = Vector3.one * Range(random, 0.55f, 0.9f);
                        }

                        Vector3 backdrop = p.Offset(side * Range(random, 23, 40), 0, Range(random, -7, 7));
                        backdrop.y = -10;
                        AddRock(sector, mauveCliff, redCliff, backdrop, Range(random, 11, 19), Range(random, 15, 23), random);

                        Vector3 splash = basePoint + p.Right * (-side * (width * 0.45f + 0.32f));
                        splash.y = -8.68f;
                        AddWorldBox(sector, ripple, splash, p.Forward, Range(random, 2, 4), 0.015f, Range(random, 0.3f, 1.1f));

                        Vector3 scrub = basePoint + Vector3.up * height;
                        for (int sprout = 0; sprout < 3; sprout++)
                        {
                            var root = scrub + new Vector3(Range(random, -1, 1), -0.1f, Range(random, -1, 1));
                            AddRock(sector, green, green, root, Range(random, 0.4f, 0.9f), Range(random, 0.35f, 0.85f), random);
                        }
                    }
                }

                Emit("Canyon cliffs", sector);
            }

            for (int i = 0; i < 7; i++)
            {
                TrackPose p = Sample(14 + 93 * i);
                Vector3 building = p.Offset(i % 2 == 0 ? -7.3f : 7.3f, -0.4f);
                AddWindmill(building, p.Forward, millModel);
            }
        }

        private void AddWindmill(Vector3 origin, Vector3 forward, GameObject model)
        {
            var random = new System.Random(193);
            var baseMesh = new Batch();
            Vector3 foundation = new Vector3(origin.x, -9.8f, origin.z);
            AddRock(baseMesh, stone, redCliff, foundation, 5.25f, origin.y - foundation.y, random);
            Emit("Windmill foundation", baseMesh);
            if (model != null)
            {
                var imported = Instantiate(model, origin, Quaternion.LookRotation(-forward, Vector3.up), transform);
                Transform rotor = FindChild(imported.transform, "WindmillRotor");
                if (rotor != null) sails.Add(rotor);
                return;
            }

            var building = new Batch();
            TrackPose p = LevelPose(origin, forward);
            AddBox(building, stone, p, 0, 1.22f, 0, 1.8f, 2.44f, 1.5f);
            AddBox(building, timber, p, 0, 2.55f, -0.02f, 1.98f, 0.3f, 1.72f);
            AddBox(building, teal, p, 0.38f, 0.54f, -0.77f, 0.42f, 1.08f, 0.08f);
            Emit("Windmill tower", building);

            var rotorObject = new GameObject("Rotor");
            rotorObject.transform.SetParent(transform);
            rotorObject.transform.SetPositionAndRotation(p.Offset(0, 2.2f, -0.94f),
                Quaternion.LookRotation(p.Forward, Vector3.up));
            sails.Add(rotorObject.transform);
            var blades = new Batch();
            for (int index = 0; index < 4; index++)
            {
                Quaternion rotation = Quaternion.AngleAxis(index * 90, Vector3.forward);
                TrackPose blade = new TrackPose(Vector3.zero, Vector3.forward, rotation * Vector3.up, rotation * Vector3.right);
                AddBox(blades, timber, blade, 0, 0.86f, 0, 0.10f, 1.73f, 0.08f);
                AddBox(blades, fabric, blade, 0.24f, 1.15f, 0.045f, 0.42f, 0.94f, 0.045f);
            }

            AddBox(blades, rail, LevelPose(Vector3.zero, Vector3.forward), 0, 0, 0, 0.28f, 0.28f, 0.17f);
            Emit("Sails", blades, rotorObject.transform);
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (root.name == childName) return root;
            foreach (Transform child in root)
            {
                Transform result = FindChild(child, childName);
                if (result != null) return result;
            }

            return null;
        }

        private static float Range(System.Random random, float min, float max) =>
            Mathf.Lerp(min, max, (float)random.NextDouble());

        private static void AddRock(Batch batch, Material cap, Material wall, Vector3 root,
            float width, float height, System.Random random)
        {
            const int sides = 6;
            var baseRing = new Vector3[sides];
            var shoulder = new Vector3[sides];
            var topRing = new Vector3[sides];
            float rotation = Range(random, 0, Mathf.PI * 2);
            for (int i = 0; i < sides; i++)
            {
                float angle = rotation + Mathf.PI * 2 * i / sides;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float radius = width * Range(random, 0.42f, 0.58f);
                baseRing[i] = root + direction * radius;
                shoulder[i] = root + direction * (radius * 0.91f) + Vector3.up * (height * 0.76f);
                topRing[i] = root + direction * (radius * 0.63f) + Vector3.up * height * Range(random, 0.92f, 1);
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                batch.Quad(wall, baseRing[i], shoulder[i], shoulder[next], baseRing[next]);
                batch.Quad(cap, shoulder[i], topRing[i], topRing[next], shoulder[next]);
                batch.Triangle(cap, root + Vector3.up * height, topRing[next], topRing[i]);
            }
        }

        private void BuildFeatures()
        {
            GameObject chevrons = Resources.Load<GameObject>("RidgeRelay/Environment/Chevrons");
            GameObject[] gadgetIcons =
            {
                null,
                Resources.Load<GameObject>("RidgeRelay/Environment/PickupTailwind"),
                Resources.Load<GameObject>("RidgeRelay/Environment/PickupBristlePad"),
                Resources.Load<GameObject>("RidgeRelay/Environment/PickupShellFilm")
            };
            for (int i = 0; i < RaceSimulation.Features.Count; i++)
            {
                CourseFeature feature = RaceSimulation.Features[i];
                float across = feature.Lane * RaceSimulation.LaneWidth;
                if (feature.Item != Gadget.None)
                {
                    var ring = new Batch();
                    TrackPose pick = Sample(feature.Start + 0.5f, feature.Lane);
                    AddBox(ring, metal, pick, 0, 0.055f, 0, 0.94f, 0.11f, 0.98f);
                    AddBox(ring, GadgetMaterial(feature.Item), pick, 0, 0.117f, 0, 0.74f, 0.02f, 0.78f);
                    Emit("Supply station", ring);
                    var pickup = new GameObject(feature.Item.ToString());
                    pickup.transform.SetParent(transform);
                    pickup.transform.position = pick.Offset(0, 0.8f);
                    pickups.Add(i, pickup.transform);
                    GameObject icon = gadgetIcons[(int)feature.Item];
                    if (icon != null) Instantiate(icon, pickup.transform, false);
                    else BuildGadgetIcon(feature.Item, pickup.transform);
                    continue;
                }

                var warning = new Batch();
                TrackPose pose = Sample(feature.Start - (feature.Gap ? 6 : 3.75f), feature.Lane);
                for (int line = 0; line < 2; line++)
                {
                    Vector3 middle = pose.Offset(0, 0.029f, line * (feature.Gap ? 1.7f : 0.65f));
                    Vector3 left = middle - pose.Right * 0.64f;
                    Vector3 right = middle + pose.Right * 0.64f;
                    Vector3 tip = middle + pose.Forward * 0.54f;
                    Vector3 notch = middle + pose.Forward * 0.31f;
                    warning.Triangle(hazard, left, tip, notch);
                    warning.Triangle(hazard, right, notch, tip);
                }

                if (feature.Gap)
                {
                    TrackPose end = Sample(feature.End, feature.Lane);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        AddBox(warning, teal, end, side * 0.82f, 0.35f, 0.15f, 0.12f, 0.7f, 0.12f);
                        AddBox(warning, bright, end, side * 0.82f, 0.75f, 0.15f, 0.22f, 0.14f, 0.22f);
                    }

                    Strip(warning, teal, feature.End + 0.05f, feature.End + 0.4f, across - 0.73f, across + 0.73f, 0.031f);
                }
                else
                {
                    TrackPose hurdle = Sample(feature.Start + 0.2f, feature.Lane);
                    AddBox(warning, hazard, hurdle, 0, 0.33f, 0, 1.62f, 0.58f, 0.31f);
                    for (int slat = -2; slat <= 2; slat += 2)
                        AddBox(warning, bright, hurdle, 0.27f * slat, 0.34f, -0.162f, 0.17f, 0.32f, 0.016f);
                }

                Emit(feature.Gap ? "Jump markings" : "Lane hurdle", warning);
                if (chevrons != null && feature.Gap)
                {
                    TrackPose sign = Sample(feature.Start - 6);
                    int side = feature.Lane == 0 ? 1 : feature.Lane;
                    Instantiate(chevrons, sign.Offset(side * 3.35f, 0.04f),
                        Quaternion.LookRotation(-sign.Forward, sign.Up), transform).transform.localScale = Vector3.one * 0.65f;
                }
            }
        }

        private Material GadgetMaterial(Gadget gadget)
        {
            switch (gadget)
            {
                case Gadget.Tailwind: return boost;
                case Gadget.BristlePad: return pad;
                default: return shield;
            }
        }

        private void BuildGadgetIcon(Gadget gadget, Transform parent)
        {
            var batch = new Batch();
            TrackPose pose = LevelPose(Vector3.zero, Vector3.forward);
            Material color = GadgetMaterial(gadget);
            if (gadget == Gadget.Tailwind)
            {
                for (int i = 0; i < 3; i++)
                    AddBox(batch, color, pose, 0, -0.21f + i * 0.20f, 0, 0.7f - i * 0.18f, 0.14f, 0.37f);
            }
            else if (gadget == Gadget.BristlePad)
            {
                AddBox(batch, color, pose, 0, -0.17f, 0, 0.62f, 0.18f, 0.55f);
                for (int i = -1; i <= 1; i++)
                    AddBox(batch, color, pose, i * 0.18f, 0.055f, 0, 0.12f, 0.3f, 0.34f);
            }
            else
            {
                AddBox(batch, color, pose, 0, 0.03f, 0, 0.60f, 0.47f, 0.37f);
                AddBox(batch, color, pose, 0, -0.23f, 0, 0.36f, 0.18f, 0.31f);
            }

            Emit("Gadget icon", batch, parent);
        }

        public void UpdateVisuals(RaceSimulation race)
        {
            if (!built) Build();
            float clock = Mathf.Max(0, race.Clock);
            foreach (var pickup in pickups)
            {
                bool ready = race.IsPickupReady(pickup.Key);
                if (pickup.Value.gameObject.activeSelf != ready) pickup.Value.gameObject.SetActive(ready);
                if (!ready) continue;
                CourseFeature feature = RaceSimulation.Features[pickup.Key];
                TrackPose pose = Sample(feature.Start + 0.5f, feature.Lane);
                pickup.Value.SetPositionAndRotation(pose.Offset(0, 0.87f + Mathf.Sin(clock * 3.2f + pickup.Key) * 0.12f),
                    Quaternion.AngleAxis(clock * 74, Vector3.up));
            }

            for (int i = 0; i < sails.Count; i++)
                sails[i].Rotate(Vector3.forward, Time.deltaTime * (19 + i % 3 * 6), Space.Self);

            for (int i = 0; i < race.Traps.Count; i++)
            {
                if (i == traps.Count) traps.Add(CreateTrap());
                Transform view = traps[i];
                RaceSimulation.Trap trap = race.Traps[i];
                TrackPose pose = Sample(trap.Distance, trap.Lane);
                view.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, pose.Up));
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            }

            for (int i = race.Traps.Count; i < traps.Count; i++)
                if (traps[i].gameObject.activeSelf) traps[i].gameObject.SetActive(false);
        }

        private Transform CreateTrap()
        {
            var batch = new Batch();
            TrackPose pose = LevelPose(Vector3.zero, Vector3.forward);
            AddBox(batch, metal, pose, 0, 0.055f, 0, 1.42f, 0.11f, 0.76f);
            for (int i = -2; i <= 2; i++)
                AddBox(batch, pad, pose, i * 0.24f, 0.15f, 0, 0.15f, 0.24f, 0.63f);
            return Emit("Bristle patch", batch);
        }

        private static TrackPose LevelPose(Vector3 origin, Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            return new TrackPose(origin, flat, Vector3.up, Vector3.Cross(Vector3.up, flat));
        }

        private static void AddWorldBox(Batch batch, Material material, Vector3 position, Vector3 forward,
            float width, float height, float length)
        {
            AddBox(batch, material, LevelPose(position, forward), 0, 0, 0, width, height, length);
        }

        private static void AddBox(Batch batch, Material material, TrackPose pose,
            float across, float up, float along, float width, float height, float length)
        {
            float x = width * 0.5f;
            float y = height * 0.5f;
            float z = length * 0.5f;
            Vector3 a = pose.Offset(across - x, up - y, along - z);
            Vector3 b = pose.Offset(across + x, up - y, along - z);
            Vector3 c = pose.Offset(across + x, up - y, along + z);
            Vector3 d = pose.Offset(across - x, up - y, along + z);
            Vector3 e = pose.Offset(across - x, up + y, along - z);
            Vector3 f = pose.Offset(across + x, up + y, along - z);
            Vector3 g = pose.Offset(across + x, up + y, along + z);
            Vector3 h = pose.Offset(across - x, up + y, along + z);
            batch.Quad(material, a, b, c, d);
            batch.Quad(material, e, h, g, f);
            batch.Quad(material, a, e, f, b);
            batch.Quad(material, b, f, g, c);
            batch.Quad(material, c, g, h, d);
            batch.Quad(material, d, h, e, a);
        }

        private Transform Emit(string objectName, Batch batch, Transform parent = null)
        {
            var instance = new GameObject(objectName);
            instance.transform.SetParent(parent == null ? transform : parent, false);
            var geometry = new Mesh { name = objectName };
            if (batch.Vertices.Count > 65535)
                geometry.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            geometry.SetVertices(batch.Vertices);
            geometry.subMeshCount = batch.Materials.Count;
            for (int i = 0; i < batch.Materials.Count; i++)
                geometry.SetTriangles(batch.Indices[i], i);
            geometry.RecalculateNormals();
            geometry.RecalculateBounds();
            instance.AddComponent<MeshFilter>().sharedMesh = geometry;
            instance.AddComponent<MeshRenderer>().sharedMaterials = batch.Materials.ToArray();
            ownedMeshes.Add(geometry);
            return instance.transform;
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in ownedMeshes) Destroy(mesh);
            foreach (Material material in ownedMaterials) Destroy(material);
        }

        private sealed class Batch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<List<int>> Indices = new List<List<int>>();

            private List<int> IndicesFor(Material material)
            {
                int i = Materials.IndexOf(material);
                if (i >= 0) return Indices[i];
                var indices = new List<int>();
                Materials.Add(material);
                Indices.Add(indices);
                return indices;
            }

            public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                List<int> indices = IndicesFor(material);
                int start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Vertices.Add(d);
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 3);
            }

            public void Triangle(Material material, Vector3 a, Vector3 b, Vector3 c)
            {
                List<int> indices = IndicesFor(material);
                int start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
            }
        }
    }
}


