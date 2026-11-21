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
        private const float CrestStart = 340;
        private const float CrestEnd = 402;
        private const float TunnelDivider = RaceSimulation.LaneWidth * 0.5f;
        private const float TunnelDividerHalfWidth = 0.10f;
        private const float TunnelClearance = 5.6f;
        private static readonly TrackPose[] Path = CreatePath();
        private readonly Dictionary<int, Transform> pickups = new Dictionary<int, Transform>();
        private readonly List<Transform> traps = new List<Transform>();
        private readonly List<Transform> sails = new List<Transform>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private Material stone, sandstone, inset, bright, rail, teal, metal, hazard, water, ripple;
        private Material redCliff, mauveCliff, sandCliff, green, timber, fabric, guide, pad, gold, tunnelStone;
        private Material boardwalk, ridgeSlate;
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
            float across = TunnelAcross(distance, lateral * RaceSimulation.LaneWidth);
            Vector3 position = Vector3.Lerp(a.Position, b.Position, t) + right * across;
            return new TrackPose(position, forward, up, right);
        }

        public static float TunnelCameraBlend(float distance) =>
            Mathf.SmoothStep(0, 1, (distance - (RaceSimulation.TunnelLockStart - 10)) / 10) *
            (1 - Mathf.SmoothStep(0, 1, (distance - RaceSimulation.TunnelLockEnd) / 9));

        private static float TunnelAcross(float distance, float across)
        {
            float blend = Mathf.SmoothStep(0, 1, (distance - RaceSimulation.TunnelLockStart) / 4) *
                (1 - Mathf.SmoothStep(0, 1, (distance - RaceSimulation.TunnelEnd) / 4));
            float scale = across <= TunnelDivider ? 1.16f : 0.82f;
            return TunnelDivider + (across - TunnelDivider) * Mathf.Lerp(1, scale, blend);
        }

        private static Vector3 TrackPoint(float distance, float across, float height)
        {
            TrackPose pose = Sample(distance, across / RaceSimulation.LaneWidth);
            return pose.Position + pose.Up * height;
        }

        private static TrackPose[] CreatePath()
        {
            var result = new TrackPose[Mathf.RoundToInt((End - Start) / Spacing) + 1];
            Vector3 position = new Vector3(0, 12, Start);
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
            float pitch = 0.025f * Mathf.Sin(s * 0.047f);
            // A river descent, ridge climb, and final valley each return smoothly to level.
            pitch += Grade(s, 35, 225, -0.30f);
            pitch += Grade(s, 245, CrestEnd, 0.36f);
            pitch += Grade(s, 488, 630, -0.32f);
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        private static float Grade(float distance, float start, float end, float amplitude)
        {
            if (distance <= start || distance >= end) return 0;
            return amplitude * Mathf.Sin((distance - start) / (end - start) * Mathf.PI * 2);
        }

        private static float Heading(float s)
        {
            float entry = Mathf.SmoothStep(0, 1, (s - 26) / 52);
            float switchback = s > 490 && s < 630
                ? 0.90f * Mathf.Sin((s - 490) * (2 * Mathf.PI / 140)) : 0;
            return entry * (1.05f * Mathf.Sin((s - 41) * 0.029f) + 0.30f * Mathf.Sin(s * 0.057f)) +
                switchback;
        }

        public void Build()
        {
            if (built) return;
            built = true;
            CreatePalette();
            BuildCourse();
            BuildFinishTerrace();
            BuildTunnel();
            BuildEnvironment();
            BuildWaterfalls();
            BuildCourseLandmarks();
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
            Shader riverShader = Shader.Find("RidgeRelay/River Water");
            if (riverShader != null) water.shader = riverShader;
            ripple = MaterialFor("Quiet shallows", 0xa6e2d3);
            redCliff = MaterialFor("Iron sandstone", 0xa25e59);
            mauveCliff = MaterialFor("Lavender rock", 0x895f77);
            sandCliff = MaterialFor("Pale cliff", 0xd99972);
            green = MaterialFor("Canyon scrub", 0x647c67);
            timber = MaterialFor("Ochre wood", 0x7c5947);
            fabric = MaterialFor("Windmill sails", 0xf4d2a1);
            guide = MaterialFor("Turquoise wayfinding", 0x20d4c4, true);
            pad = MaterialFor("Bristle orange", 0xff7940, true);
            gold = MaterialFor("Gold route markers", 0xffbb53, true);
            tunnelStone = MaterialFor("Tunnel interior sandstone", 0xa86b61);
            boardwalk = MaterialFor("River bridge cedar", 0x926750);
            ridgeSlate = MaterialFor("High ridge slate", 0x616b81);
        }

        public static TrackPose FinishPose(int place) =>
            Sample(RaceSimulation.CourseLength + 8, (5 + (place - 1) * 1.6f) / RaceSimulation.LaneWidth);

        private void BuildFinishTerrace()
        {
            var terrace = new Batch();
            float line = RaceSimulation.CourseLength;
            Strip(terrace, boardwalk, line + 3, line + 14, 3, 11.2f, 0);
            for (float s = line + 3; s < line + 14; s += 0.5f)
                Strip(terrace, timber, s, s + 0.025f, 3, 11.2f, 0.01f);
            for (int place = 1; place <= RaceSimulation.Entrants; place++)
            {
                float across = 5 + (place - 1) * 1.6f;
                Strip(terrace, place == 1 ? gold : guide, line + 7.5f, line + 8.5f,
                    across - 0.55f, across + 0.55f, 0.025f);
            }
            for (float s = line + 3; s <= line + 14; s += 2.75f)
            {
                TrackPose pose = Sample(s, 11.1f / RaceSimulation.LaneWidth);
                AddBox(terrace, timber, pose, 0, 0.45f, 0, 0.18f, 0.9f, 0.18f);
                AddBox(terrace, teal, pose, 0, 0.95f, 0, 0.29f, 0.12f, 0.29f);
                Vector3 top = pose.Position;
                float height = top.y + 10.5f;
                AddWorldBox(terrace, stone, top - Vector3.up * height * 0.5f,
                    pose.Forward, 0.6f, height, 0.6f);
            }
            Emit("Finish celebration terrace", terrace);
        }

        private void BuildCourseLandmarks()
        {
            var bridge = new Batch();
            for (float s = 120; s <= 236; s += 8)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    TrackPose p = Sample(s, side);
                    AddBox(bridge, timber, p, side * 1.1f, 0.63f, 0, 0.27f, 1.26f, 0.29f);
                    AddBox(bridge, guide, p, side * 1.1f, 1.3f, 0, 0.33f, 0.10f, 0.36f);
                }
            }
            Emit("Cedar bridge lanterns", bridge);
            var bridgeRails = new Batch();
            for (float s = 118; s < 236; s += 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    TrackPose pose = Sample(s + 1, side * 1.57f);
                    AddBox(bridgeRails, timber, pose, 0, 0.88f, 0, 0.14f, 0.14f, 2.05f);
                    AddBox(bridgeRails, timber, pose, 0, 0.46f, 0, 0.10f, 0.10f, 2.05f);
                    if ((int)s % 4 == 2)
                    {
                        AddBox(bridgeRails, timber, pose, 0, 0.5f, -1, 0.19f, 1.0f, 0.19f);
                        AddBeam(bridgeRails, timber, pose.Offset(0,0.17f,-1), pose.Offset(0,0.79f,3), 0.10f);
                        AddPennant(bridgeRails, pose, side);
                        AddBolt(bridgeRails, pose, side, 0.72f, -1);
                        AddBolt(bridgeRails, pose, side, 0.38f, -1);
                    }
                }
            Emit("Cedar bridge handrails", bridgeRails);

            var river = new Batch();
            for (float s = 112; s < 175; s += 5)
            {
                TrackPose p = Sample(s);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 bank = p.Offset(side * 6, 0);
                    bank.y = -8.55f;
                    AddWorldBox(river, ripple, bank, p.Forward, 5, 0.04f, 0.38f);
                    Vector3 support = p.Offset(side * 2.8f, 0);
                    float top = p.Position.y - 0.4f;
                    support.y = (top - 8.7f) * 0.5f;
                    AddWorldBox(river, timber, support, p.Forward, 0.35f,
                        Mathf.Max(0.3f, top + 8.7f), 0.45f);
                }
            }
            Emit("River rapids and trestle supports", river);

            var ridge = new Batch();
            for (float s = 495; s <= 631; s += 16)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    TrackPose p = Sample(s, side);
                    AddBox(ridge, ridgeSlate, p, side * 1.38f, 0.9f, 0, 0.6f, 1.8f, 0.66f);
                    AddBox(ridge, gold, p, side * 1.38f, 1.55f, 0, 0.66f, 0.18f, 0.72f);
                }
            }
            Emit("High ridge beacons", ridge);
        }

        private void AddPennant(Batch batch, TrackPose pose, int side)
        {
            Vector3 a=pose.Offset(-side*0.09f,0.82f,-0.43f);
            Vector3 b=pose.Offset(-side*0.09f,0.82f,0.43f);
            Vector3 tip=pose.Offset(-side*0.14f,0.22f,0);
            batch.Triangle(teal,a,b,tip);
            batch.Triangle(teal,tip,b,a);
            float face=-side*0.15f;
            Vector3 body=pose.Offset(face,0.57f,0);
            Vector3 tail=pose.Offset(face,0.61f,-0.18f);
            Vector3 chest=pose.Offset(face,0.65f,0.13f);
            batch.Triangle(bright,body,tail,chest);
            batch.Triangle(bright,chest,tail,body);
            Vector3 ear=pose.Offset(face,0.76f,0.06f);
            batch.Triangle(bright,chest,ear,pose.Offset(face,0.64f,0.02f));
            batch.Triangle(bright,pose.Offset(face,0.64f,0.02f),ear,chest);
            AddBeam(batch,fabric,a,b,0.018f);
        }

        private void AddBolt(Batch batch, TrackPose pose, int side, float height, float along)
        {
            Vector3 center=pose.Offset(-side*0.102f,height,along);
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*0.2f,b=(i+1)*Mathf.PI*0.2f;
                Vector3 p=center+pose.Up*(Mathf.Cos(a)*0.04f)+pose.Forward*(Mathf.Sin(a)*0.04f);
                Vector3 q=center+pose.Up*(Mathf.Cos(b)*0.04f)+pose.Forward*(Mathf.Sin(b)*0.04f);
                batch.Triangle(metal,center,p,q);
                batch.Triangle(metal,center,q,p);
            }
        }

        private static void AddBeam(Batch batch, Material material, Vector3 from, Vector3 to, float width)
        {
            Vector3 forward=(to-from).normalized;
            Vector3 right=Vector3.Cross(Vector3.up,forward).normalized;
            Vector3 up=Vector3.Cross(forward,right).normalized;
            AddBox(batch,material,new TrackPose((from+to)*0.5f,forward,up,right),
                0,0,0,width,width,Vector3.Distance(from,to));
        }

        private Material MaterialFor(string materialName, int packedColor, bool emission = false)
        {
            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            var color = new Color32((byte)(packedColor >> 16), (byte)(packedColor >> 8), (byte)packedColor, 255);
            var material = new Material(shader) { name = materialName, color = color };
            material.SetFloat("_Smoothness", 0.08f);
            string surface = materialName switch
            {
                "Chalk sandstone" or "Warm paving" or "Sandstone coping" => "Sandstone",
                "River bridge cedar" or "Ochre wood" => "Timber",
                "High ridge slate" => "Slate",
                "Lavender rock" => "CanyonRockV2",
                "Iron sandstone" or "Pale cliff" or "Tunnel interior sandstone" => "CanyonRockV2",
                _ => null
            };
            if (surface != null)
            {
                Texture2D texture = Resources.Load<Texture2D>("RidgeRelay/Surfaces/" + surface);
                material.mainTexture = texture;
                Texture2D normal = Resources.Load<Texture2D>("RidgeRelay/Surfaces/" + surface + "Normal");
                Texture2D mask = Resources.Load<Texture2D>("RidgeRelay/Surfaces/" + surface + "Mask");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 0.4f);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (mask != null)
                {
                    material.SetTexture("_MetallicGlossMap", mask);
                    material.SetFloat("_Smoothness", 1);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (texture != null)
                    material.color = materialName == "Lavender rock" ? new Color(0.67f, 0.76f, 0.91f)
                        : surface == "CanyonRockV2" ? Color.white
                        : surface == "Timber" ? new Color(0.70f, 0.73f, 0.76f) : Color.white;
            }
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
            cuts.Add(RaceSimulation.TunnelLockStart);
            cuts.Add(RaceSimulation.TunnelStart);
            cuts.Add(RaceSimulation.TunnelEnd);
            cuts.Add(RaceSimulation.TunnelLockEnd);
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
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (GapAt(s, side)) continue;
                        TrackPose pose = Sample(s, side);
                        Vector3 top = pose.Offset(0.12f * side, -0.42f);
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
                if (a >= 118 && a < 236) paving = boardwalk;
                else if (a >= 492 && a < 632) paving = ridgeSlate;
                Strip(batch, paving, a, b, left, right, 0);
                if (paving == boardwalk)
                    for (float plank = a + 0.28f; plank + 0.025f < b; plank += 0.42f)
                        Strip(batch, timber, plank, plank + 0.025f, left, right, 0.012f);
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
                float coping = paving == boardwalk ? 0.155f : 0.60f;
                Strip(batch, rail, a, b, Mathf.Min(inner, edge), Mathf.Max(inner, edge), coping);
                Strip(batch, teal, a, b, Mathf.Min(inner, edge), Mathf.Max(inner, edge), coping + 0.011f);
                AddDeckWall(batch, rail, a, b, inner, lane > 0, 0, coping);
                AddDeckWall(batch, rail, a, b, edge, lane < 0, 0, coping);
                if (Mathf.FloorToInt(a + 14) % 6 == 0)
                {
                    TrackPose p = Sample(a + 0.08f, (edge - lane * 0.12f) / RaceSimulation.LaneWidth);
                    float post = paving == boardwalk ? 0.4f : 0.85f;
                    AddBox(batch, rail, p, 0, post * 0.5f, 0, 0.3f, post, 0.3f);
                    AddBox(batch, bright, p, 0, post + 0.02f, 0, 0.34f, 0.07f, 0.34f);
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
            if(material.name=="River bridge cedar")
            {
                batch.Quad(material,TrackPoint(from,left,y),TrackPoint(to,left,y),
                    TrackPoint(to,right,y),TrackPoint(from,right,y),
                    new Vector2(from*0.4f,left*0.23f),new Vector2(to*0.4f,left*0.23f),
                    new Vector2(to*0.4f,right*0.23f),new Vector2(from*0.4f,right*0.23f));
                return;
            }
            batch.Quad(material, TrackPoint(from, left, y), TrackPoint(to, left, y),
                TrackPoint(to, right, y), TrackPoint(from, right, y),
                new Vector2(left * 0.5f, from * 0.25f), new Vector2(left * 0.5f, to * 0.25f),
                new Vector2(right * 0.5f, to * 0.25f), new Vector2(right * 0.5f, from * 0.25f));
        }

        private static void AddDeckWall(Batch batch, Material material, float from, float to, float side,
            bool facesLeft, float bottom = -0.4f, float top = 0)
        {
            Vector3 p = TrackPoint(from, side, top);
            Vector3 q = TrackPoint(from, side, bottom);
            Vector3 r = TrackPoint(to, side, bottom);
            Vector3 s = TrackPoint(to, side, top);
            if (facesLeft) batch.Quad(material, p, q, r, s);
            else batch.Quad(material, s, r, q, p);
        }

        private static void AddDeckEnd(Batch batch, Material material, float distance, float left, float right, bool facesBack)
        {
            Vector3 a = TrackPoint(distance, left, 0);
            Vector3 b = TrackPoint(distance, right, 0);
            Vector3 c = TrackPoint(distance, right, -0.4f);
            Vector3 d = TrackPoint(distance, left, -0.4f);
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

        private void BuildTunnel()
        {
            var tunnel = new Batch();
            const float left = -DeckHalfWidth - 0.12f;
            const float right = DeckHalfWidth + 0.12f;
            float dividerLeft = TunnelDivider - TunnelDividerHalfWidth;
            float dividerRight = TunnelDivider + TunnelDividerHalfWidth;
            float start = RaceSimulation.TunnelStart;
            float end = RaceSimulation.TunnelEnd;

            for (float from = start; from < end; from += 2)
            {
                float to = Mathf.Min(from + 2, end);
                const float spring=3;
                AddDeckWall(tunnel, tunnelStone, from, to, left, false, 0, spring);
                AddDeckWall(tunnel, tunnelStone, from, to, right, true, 0, spring);
                AddDeckWall(tunnel, sandCliff, from, to, left - 0.32f, true, 0, spring);
                AddDeckWall(tunnel, sandCliff, from, to, right + 0.32f, false, 0, spring);
                for(int arch=0;arch<16;arch++)
                {
                    float a=arch*Mathf.PI/16,b=(arch+1)*Mathf.PI/16;
                    float x0=right*Mathf.Cos(a),x1=right*Mathf.Cos(b);
                    float y0=spring+(TunnelClearance-spring)*Mathf.Sin(a);
                    float y1=spring+(TunnelClearance-spring)*Mathf.Sin(b);
                    tunnel.Quad(tunnelStone,TrackPoint(from,x0,y0),TrackPoint(to,x0,y0),
                        TrackPoint(to,x1,y1),TrackPoint(from,x1,y1));
                    tunnel.Quad(sandCliff,TrackPoint(from,x1*1.1f,y1+0.32f),TrackPoint(to,x1*1.1f,y1+0.32f),
                        TrackPoint(to,x0*1.1f,y0+0.32f),TrackPoint(from,x0*1.1f,y0+0.32f));
                }
                AddDeckWall(tunnel, stone, from, to, dividerLeft, true, 0, 3);
                AddDeckWall(tunnel, sandstone, from, to, dividerRight, false, 0, 3);
                Strip(tunnel,rail,from,to,dividerLeft,dividerRight,3);
                AddDeckWall(tunnel, guide, from, to, dividerLeft - 0.005f, true, 1.52f, 1.62f);
                AddDeckWall(tunnel, gold, from, to, dividerRight + 0.005f, false, 1.52f, 1.62f);
            }

            for (float s = start - 8; s < end + 2; s += 3.4f)
            {
                PaintArrow(tunnel, guide, s, -1, 0.54f);
                PaintArrow(tunnel, gold, s, RaceSimulation.TunnelFastLane, 0.58f);
                if (s < start || s > end - 0.5f) continue;

                for (int side = -1; side <= 1; side += 2)
                {
                    TrackPose fixture = Sample(s, side * 1.28f);
                    AddBox(tunnel, metal, fixture, 0, 2.78f, 0, 0.52f, 0.18f, 0.64f);
                    AddBox(tunnel, side < 0 ? guide : gold, fixture, 0, 2.67f, 0,
                        0.42f, 0.055f, 0.53f);
                }
            }

            foreach (float portal in new[] { start, end })
            {
                foreach (float across in new[] { left, TunnelDivider, right })
                {
                    TrackPose wall = Sample(portal, across / RaceSimulation.LaneWidth);
                    AddBox(tunnel, stone, wall, 0, 1.5f, 0, 0.36f, 3, 0.65f);
                }
                for(int arch=0;arch<16;arch++)
                {
                    float a=arch*Mathf.PI/16,b=(arch+0.94f)*Mathf.PI/16;
                    Vector3 from=TrackPoint(portal,right*Mathf.Cos(a),3+2.6f*Mathf.Sin(a));
                    Vector3 to=TrackPoint(portal,right*Mathf.Cos(b),3+2.6f*Mathf.Sin(b));
                    AddBeam(tunnel,arch%3==0?stone:sandstone,from,to,0.55f);
                }
            }

            Emit("Tank Tunnel", tunnel);
        }

        private static void PaintArrow(Batch batch, Material paint, float distance, int lane, float halfWidth)
        {
            float center = lane * RaceSimulation.LaneWidth;
            const float y = 0.031f;
            Vector3 left = TrackPoint(distance, center - halfWidth, y);
            Vector3 right = TrackPoint(distance, center + halfWidth, y);
            Vector3 tip = TrackPoint(distance + 0.62f, center, y);
            Vector3 notch = TrackPoint(distance + 0.33f, center, y);
            batch.Quad(paint, left, tip, notch, TrackPoint(distance - 0.20f, center - halfWidth, y));
            batch.Quad(paint, right, TrackPoint(distance - 0.20f, center + halfWidth, y), notch, tip);
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
            GameObject treeModel = Resources.Load<GameObject>("RidgeRelay/Environment/CanyonTree");
            GameObject shrubModel = Resources.Load<GameObject>("RidgeRelay/Environment/RiverShrub");
            GameObject flowersModel = Resources.Load<GameObject>("RidgeRelay/Environment/LavenderPatch");
            var greenery = new GameObject("Canyon vegetation");
            greenery.transform.SetParent(transform, false);
            for (float segment = -9; segment < End; segment += ChunkLength)
            {
                var sector = new Batch();
                for (float s = segment + 2; s < Mathf.Min(segment + ChunkLength, End); s += 12)
                {
                    TrackPose p = Sample(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float tunnelMargin = s > RaceSimulation.TunnelLockStart - 4 &&
                            s < RaceSimulation.TunnelLockEnd + 4 ? 4 : 0;
                        float riverMargin = s >= 90 && s <= 225 ? 7 : 0;
                        float x = side * (10 + tunnelMargin + riverMargin + Range(random, 0, 7));
                        Vector3 basePoint = p.Offset(x, 0, Range(random, -3, 3));
                        basePoint.y = -9.8f;
                        float width = Range(random, 10, 18);
                        float height = Mathf.Max(1.8f,
                            p.Position.y + 9.8f - Range(random, 0.6f, 2.2f));
                        basePoint = ClearRoad(basePoint, width * 0.6f, height + 8, p.Right * side);
                        AddRock(sector, sandCliff, redCliff, basePoint, width, height, random);
                        if (cliffModel != null && ((int)s + side) % 3 == 0)
                        {
                            var cliff = Instantiate(cliffModel, basePoint + Vector3.up * (height - 1.7f),
                                Quaternion.Euler(0, Range(random, 0, 360), 0), transform);
                            cliff.transform.localScale = Vector3.one * Range(random, 0.55f, 0.9f);
                        }

                        Vector3 backdrop = p.Offset(side * Range(random, 48, 70), 0, Range(random, -7, 7));
                        backdrop.y = -10;
                        float backdropWidth = Range(random, 26, 42);
                        float backdropHeight = Mathf.Max(Range(random, 15, 23),
                            p.Position.y + 10 + Range(random, 8, 15));
                        backdrop = ClearRoad(backdrop, backdropWidth * 0.6f, backdropHeight, p.Right * side);
                        if ((int)(s - segment) < 15)
                            AddRock(sector, mauveCliff, sandCliff, backdrop, backdropWidth, backdropHeight, random);

                        Vector3 splash = basePoint + p.Right * (-side * (width * 0.45f + 0.32f));
                        splash.y = -8.68f;
                        AddWorldBox(sector, ripple, splash, p.Forward, Range(random, 2, 4), 0.015f, Range(random, 0.3f, 1.1f));

                        Vector3 scrub = basePoint + Vector3.up * height;
                        if (treeModel != null && random.NextDouble() < 0.48)
                        {
                            var tree = Instantiate(treeModel, scrub - Vector3.up * 0.1f,
                                Quaternion.Euler(0, Range(random, 0, 360), 0), greenery.transform);
                            tree.transform.localScale = Vector3.one * Range(random, 0.85f, 1.35f);
                        }
                        if (shrubModel != null)
                        {
                            var shrub = Instantiate(shrubModel, scrub + p.Right * side * 0.8f,
                                Quaternion.Euler(0, Range(random, 0, 360), 0), greenery.transform);
                            shrub.transform.localScale = Vector3.one * Range(random, 0.8f, 1.5f);
                        }
                        if (flowersModel != null && (int)s % 3 == 0)
                            Instantiate(flowersModel, scrub + p.Right * -side * 0.7f,
                                Quaternion.LookRotation(p.Forward, Vector3.up), greenery.transform);
                        for (int sprout = 0; sprout < 3; sprout++)
                        {
                            var root = scrub + new Vector3(Range(random, -1, 1), -0.1f, Range(random, -1, 1));
                            AddRock(sector, green, green, root, Range(random, 0.4f, 0.9f), Range(random, 0.35f, 0.85f), random);
                        }
                    }
                }

                Emit("Canyon cliffs", sector);
            }

            var land = new Batch();
            for (float s = 245; s < 630; s += 14)
            {
                if (s > 402 && s < 490) continue;
                bool gapNearby = false;
                foreach (CourseFeature feature in RaceSimulation.Features)
                    if (feature.Gap && feature.Start < s + 15 && feature.End > s - 15) gapNearby = true;
                if (gapNearby) continue;
                Vector3 root = Sample(s).Position;
                root.y = -9.8f;
                float height = Sample(s).Position.y - root.y - 1;
                foreach (TrackPose road in Path)
                {
                    float dx = road.Position.x - root.x;
                    float dz = road.Position.z - root.z;
                    if (dx * dx + dz * dz < 20 * 20)
                        height = Mathf.Min(height, road.Position.y - root.y - 1);
                }
                if (height > 1) AddRock(land, sandCliff, redCliff, root, 28, height, random);
            }
            Emit("Ridge landforms", land);
            var gardens=new Batch();
            for(float distance=10;distance<640;distance+=24)
            {
                if(distance>RaceSimulation.TunnelLockStart-8 && distance<RaceSimulation.TunnelLockEnd+8)continue;
                int side=((int)distance/24)%2==0?-1:1;
                TrackPose road=Sample(distance);
                Vector3 root=road.Offset(side*6.3f,0);
                root=ClearRoad(root,0.8f,6,road.Right*side);
                float top=root.y-0.35f;
                foreach(TrackPose nearby in Path)
                {
                    float dx=nearby.Position.x-root.x,dz=nearby.Position.z-root.z;
                    if(dx*dx+dz*dz<6*6)top=Mathf.Min(top,nearby.Position.y-0.6f);
                }
                root.y=-9.8f;
                if(top-root.y<1)continue;
                AddRock(gardens,green,sandCliff,root,7.5f,top-root.y,random);
                Vector3 planted=new Vector3(root.x,top-0.02f,root.z);
                if(treeModel!=null)
                {
                    var tree=Instantiate(treeModel,planted,Quaternion.Euler(0,Range(random,0,360),0),greenery.transform);
                    tree.transform.localScale=Vector3.one*Range(random,1.15f,1.5f);
                }
                for(int i=0;i<3;i++)
                {
                    Vector3 at=planted+road.Forward*(i-1)*0.9f+road.Right*side*1.5f;
                    if(shrubModel!=null)Instantiate(shrubModel,at,Quaternion.identity,greenery.transform);
                    if(flowersModel!=null && i==1)Instantiate(flowersModel,at+road.Forward*0.6f,Quaternion.identity,greenery.transform);
                }
            }
            Emit("Garden shelves",gardens);

            for (int i = 0; i < 7; i++)
            {
                TrackPose p = Sample(14 + 93 * i);
                Vector3 building = p.Offset(i % 2 == 0 ? -7.3f : 7.3f, -0.4f);
                AddWindmill(building, p.Forward, millModel);
            }
        }

        private static Vector3 ClearRoad(Vector3 origin, float radius, float height, Vector3 outward)
        {
            // A rock beside one bend can otherwise occupy a different part of the road.
            float clearance = radius + DeckHalfWidth + 0.75f;
            outward.y = 0;
            outward.Normalize();
            for (int attempt = 0; attempt < 16; attempt++)
            {
                bool overlaps = false;
                for (int i = 0; i < Path.Length; i += 4)
                {
                    Vector3 road = Path[i].Position;
                    if (road.y + 2.2f < origin.y || road.y - 0.6f > origin.y + height) continue;
                    float dx = road.x - origin.x;
                    float dz = road.z - origin.z;
                    float corridor = Start + i * Spacing >= RaceSimulation.CourseLength - 6
                        ? radius + 17 : clearance;
                    if (dx * dx + dz * dz >= corridor * corridor) continue;
                    overlaps = true;
                    break;
                }
                if (!overlaps) return origin;
                origin += outward * 4;
            }
            // Dense switchbacks can exhaust nearby space. The outer bound is always clear.
            foreach (TrackPose pose in Path)
                origin.x = Mathf.Max(origin.x, pose.Position.x + clearance + 1);
            return origin;
        }

        private void BuildWaterfalls()
        {
            Shader shader = Shader.Find("RidgeRelay/Waterfall");
            if (shader == null) return;
            var flow = new Material(shader) { name = "Flowing waterfall" };
            ownedMaterials.Add(flow);
            var falls = new Batch();
            var cliffs = new Batch();
            var random = new System.Random(614);
            var mistMaterial=new Material(Shader.Find("RidgeRelay/Soft Smoke")){name="Waterfall spray"};
            ownedMaterials.Add(mistMaterial);
            foreach (float distance in new[] { 175f, 235f, 535f })
            {
                TrackPose road = Sample(distance);
                float top = Mathf.Max(road.Position.y + 11, 8);
                Vector3 basePoint = road.Offset(-15, 0, 5);
                basePoint.y = -9.8f;
                basePoint = ClearRoad(basePoint, 7.5f, top + 10, -road.Right);
                AddRock(cliffs, sandCliff, redCliff, basePoint, 12, top + 9.8f, random);
                Vector3 towardView = Sample(distance - 20).Position - basePoint;
                towardView.y = 0;
                towardView.Normalize();
                Vector3 acrossFall = Vector3.Cross(Vector3.up, towardView).normalized;
                Vector3 face = basePoint + towardView * 6.2f;
                for (int segment = 0; segment < 12; segment++)
                {
                    float upper = Mathf.Lerp(top - 0.5f, -8.6f, segment / 12f);
                    float lower = Mathf.Lerp(top - 0.5f, -8.6f, (segment + 1) / 12f);
                    float spread = 3f + segment * 0.045f;
                    Vector3 center = new Vector3(face.x, 0, face.z) + towardView * (segment * 0.035f);
                    Vector3 a = center - acrossFall * spread + Vector3.up * upper;
                    Vector3 b = center - acrossFall * spread + Vector3.up * lower;
                    Vector3 c = center + acrossFall * spread + Vector3.up * lower;
                    Vector3 d = center + acrossFall * spread + Vector3.up * upper;
                    falls.Quad(flow, a, b, c, d, new Vector2(0, 1 - segment / 12f),
                        new Vector2(0, 1 - (segment + 1) / 12f),
                        new Vector2(1, 1 - (segment + 1) / 12f), new Vector2(1, 1 - segment / 12f));
                }
                Vector3 pool = new Vector3(face.x, -8.63f, face.z);
                CreateWaterfallMist(pool,mistMaterial);
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2 / 24;
                    float b = (i + 0.8f) * Mathf.PI * 2 / 24;
                    Vector3 directionA = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 directionB = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    falls.Quad(ripple, pool + directionA * 1.5f, pool + directionA * 2.9f,
                        pool + directionB * 2.9f, pool + directionB * 1.5f);
                }
            }
            Emit("Waterfall cliffs", cliffs);
            Emit("Waterfall cascades", falls);
        }

        private void CreateWaterfallMist(Vector3 pool, Material material)
        {
            var system=new GameObject("Waterfall spray").AddComponent<ParticleSystem>();
            system.transform.SetParent(transform,false);
            system.transform.SetPositionAndRotation(pool+Vector3.up*0.25f,Quaternion.Euler(-90,0,0));
            system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=system.main;
            main.loop=true;
            main.duration=2;
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,2);
            main.startSize=new ParticleSystem.MinMaxCurve(1.5f,3);
            main.startSpeed=new ParticleSystem.MinMaxCurve(0.3f,1.2f);
            main.startColor=new Color(0.82f,0.96f,0.98f,0.32f);
            main.maxParticles=40;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=system.emission;emission.rateOverTime=12;
            var shape=system.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=2.8f;shape.angle=22;
            var size=system.sizeOverLifetime;size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0.5f,1,1.4f));
            var fade=system.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.2f),new GradientAlphaKey(0,1)});
            fade.color=new ParticleSystem.MinMaxGradient(gradient);
            var renderer=system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            system.Play();
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
            const int sides = 8;
            float[] heights = { 0, 0.36f, 0.42f, 0.78f, 0.94f, 1 };
            float[] widths = { 1.12f, 1.06f, 0.94f, 0.88f, 0.74f, 0.68f };
            var rings = new Vector3[heights.Length, sides];
            float rotation = Range(random, 0, Mathf.PI * 2);
            for (int i = 0; i < sides; i++)
            {
                float angle = rotation + Mathf.PI * 2 * i / sides;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float radius = width * Range(random, 0.44f, 0.54f);
                for (int level = 0; level < heights.Length; level++)
                    rings[level, i] = root + direction * radius * widths[level] +
                        Vector3.up * (height * heights[level]);
            }
            for (int level = 0; level < heights.Length - 1; level++)
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    batch.Quad(level == 1 || level >= 4 ? cap : wall,
                        rings[level, i], rings[level + 1, i],
                        rings[level + 1, next], rings[level, next]);
                }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                batch.Triangle(cap, root + Vector3.up * height,
                    rings[heights.Length - 1, next], rings[heights.Length - 1, i]);
            }
        }

        private void BuildFeatures()
        {
            GameObject chevrons = Resources.Load<GameObject>("RidgeRelay/Environment/Chevrons");
            for (int i = 0; i < RaceSimulation.Features.Count; i++)
            {
                CourseFeature feature = RaceSimulation.Features[i];
                float across = feature.Lane * RaceSimulation.LaneWidth;
                if (feature.Item != Gadget.None)
                {
                    BuildPickupGate(i, feature);
                    continue;
                }

                var warning = new Batch();
                Material paint = feature.Shortcut ? gold : hazard;
                for (int line = 0; line < 2; line++)
                {
                    float distance = feature.Start - (feature.Gap ? 6 : 3.75f) + line * (feature.Gap ? 1.7f : 0.78f);
                    PaintArrow(warning, paint, distance, feature.Lane, 0.64f);
                }

                if (feature.Gap)
                {
                    TrackPose end = Sample(feature.End, feature.Lane);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        AddBox(warning, teal, end, side * 0.82f, 0.5f, 0.15f, 0.12f, 1.0f, 0.12f);
                        AddBox(warning, feature.Shortcut ? gold : bright, end, side * 0.82f, 1.06f, 0.15f,
                            0.22f, 0.16f, 0.22f);
                    }

                    Strip(warning, feature.Shortcut ? gold : teal, feature.End + 0.05f, feature.End + 0.4f,
                        across - 0.73f, across + 0.73f, 0.031f);
                    if (feature.Shortcut) MarkShortcut(warning, feature, across);
                }
                else
                {
                    TrackPose hurdle = Sample(feature.Start + 0.2f, feature.Lane);
                    AddBox(warning, hazard, hurdle, 0, 0.33f, 0, 1.62f, 0.58f, 0.31f);
                    for (int slat = -2; slat <= 2; slat += 2)
                        AddBox(warning, bright, hurdle, 0.27f * slat, 0.34f, -0.162f, 0.17f, 0.32f, 0.016f);
                }

                Emit(feature.Shortcut ? "Downhill shortcut" : (feature.Gap ? "Jump markings" : "Lane hurdle"), warning);
                if (chevrons != null && feature.Gap)
                {
                    TrackPose sign = Sample(feature.Start - 6);
                    int side = feature.Lane == 0 ? 1 : feature.Lane;
                    Instantiate(chevrons, sign.Offset(side * 3.35f, 0.04f),
                        Quaternion.LookRotation(-sign.Forward, sign.Up), transform).transform.localScale = Vector3.one * 0.65f;
                }
            }
        }

        private void MarkShortcut(Batch batch, CourseFeature feature, float across)
        {
            PaintArrow(batch, gold, feature.Start - 13, feature.Lane, 0.66f);
            PaintArrow(batch, gold, feature.Start - 10, feature.Lane, 0.66f);
            Strip(batch, gold, feature.Start - 1.28f, feature.Start - 0.9f, across - 0.75f, across + 0.75f, 0.036f);
            Strip(batch, bright, feature.Start - 0.90f, feature.Start - 0.73f, across - 0.75f, across + 0.75f, 0.036f);
            TrackPose sign = Sample(feature.Start - 10, feature.Lane);
            AddBox(batch, metal, sign, -1.22f, 0.72f, 0, 0.16f, 1.44f, 0.22f);
            AddBox(batch, gold, sign, -1.22f, 1.62f, 0, 0.55f, 0.38f, 0.2f);
            for (float s = feature.End + 2; s < feature.End + 15; s += 3.5f)
                PaintArrow(batch, gold, s, feature.Lane, 0.57f);
        }

        private void BuildPickupGate(int index, CourseFeature feature)
        {
            TrackPose gate = Sample(feature.Start + 0.5f, feature.Lane);
            var frame = new Batch();
            for (int side = -1; side <= 1; side += 2)
            {
                AddBox(frame, metal, gate, side * 0.88f, 0.24f, 0, 0.16f, 0.48f, 0.58f);
                AddBox(frame, guide, gate, side * 0.88f, 0.5f, 0, 0.19f, 0.08f, 0.58f);
            }
            float center = feature.Lane * RaceSimulation.LaneWidth;
            Strip(frame, guide, feature.Start + 0.3f, feature.Start + 0.64f, center - 0.78f, center + 0.78f, 0.026f);
            Emit("Gadget gate", frame);

            var pickup = new GameObject("Gadget pickup");
            pickup.transform.SetParent(transform);
            pickup.transform.position = gate.Offset(0, 0.87f);
            pickups.Add(index, pickup.transform);

            var icon = new Batch();
            TrackPose prop = LevelPose(Vector3.zero, Vector3.forward);
            AddBox(icon, guide, prop, 0, 0, 0, 0.55f, 0.46f, 0.54f);
            AddBox(icon, metal, prop, 0, -0.05f, 0, 0.57f, 0.08f, 0.56f);
            AddBox(icon, bright, prop, 0, 0.25f, 0, 0.58f, 0.09f, 0.57f);
            AddBox(icon, bright, prop, 0, 0.045f, 0, 0.11f, 0.20f, 0.57f);
            Emit("Gadget crate", icon, pickup.transform);
        }

        public void UpdateVisuals(RaceSimulation race, int localSlot = 0)
        {
            if (!built) Build();
            float clock = Mathf.Max(0, race.Clock);
            foreach (var pickup in pickups)
            {
                bool ready = race.IsPickupReady(pickup.Key, localSlot);
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
            geometry.SetUVs(0, batch.Uvs);
            geometry.subMeshCount = batch.Materials.Count;
            for (int i = 0; i < batch.Materials.Count; i++)
                geometry.SetTriangles(batch.Indices[i], i);
            geometry.RecalculateNormals();
            if (objectName == "Canyon cliffs" || objectName == "Ridge landforms" || objectName == "Waterfall cliffs")
            {
                Vector3[] normals = geometry.normals;
                var shared = new Dictionary<Vector3, Vector3>();
                for (int i = 0; i < batch.Vertices.Count; i++)
                {
                    Vector3 vertex = batch.Vertices[i];
                    shared.TryGetValue(vertex, out Vector3 sum);
                    shared[vertex] = sum + normals[i];
                }
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = Vector3.Lerp(normals[i], shared[batch.Vertices[i]].normalized, 0.65f).normalized;
                geometry.normals = normals;
            }
            geometry.RecalculateBounds();
            geometry.RecalculateTangents();
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
            public readonly List<Vector2> Uvs = new List<Vector2>();
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
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Quad(material, a, b, c, d, Project(a, normal), Project(b, normal),
                    Project(c, normal), Project(d, normal));
            }

            private static Vector2 Project(Vector3 point, Vector3 normal)
            {
                if (Mathf.Abs(normal.y) > 0.5f) return new Vector2(point.x, point.z) * 0.25f;
                return Mathf.Abs(normal.x) > Mathf.Abs(normal.z)
                    ? new Vector2(point.z, point.y) * 0.2f : new Vector2(point.x, point.y) * 0.2f;
            }

            public void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
            {
                List<int> indices = IndicesFor(material);
                int start = Vertices.Count;
                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Vertices.Add(d);
                Uvs.Add(uvA);
                Uvs.Add(uvB);
                Uvs.Add(uvC);
                Uvs.Add(uvD);
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
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Uvs.Add(Project(a, normal));
                Uvs.Add(Project(b, normal));
                Uvs.Add(Project(c, normal));
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
            }
        }
    }
}


