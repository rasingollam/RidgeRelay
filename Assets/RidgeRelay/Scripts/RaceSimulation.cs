using System;
using System.Collections.Generic;

namespace RidgeRelay
{
    public enum Gadget { None, Tailwind, BristlePad, ShellFilm, PulseShot }
    public enum RacePhase { Countdown, Running, Finished }

    public readonly struct RaceCommand
    {
        public readonly int LaneDelta;
        public readonly bool Jump;
        public readonly bool UseItem;

        public RaceCommand(int laneDelta = 0, bool jump = false, bool useItem = false)
        {
            LaneDelta = Math.Sign(laneDelta);
            Jump = jump;
            UseItem = useItem;
        }
    }

    public sealed class RunnerState
    {
        public int Lane { get; internal set; }
        public float Lateral { get; internal set; }
        public float Distance { get; internal set; }
        public float Height { get; internal set; }
        public Gadget Held { get; internal set; }
        public int Place { get; internal set; }
        public float FinishTime { get; internal set; } = -1;
        public float Shield { get; internal set; }
        public float Boost { get; internal set; }
        public float Slow { get; internal set; }
        public float Recovery { get; internal set; }
        public float Immunity { get; internal set; }
        public uint PickupMask { get; internal set; }
        public float Slipstream { get; internal set; }
        public float PulseWarning { get; internal set; }
        public int PulseLane { get; internal set; }
        public int PulseOwner { get; internal set; } = -1;
        public float ItemCooldown { get; internal set; }
        public float RouteBoost { get; internal set; }
        public int PulseHits { get; internal set; }
        public int PulseDodges { get; internal set; }
        public int PulseBlocks { get; internal set; }
        public int ShortcutClears { get; internal set; }
        public int ShortcutFalls { get; internal set; }
        internal float VerticalSpeed;
        internal int Checkpoint;
        internal bool OnShortcut;
    }

    public readonly struct CourseFeature
    {
        public readonly float Start;
        public readonly float End;
        public readonly int Lane;
        public readonly Gadget Item;
        public readonly bool Gap;
        public readonly bool Shortcut;

        public CourseFeature(float start, float end, int lane, Gadget item = Gadget.None,
            bool gap = false, bool shortcut = false)
        {
            Start = start;
            End = end;
            Lane = lane;
            Item = item;
            Gap = gap;
            Shortcut = shortcut;
        }
    }

    public sealed class RaceMetrics
    {
        private readonly float[] checkpointGaps;
        public IReadOnlyList<float> CheckpointGaps { get; }
        public int LeadChanges { get; internal set; }
        public int PulseHits { get; internal set; }
        public int PulseDodges { get; internal set; }
        public int PulseBlocks { get; internal set; }
        public int ShortcutClears { get; internal set; }
        public int ShortcutFalls { get; internal set; }

        internal RaceMetrics(int checkpointCount)
        {
            checkpointGaps = new float[checkpointCount];
            Array.Fill(checkpointGaps, -1);
            CheckpointGaps = Array.AsReadOnly(checkpointGaps);
        }

        internal void RecordGap(int checkpoint, float spread) => checkpointGaps[checkpoint] = spread;
    }

    public sealed class RaceSimulation
    {
        public const float StepSeconds = 1f / 60;
        public const float CourseLength = 655;
        public const int Entrants = 4;
        public const float LaneWidth = 2;
        public const float BaseSpeed = 9;
        public const float SlipstreamActiveThreshold = 0.85f;
        public const float PulseWindup = 0.65f;
        public const float PulseRange = 23;
        public const float UseCooldown = 1.2f;
        public const float ShortcutBoostSeconds = 2.4f;
        public const float TunnelStart = 436;
        public const float TunnelEnd = 462;
        public const int TunnelFastLane = 1;
        public const float TunnelLockStart = 432;
        public const float TunnelLockEnd = 466;
        public const float TunnelSpeedMultiplier = 1.12f;
        public static readonly IReadOnlyList<float> Checkpoints =
            Array.AsReadOnly(new float[] { 0, 118, 236, 354, 472, 590 });

        public static readonly IReadOnlyList<CourseFeature> Features = Array.AsReadOnly(new[]
        {
            new CourseFeature(24, 25, 0),
            new CourseFeature(43, 47, -1, Gadget.Tailwind),
            new CourseFeature(43, 47, 1, Gadget.ShellFilm),
            new CourseFeature(70, 76, -1, gap: true),
            new CourseFeature(96, 97, 1),
            new CourseFeature(132, 135, 0, Gadget.BristlePad),
            new CourseFeature(132, 135, 1, Gadget.BristlePad),
            new CourseFeature(154, 160, 0, gap: true),
            new CourseFeature(182, 183, -1),
            new CourseFeature(200, 203, -1, Gadget.Tailwind),
            new CourseFeature(200, 203, 1, Gadget.Tailwind),
            new CourseFeature(219, 220, 1),
            new CourseFeature(252, 258, 1, gap: true),
            new CourseFeature(274, 277, -1, Gadget.ShellFilm),
            new CourseFeature(274, 277, 0, Gadget.ShellFilm),
            new CourseFeature(304, 305, 0),
            new CourseFeature(332, 336, -1, Gadget.BristlePad),
            new CourseFeature(332, 336, 1, Gadget.BristlePad),
            new CourseFeature(363, 364, 1),
            new CourseFeature(387, 393, -1, gap: true, shortcut: true),
            new CourseFeature(414, 417, -1, Gadget.Tailwind),
            new CourseFeature(414, 417, 0, Gadget.Tailwind),
            // The narrow tunnel earns its speed advantage only if the runner clears this hurdle.
            new CourseFeature(443, 444, TunnelFastLane),
            new CourseFeature(482, 488, 0, gap: true),
            new CourseFeature(514, 517, 0, Gadget.BristlePad),
            new CourseFeature(514, 517, 1, Gadget.BristlePad),
            new CourseFeature(552, 553, -1),
            new CourseFeature(574, 577, -1, Gadget.Tailwind),
            new CourseFeature(574, 577, 1, Gadget.Tailwind),
            new CourseFeature(607, 613, 1, gap: true),
            new CourseFeature(632, 633, 0)
        });

        private static readonly uint[] GateBits = BuildGateBits();

        public readonly struct Trap
        {
            public readonly int Owner;
            public readonly int Lane;
            public readonly float Distance;
            public readonly float Expires;
            public Trap(int owner, int lane, float distance, float expires)
            {
                Owner = owner;
                Lane = lane;
                Distance = distance;
                Expires = expires;
            }
        }

        private readonly RunnerState[] runners = new RunnerState[Entrants];
        private readonly float[] priorDistance = new float[Entrants];
        private readonly float[] priorLateral = new float[Entrants];
        private readonly float[] priorHeight = new float[Entrants];
        private readonly bool[] newWarnings = new bool[Entrants];
        private readonly List<Trap> traps = new List<Trap>();
        private const int CountdownTicks = 180;
        private int tick = -CountdownTicks;
        private int finished;
        private int leader = -1;
        private int nextGapCheckpoint = 1;
        private readonly bool waitForAll;
        private readonly uint raceSeed;

        public IReadOnlyList<RunnerState> Runners => runners;
        public IReadOnlyList<Trap> Traps => traps;
        public RaceMetrics Metrics { get; } = new RaceMetrics(Checkpoints.Count);
        public bool IsPickupReady(int featureIndex, int runnerIndex = 0)
        {
            uint gate = GateBits[featureIndex];
            return gate != 0 && (runners[runnerIndex].PickupMask & gate) == 0;
        }
        public RacePhase Phase { get; private set; }
        public float Clock => tick * StepSeconds;

        public RaceSimulation(bool waitForAll = false, int seed = 4261)
        {
            this.waitForAll = waitForAll;
            raceSeed = unchecked((uint)seed);
            for (int i = 0; i < Entrants; i++)
            {
                int lane = i % 3 - 1;
                runners[i] = new RunnerState
                {
                    Lane = lane, Lateral = lane, Distance = i == 3 ? -2 : 0, Place = i + 1
                };
            }
        }

        public void Tick(RaceCommand[] commands)
        {
            if (Phase == RacePhase.Finished) return;
            tick++;
            if (tick < 0) return;
            Phase = RacePhase.Running;

            for (int i = 0; i < Entrants; i++)
            {
                priorDistance[i] = runners[i].Distance;
                priorLateral[i] = runners[i].Lateral;
                priorHeight[i] = runners[i].Height;
                newWarnings[i] = false;
            }
            traps.RemoveAll(trap => trap.Expires <= Clock);
            for (int i = 0; i < Entrants; i++) Move(i, commands[i]);
            for (int i = 0; i < Entrants; i++) ResolvePulse(i);

            // A finish only counts after all runners have advanced through this tick.
            while (finished < Entrants)
            {
                RunnerState first = null;
                foreach (RunnerState runner in runners)
                    if (runner.FinishTime >= 0 && runner.Place == 0 &&
                        (first == null || runner.FinishTime < first.FinishTime))
                        first = runner;
                if (first == null) break;
                first.Place = ++finished;
            }
            for (int i = 0; i < Entrants; i++)
            {
                RunnerState runner = runners[i];
                if (runner.FinishTime >= 0) continue;
                int place = finished + 1;
                for (int j = 0; j < Entrants; j++)
                    if (runners[j].FinishTime < 0 &&
                        (runners[j].Distance > runner.Distance ||
                         (j < i && runners[j].Distance == runner.Distance))) place++;
                runner.Place = place;
            }
            RecordRaceMetrics();

            bool raceOver = waitForAll ? finished == Entrants || Clock >= 120 : runners[0].FinishTime >= 0;
            if (raceOver) Phase = RacePhase.Finished;
        }

        internal int SimulationTick => tick;

        internal void ApplyNetworkState(int snapshotTick, RacePhase snapshotPhase,
            IReadOnlyList<Trap> sharedTraps)
        {
            tick = snapshotTick;
            Phase = snapshotPhase;
            traps.Clear();
            for (int i = 0; i < sharedTraps.Count; i++) traps.Add(sharedTraps[i]);
        }

        public static bool CanChangeLane(float distance, int currentLane, int requestedLane) =>
            distance < TunnelLockStart || distance >= TunnelLockEnd ||
            (currentLane == TunnelFastLane) == (requestedLane == TunnelFastLane);

        public bool HasPulseTarget(int runnerIndex)
        {
            RunnerState runner = runners[runnerIndex];
            for (int i = 0; i < Entrants; i++)
            {
                float ahead = runners[i].Distance - runner.Distance;
                if (ahead > 1 && ahead <= PulseRange &&
                    Math.Abs(runners[i].Lateral - runner.Lateral) <= 0.55f && CanTargetPulse(runners[i]))
                    return true;
            }
            return false;
        }

        private void Move(int index, RaceCommand command)
        {
            RunnerState runner = runners[index];
            if (runner.FinishTime >= 0) return;
            runner.Boost = Decay(runner.Boost);
            runner.Shield = Decay(runner.Shield);
            runner.Slow = Decay(runner.Slow);
            runner.Immunity = Decay(runner.Immunity);
            runner.ItemCooldown = Decay(runner.ItemCooldown);
            runner.RouteBoost = Decay(runner.RouteBoost);
            UpdateDraft(index);
            if (runner.Recovery > 0)
            {
                runner.Recovery = Decay(runner.Recovery);
                return;
            }

            int requestedLane = Math.Max(-1, Math.Min(1, runner.Lane + command.LaneDelta));
            if (CanChangeLane(runner.Distance, runner.Lane, requestedLane)) runner.Lane = requestedLane;
            runner.Lateral += Math.Max(-5 * StepSeconds,
                Math.Min(5 * StepSeconds, runner.Lane - runner.Lateral));
            int occupiedLane = (int)Math.Round(runner.Lateral);
            if (command.Jump && runner.Height <= 0) runner.VerticalSpeed = 7.8f;
            if (runner.Height > 0 || runner.VerticalSpeed > 0)
            {
                runner.Height = Math.Max(0, runner.Height + runner.VerticalSpeed * StepSeconds);
                runner.VerticalSpeed -= 18 * StepSeconds;
            }

            if (command.UseItem && runner.ItemCooldown <= 0 && runner.Held != Gadget.None)
            {
                bool used = true;
                switch (runner.Held)
                {
                    case Gadget.Tailwind: runner.Boost = 2.2f; break;
                    case Gadget.ShellFilm: runner.Shield = 5; break;
                    case Gadget.BristlePad:
                        traps.Add(new Trap(index, occupiedLane, runner.Distance - 1.5f, Clock + 8));
                        break;
                    case Gadget.PulseShot:
                        used = TryFirePulse(index);
                        break;
                }
                if (used)
                {
                    runner.Held = Gadget.None;
                    runner.ItemCooldown = UseCooldown;
                }
            }

            float previous = runner.Distance;
            float pace = BaseSpeed;
            if (runner.Boost > 0) pace *= 1.35f;
            if (runner.RouteBoost > 0) pace *= 1.28f;
            if (runner.Slipstream >= SlipstreamActiveThreshold) pace *= 1.16f;
            if (occupiedLane == TunnelFastLane && previous >= TunnelStart && previous < TunnelEnd)
                pace *= TunnelSpeedMultiplier;
            if (runner.Slow > 0) pace *= 0.58f;
            runner.Distance += pace * StepSeconds;

            for (int i = 0; i < Features.Count; i++)
            {
                CourseFeature feature = Features[i];
                if (feature.Shortcut)
                {
                    if (runner.OnShortcut && occupiedLane != feature.Lane) runner.OnShortcut = false;
                    if (occupiedLane == feature.Lane && previous < feature.Start && runner.Distance >= feature.Start)
                        runner.OnShortcut = true;
                }
                if (feature.Lane != occupiedLane || runner.Distance < feature.Start || previous >= feature.End)
                    continue;
                if (feature.Item != Gadget.None)
                {
                    if (runner.Height < 1 && runner.Held == Gadget.None && IsPickupReady(i, index))
                    {
                        runner.Held = RollGadget(index, i, runner.Place);
                        runner.PickupMask |= GateBits[i];
                    }
                }
                else if (feature.Gap && runner.Distance < feature.End && runner.Height <= 0)
                {
                    if (feature.Shortcut)
                    {
                        runner.OnShortcut = false;
                        runner.ShortcutFalls++;
                        Metrics.ShortcutFalls++;
                    }
                    runner.Distance = Checkpoints[runner.Checkpoint] - 1;
                    runner.Lane = 0;
                    runner.Lateral = 0;
                    runner.Recovery = 0.85f;
                    runner.Immunity = 1.5f;
                    return;
                }
                else if (feature.Shortcut && runner.OnShortcut && runner.Distance >= feature.End)
                {
                    runner.OnShortcut = false;
                    runner.ShortcutClears++;
                    Metrics.ShortcutClears++;
                    runner.RouteBoost = ShortcutBoostSeconds;
                }
                else if (!feature.Gap && previous < feature.Start && runner.Height < 0.65f)
                {
                    Hit(runner);
                }
            }

            for (int i = traps.Count - 1; i >= 0; i--)
            {
                Trap trap = traps[i];
                if (trap.Owner == index || trap.Lane != occupiedLane || runner.Height > 0.35f ||
                    previous >= trap.Distance || runner.Distance < trap.Distance) continue;
                traps.RemoveAt(i);
                Hit(runner);
            }

            if (runner.Checkpoint + 1 < Checkpoints.Count &&
                runner.Distance >= Checkpoints[runner.Checkpoint + 1]) runner.Checkpoint++;
            if (runner.Distance >= CourseLength)
            {
                float fraction = (CourseLength - previous) / (runner.Distance - previous);
                runner.FinishTime = Clock + fraction * StepSeconds;
                runner.Distance = CourseLength;
                runner.Place = 0;
            }
        }

        private static uint[] BuildGateBits()
        {
            var bits = new uint[Features.Count];
            for (int i = 0; i < bits.Length; i++)
            {
                if (Features[i].Item == Gadget.None) continue;
                bits[i] = 1u << i;
                if (i > 0 && Features[i - 1].Start == Features[i].Start && bits[i - 1] != 0)
                    bits[i] = bits[i - 1];
            }
            return bits;
        }

        private Gadget RollGadget(int runner, int feature, int place)
        {
            // The opening gate introduces the speed item before ranked pickup rolls.
            if (Features[feature].Start == 43) return Gadget.Tailwind;
            int rank = Math.Max(1, Math.Min(Entrants, place));
            uint value = unchecked((raceSeed ^ GateBits[feature]) + (uint)runner * 0x9E3779B9u);
            value = unchecked((value ^ (value >> 16)) * 0x7FEB352Du);
            value = unchecked((value ^ (value >> 15)) * 0x846CA68Bu);
            int roll = (int)((value ^ (value >> 16)) % 100);
            int tailwind = 18 + 9 * (rank - 1);
            int pulse = 12 + 7 * (rank - 1);
            int shell = 27 - 5 * (rank - 1);
            if (roll < tailwind) return Gadget.Tailwind;
            if (roll < tailwind + pulse) return Gadget.PulseShot;
            return roll < tailwind + pulse + shell ? Gadget.ShellFilm : Gadget.BristlePad;
        }

        public bool HasDraftRival(int index)
        {
            RunnerState runner = runners[index];
            for (int i = 0; i < Entrants; i++)
                if (CanDraft(runner.Distance, runner.Lateral, runners[i].Distance,
                        runners[i].Lateral, runners[i].Height, runners[i])) return true;
            return false;
        }

        private void UpdateDraft(int index)
        {
            RunnerState runner = runners[index];
            if (runner.Recovery > 0 || runner.Slow > 0)
            {
                runner.Slipstream = 0;
                return;
            }
            if (runner.Slipstream >= SlipstreamActiveThreshold)
            {
                float remaining = runner.Slipstream - 0.16f * StepSeconds;
                runner.Slipstream = remaining >= SlipstreamActiveThreshold ? remaining : 0;
                return;
            }

            bool drafting = false;
            if (priorHeight[index] < 0.35f)
            {
                for (int i = 0; i < Entrants; i++)
                {
                    if (!CanDraft(priorDistance[index], priorLateral[index], priorDistance[i],
                            priorLateral[i], priorHeight[i], runners[i])) continue;
                    drafting = true;
                    break;
                }
            }
            float charge = runner.Slipstream + (drafting ? 0.62f : -0.42f) * StepSeconds;
            runner.Slipstream = charge >= SlipstreamActiveThreshold ? 1 : Math.Max(0, charge);
        }

        private static bool CanDraft(float distance, float lateral, float rivalDistance, float rivalLateral,
            float rivalHeight, RunnerState rival)
        {
            float ahead = rivalDistance - distance;
            return ahead >= 1.25f && ahead <= 9 && Math.Abs(rivalLateral - lateral) <= 0.36f &&
                   rivalHeight < 0.35f && rival.Recovery <= 0 && rival.FinishTime < 0;
        }

        private static bool CanTargetPulse(RunnerState runner) =>
            runner.FinishTime < 0 && runner.Recovery <= 0 && runner.Immunity <= 0 && runner.PulseOwner < 0;

        private bool TryFirePulse(int owner)
        {
            int target = -1;
            float closest = PulseRange;
            for (int i = 0; i < Entrants; i++)
            {
                float ahead = priorDistance[i] - priorDistance[owner];
                if (ahead <= 1 || ahead > closest || Math.Abs(priorLateral[i] - priorLateral[owner]) > 0.55f ||
                    !CanTargetPulse(runners[i])) continue;
                if (ahead == closest && target >= 0) continue;
                closest = ahead;
                target = i;
            }
            if (target < 0) return false;
            RunnerState rival = runners[target];
            rival.PulseLane = (int)Math.Round(priorLateral[target]);
            rival.PulseOwner = owner;
            rival.PulseWarning = PulseWindup;
            newWarnings[target] = true;
            return true;
        }

        private void ResolvePulse(int targetIndex)
        {
            RunnerState target = runners[targetIndex];
            if (target.PulseOwner < 0 || newWarnings[targetIndex]) return;
            target.PulseWarning = Decay(target.PulseWarning);
            if (target.PulseWarning > 0) return;
            int owner = target.PulseOwner;
            target.PulseOwner = -1;
            if (target.FinishTime >= 0 || target.Recovery > 0 || target.Immunity > 0) return;
            if (Math.Abs(target.Lateral - target.PulseLane) >= 0.50f || target.Height >= 0.55f)
            {
                target.PulseDodges++;
                Metrics.PulseDodges++;
                return;
            }
            if (target.Shield > 0)
            {
                target.PulseBlocks++;
                Metrics.PulseBlocks++;
            }
            else
            {
                runners[owner].PulseHits++;
                Metrics.PulseHits++;
            }
            Hit(target);
        }

        private void RecordRaceMetrics()
        {
            int currentLeader = 0;
            float trailing = runners[0].Distance;
            for (int i = 1; i < Entrants; i++)
            {
                if (runners[i].Place == 1) currentLeader = i;
                trailing = Math.Min(trailing, runners[i].Distance);
            }
            float front = runners[currentLeader].Distance;
            if (front >= 5 && currentLeader != leader)
            {
                if (leader >= 0) Metrics.LeadChanges++;
                leader = currentLeader;
            }
            if (nextGapCheckpoint < Checkpoints.Count && front >= Checkpoints[nextGapCheckpoint])
            {
                Metrics.RecordGap(nextGapCheckpoint, front - trailing);
                nextGapCheckpoint++;
            }
        }

        private static void Hit(RunnerState runner)
        {
            if (runner.Immunity > 0) return;
            if (runner.Shield > 0) runner.Shield = 0;
            else runner.Slow = 1.2f;
            runner.Immunity = 0.7f;
        }

        private static float Decay(float timer) => Math.Max(0, timer - StepSeconds);
    }
}






