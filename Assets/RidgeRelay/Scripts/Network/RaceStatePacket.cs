using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RidgeRelay
{
    internal sealed class RaceStatePacket
    {
        private const int MaxTraps = 32;
        private const int RunnerBytes = 3 + 9 * sizeof(float);
        private const int HeaderBytes = 4 * sizeof(int) + 2;
        private const int TrapBytes = 2 + 2 * sizeof(float);
        internal const int Capacity = HeaderBytes + RunnerBytes * RaceSimulation.Entrants + TrapBytes * MaxTraps;

        internal int Match;
        internal int Tick;
        internal int AcknowledgedInput;
        internal RacePhase Phase;
        private uint readyMask;
        private readonly RunnerPacket[] runners = new RunnerPacket[RaceSimulation.Entrants];
        private readonly List<RaceSimulation.Trap> traps = new List<RaceSimulation.Trap>(MaxTraps);

        internal static void Write(FastBufferWriter writer, RaceSimulation race, int match, int ack)
        {
            uint pickups = 0;
            for (int i = 0; i < RaceSimulation.Features.Count; ++i)
                if (race.IsPickupReady(i)) pickups |= 1u << i;

            int trapCount = Mathf.Min(race.Traps.Count, MaxTraps);
            writer.WriteValueSafe(match);
            writer.WriteValueSafe(race.SimulationTick);
            writer.WriteValueSafe(ack);
            writer.WriteValueSafe(pickups);
            writer.WriteValueSafe((byte)race.Phase);
            writer.WriteValueSafe((byte)trapCount);
            foreach (RunnerState runner in race.Runners) new RunnerPacket(runner).Write(writer);
            for (int i = race.Traps.Count - trapCount; i < race.Traps.Count; ++i)
            {
                RaceSimulation.Trap trap = race.Traps[i];
                writer.WriteValueSafe((byte)trap.Owner);
                writer.WriteValueSafe((byte)(trap.Lane + 1));
                writer.WriteValueSafe(trap.Distance);
                writer.WriteValueSafe(trap.Expires);
            }
        }

        internal bool TryRead(FastBufferReader reader)
        {
            if (!reader.TryBeginRead(HeaderBytes + RunnerBytes * RaceSimulation.Entrants)) return false;
            reader.ReadValue(out Match);
            reader.ReadValue(out Tick);
            reader.ReadValue(out AcknowledgedInput);
            reader.ReadValue(out readyMask);
            reader.ReadValue(out byte phase);
            reader.ReadValue(out byte trapCount);
            if (Match <= 0 || Tick < -180 || Tick > 60 * 125 || AcknowledgedInput < 0 ||
                phase > (byte)RacePhase.Finished || trapCount > MaxTraps) return false;
            Phase = (RacePhase)phase;
            for (int i = 0; i < runners.Length; ++i)
                if (!runners[i].TryRead(ref reader)) return false;
            if (!reader.TryBeginRead(TrapBytes * trapCount)) return false;
            traps.Clear();
            for (int i = 0; i < trapCount; ++i)
            {
                reader.ReadValue(out byte owner);
                reader.ReadValue(out byte lane);
                reader.ReadValue(out float distance);
                reader.ReadValue(out float expires);
                if (owner >= RaceSimulation.Entrants || lane > 2 ||
                    !InRange(distance, -4, RaceSimulation.CourseLength) || !InRange(expires, 0, 130))
                    return false;
                traps.Add(new RaceSimulation.Trap(owner, lane - 1, distance, expires));
            }

            return reader.Position == reader.Length;
        }

        internal void Present(RaceSimulation display, RaceStatePacket previous, float fraction)
        {
            display.ApplyNetworkState(Tick, Phase, readyMask, traps);
            for (int i = 0; i < runners.Length; ++i)
            {
                RunnerPacket current = runners[i];
                RunnerPacket prior = previous.runners[i];
                float blend = previous.Match == Match && Mathf.Abs(current.Distance - prior.Distance) < 8
                    ? fraction : 1;
                current.Apply(display.Runners[i], prior, blend);
            }
        }

        private static bool InRange(float value, float min, float max) => value >= min && value <= max;

        private struct RunnerPacket
        {
            private byte lane, held, place;
            internal float Distance;
            private float lateral, height, finish, shield, boost, slow, recovery, immunity;

            internal RunnerPacket(RunnerState runner)
            {
                lane = (byte)(runner.Lane + 1);
                held = (byte)runner.Held;
                place = (byte)runner.Place;
                lateral = runner.Lateral;
                Distance = runner.Distance;
                height = runner.Height;
                finish = runner.FinishTime;
                shield = runner.Shield;
                boost = runner.Boost;
                slow = runner.Slow;
                recovery = runner.Recovery;
                immunity = runner.Immunity;
            }

            internal void Write(FastBufferWriter writer)
            {
                writer.WriteValueSafe(lane);
                writer.WriteValueSafe(held);
                writer.WriteValueSafe(place);
                writer.WriteValueSafe(lateral);
                writer.WriteValueSafe(Distance);
                writer.WriteValueSafe(height);
                writer.WriteValueSafe(finish);
                writer.WriteValueSafe(shield);
                writer.WriteValueSafe(boost);
                writer.WriteValueSafe(slow);
                writer.WriteValueSafe(recovery);
                writer.WriteValueSafe(immunity);
            }

            internal bool TryRead(ref FastBufferReader reader)
            {
                reader.ReadValue(out lane);
                reader.ReadValue(out held);
                reader.ReadValue(out place);
                reader.ReadValue(out lateral);
                reader.ReadValue(out Distance);
                reader.ReadValue(out height);
                reader.ReadValue(out finish);
                reader.ReadValue(out shield);
                reader.ReadValue(out boost);
                reader.ReadValue(out slow);
                reader.ReadValue(out recovery);
                reader.ReadValue(out immunity);
                return lane <= 2 && held <= (byte)Gadget.ShellFilm &&
                    place >= 1 && place <= RaceSimulation.Entrants &&
                    InRange(lateral, -1.01f, 1.01f) && InRange(Distance, -2, RaceSimulation.CourseLength) &&
                    InRange(height, 0, 3) && InRange(finish, -1, 125) &&
                    InRange(shield, 0, 5) && InRange(boost, 0, 2.2f) &&
                    InRange(slow, 0, 1.2f) && InRange(recovery, 0, 0.85f) && InRange(immunity, 0, 1.5f);
            }

            internal void Apply(RunnerState runner, RunnerPacket prior, float blend)
            {
                runner.Lane = lane - 1;
                runner.Held = (Gadget)held;
                runner.Place = place;
                runner.Lateral = Mathf.Lerp(prior.lateral, lateral, blend);
                runner.Distance = Mathf.Lerp(prior.Distance, Distance, blend);
                runner.Height = Mathf.Lerp(prior.height, height, blend);
                runner.FinishTime = finish;
                runner.Shield = shield;
                runner.Boost = boost;
                runner.Slow = slow;
                runner.Recovery = recovery;
                runner.Immunity = immunity;
            }
        }
    }
}
