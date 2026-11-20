using System;
using System.Collections.Generic;

namespace RidgeRelay
{
    public enum Gadget { None, Tailwind, BristlePad, ShellFilm }
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
        internal float VerticalSpeed;
        internal int Checkpoint;
    }

    public readonly struct CourseFeature
    {
        public readonly float Start;
        public readonly float End;
        public readonly int Lane;
        public readonly Gadget Item;
        public readonly bool Gap;

        public CourseFeature(float start, float end, int lane, Gadget item = Gadget.None, bool gap = false)
        {
            Start = start;
            End = end;
            Lane = lane;
            Item = item;
            Gap = gap;
        }
    }

    public sealed class RaceSimulation
    {
        public const float StepSeconds = 1f / 60;
        public const float CourseLength = 655;
        public const int Entrants = 4;
        public const float LaneWidth = 2;
        public const float BaseSpeed = 9;
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
            new CourseFeature(154, 160, 0, gap: true),
            new CourseFeature(182, 183, -1),
            new CourseFeature(200, 203, 1, Gadget.Tailwind),
            new CourseFeature(219, 220, 1),
            new CourseFeature(252, 258, 1, gap: true),
            new CourseFeature(274, 277, -1, Gadget.ShellFilm),
            new CourseFeature(304, 305, 0),
            new CourseFeature(332, 336, -1, Gadget.BristlePad),
            new CourseFeature(363, 364, 1),
            new CourseFeature(387, 393, -1, gap: true),
            new CourseFeature(414, 417, 0, Gadget.Tailwind),
            new CourseFeature(443, 444, 0),
            new CourseFeature(482, 488, 0, gap: true),
            new CourseFeature(514, 517, 1, Gadget.BristlePad),
            new CourseFeature(552, 553, -1),
            new CourseFeature(574, 577, -1, Gadget.Tailwind),
            new CourseFeature(607, 613, 1, gap: true),
            new CourseFeature(632, 633, 0)
        });

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
        private readonly float[] pickupReady = new float[Features.Count];
        private readonly List<Trap> traps = new List<Trap>();
        private const int CountdownTicks = 180;
        private int tick = -CountdownTicks;
        private int finished;
        private readonly bool waitForAll;

        public IReadOnlyList<RunnerState> Runners => runners;
        public IReadOnlyList<Trap> Traps => traps;
        public bool IsPickupReady(int featureIndex) => Clock >= pickupReady[featureIndex];
        public RacePhase Phase { get; private set; }
        public float Clock => tick * StepSeconds;

        public RaceSimulation(bool waitForAll = false)
        {
            this.waitForAll = waitForAll;
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

            traps.RemoveAll(trap => trap.Expires <= Clock);
            for (int i = 0; i < Entrants; i++) Move(i, commands[i]);

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

            bool raceOver = waitForAll ? finished == Entrants || Clock >= 120 : runners[0].FinishTime >= 0;
            if (raceOver) Phase = RacePhase.Finished;
        }

        internal int SimulationTick => tick;

        internal void ApplyNetworkState(int snapshotTick, RacePhase snapshotPhase, uint readyMask,
            IReadOnlyList<Trap> sharedTraps)
        {
            tick = snapshotTick;
            Phase = snapshotPhase;
            traps.Clear();
            for (int i = 0; i < sharedTraps.Count; i++) traps.Add(sharedTraps[i]);
            for (int i = 0; i < Features.Count; i++)
                pickupReady[i] = (readyMask & (1u << i)) != 0 ? float.NegativeInfinity : float.PositiveInfinity;
        }

        private void Move(int index, RaceCommand command)
        {
            RunnerState runner = runners[index];
            if (runner.FinishTime >= 0) return;
            runner.Boost = Decay(runner.Boost);
            runner.Shield = Decay(runner.Shield);
            runner.Slow = Decay(runner.Slow);
            runner.Immunity = Decay(runner.Immunity);
            if (runner.Recovery > 0)
            {
                runner.Recovery = Decay(runner.Recovery);
                return;
            }

            runner.Lane = Math.Max(-1, Math.Min(1, runner.Lane + command.LaneDelta));
            runner.Lateral += Math.Max(-5 * StepSeconds,
                Math.Min(5 * StepSeconds, runner.Lane - runner.Lateral));
            int occupiedLane = (int)Math.Round(runner.Lateral);
            if (command.Jump && runner.Height <= 0) runner.VerticalSpeed = 7.8f;
            if (runner.Height > 0 || runner.VerticalSpeed > 0)
            {
                runner.Height = Math.Max(0, runner.Height + runner.VerticalSpeed * StepSeconds);
                runner.VerticalSpeed -= 18 * StepSeconds;
            }

            if (command.UseItem)
            {
                switch (runner.Held)
                {
                    case Gadget.Tailwind: runner.Boost = 2.2f; break;
                    case Gadget.ShellFilm: runner.Shield = 5; break;
                    case Gadget.BristlePad:
                        traps.Add(new Trap(index, occupiedLane, runner.Distance - 1.5f, Clock + 8));
                        break;
                }
                runner.Held = Gadget.None;
            }

            float previous = runner.Distance;
            float pace = BaseSpeed - (waitForAll ? 0 : 0.13f * index);
            if (runner.Boost > 0) pace *= 1.35f;
            if (runner.Slow > 0) pace *= 0.58f;
            runner.Distance += pace * StepSeconds;

            for (int i = 0; i < Features.Count; i++)
            {
                CourseFeature feature = Features[i];
                if (feature.Lane != occupiedLane || runner.Distance < feature.Start || previous >= feature.End)
                    continue;
                if (feature.Item != Gadget.None)
                {
                    if (runner.Height < 1 && runner.Held == Gadget.None && Clock >= pickupReady[i])
                    {
                        runner.Held = feature.Item;
                        pickupReady[i] = Clock + 2;
                    }
                }
                else if (feature.Gap && runner.Height <= 0)
                {
                    runner.Distance = Checkpoints[runner.Checkpoint] - 1;
                    runner.Lane = 0;
                    runner.Lateral = 0;
                    runner.Recovery = 0.85f;
                    runner.Immunity = 1.5f;
                    return;
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






