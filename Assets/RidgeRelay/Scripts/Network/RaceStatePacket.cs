using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace RidgeRelay
{
    internal sealed class RaceStatePacket
    {
        private const int MaxTraps = 32;
        private const int RunnerBytes = 20 + sizeof(uint) + 16 * sizeof(float);
        private const int HeaderBytes = 3 * sizeof(int) + 2;
        private const int TrapBytes = 2 + 2 * sizeof(float);
        internal const int Capacity = HeaderBytes + RunnerBytes * RaceSimulation.Entrants + TrapBytes * MaxTraps;

        internal int Match;
        internal int Tick;
        internal int AcknowledgedInput;
        internal RacePhase Phase;
        private readonly RunnerPacket[] runners = new RunnerPacket[RaceSimulation.Entrants];
        private readonly List<RaceSimulation.Trap> traps = new List<RaceSimulation.Trap>(MaxTraps);

        internal static void Write(FastBufferWriter writer, RaceSimulation race, int match, int ack)
        {
            int trapCount = Mathf.Min(race.Traps.Count, MaxTraps);
            writer.WriteValueSafe(match);
            writer.WriteValueSafe(race.SimulationTick);
            writer.WriteValueSafe(ack);
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
            display.ApplyNetworkState(Tick, Phase, traps);
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
            private byte lane, held, place, pulseLane, pulseOwner;
            private byte pulseHits, pulseDodges, pulseBlocks, shortcutClears, shortcutFalls;
            private uint pickupMask;
            internal float Distance;
            private float lateral, height, finish, shield, boost, slow, recovery, immunity;
            private float slipstream, pulseWarning, itemCooldown, routeBoost;
            private byte incomingPower, powerOwner, powerLane, castPower, impactPower;
            private byte furColor;
            private byte powerResolution, lastPowerHits, lastResolvedPower, lastUsedPower;
            private float powerWarning, castTime, impactTime;

            internal RunnerPacket(RunnerState runner)
            {
                lane = (byte)(runner.Lane + 1);
                furColor = (byte)runner.FurColor;
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
                pickupMask = runner.PickupMask;
                slipstream = runner.Slipstream;
                pulseWarning = runner.PulseWarning;
                pulseLane = (byte)(runner.PulseLane + 1);
                pulseOwner = (byte)(runner.PulseOwner + 1);
                itemCooldown = runner.ItemCooldown;
                routeBoost = runner.RouteBoost;
                pulseHits = (byte)Mathf.Min(byte.MaxValue, runner.PulseHits);
                pulseDodges = (byte)Mathf.Min(byte.MaxValue, runner.PulseDodges);
                pulseBlocks = (byte)Mathf.Min(byte.MaxValue, runner.PulseBlocks);
                shortcutClears = (byte)Mathf.Min(byte.MaxValue, runner.ShortcutClears);
                shortcutFalls = (byte)Mathf.Min(byte.MaxValue, runner.ShortcutFalls);
                incomingPower = (byte)runner.IncomingPower;
                powerOwner = (byte)(runner.PowerOwner + 1);
                powerLane = (byte)(runner.PowerLane + 1);
                castPower = (byte)runner.CastPower;
                impactPower = (byte)runner.ImpactPower;
                powerWarning = runner.PowerWarning;
                castTime = runner.CastTime;
                impactTime = runner.ImpactTime;
                powerResolution = (byte)runner.PowerResolution;
                lastPowerHits = (byte)runner.LastPowerHits;
                lastResolvedPower = (byte)runner.LastResolvedPower;
                lastUsedPower = (byte)runner.LastUsedPower;
            }

            internal void Write(FastBufferWriter writer)
            {
                writer.WriteValueSafe(lane);
                writer.WriteValueSafe(held);
                writer.WriteValueSafe(place);
                writer.WriteValueSafe(furColor);
                writer.WriteValueSafe(lateral);
                writer.WriteValueSafe(Distance);
                writer.WriteValueSafe(height);
                writer.WriteValueSafe(finish);
                writer.WriteValueSafe(shield);
                writer.WriteValueSafe(boost);
                writer.WriteValueSafe(slow);
                writer.WriteValueSafe(recovery);
                writer.WriteValueSafe(immunity);
                writer.WriteValueSafe(pickupMask);
                writer.WriteValueSafe(slipstream);
                writer.WriteValueSafe(pulseWarning);
                writer.WriteValueSafe(pulseLane);
                writer.WriteValueSafe(pulseOwner);
                writer.WriteValueSafe(itemCooldown);
                writer.WriteValueSafe(routeBoost);
                writer.WriteValueSafe(pulseHits);
                writer.WriteValueSafe(pulseDodges);
                writer.WriteValueSafe(pulseBlocks);
                writer.WriteValueSafe(shortcutClears);
                writer.WriteValueSafe(shortcutFalls);
                writer.WriteValueSafe(incomingPower);
                writer.WriteValueSafe(powerOwner);
                writer.WriteValueSafe(powerLane);
                writer.WriteValueSafe(castPower);
                writer.WriteValueSafe(impactPower);
                writer.WriteValueSafe(powerWarning);
                writer.WriteValueSafe(castTime);
                writer.WriteValueSafe(impactTime);
                writer.WriteValueSafe(powerResolution);
                writer.WriteValueSafe(lastPowerHits);
                writer.WriteValueSafe(lastResolvedPower);
                writer.WriteValueSafe(lastUsedPower);
            }

            internal bool TryRead(ref FastBufferReader reader)
            {
                reader.ReadValue(out lane);
                reader.ReadValue(out held);
                reader.ReadValue(out place);
                reader.ReadValue(out furColor);
                reader.ReadValue(out lateral);
                reader.ReadValue(out Distance);
                reader.ReadValue(out height);
                reader.ReadValue(out finish);
                reader.ReadValue(out shield);
                reader.ReadValue(out boost);
                reader.ReadValue(out slow);
                reader.ReadValue(out recovery);
                reader.ReadValue(out immunity);
                reader.ReadValue(out pickupMask);
                reader.ReadValue(out slipstream);
                reader.ReadValue(out pulseWarning);
                reader.ReadValue(out pulseLane);
                reader.ReadValue(out pulseOwner);
                reader.ReadValue(out itemCooldown);
                reader.ReadValue(out routeBoost);
                reader.ReadValue(out pulseHits);
                reader.ReadValue(out pulseDodges);
                reader.ReadValue(out pulseBlocks);
                reader.ReadValue(out shortcutClears);
                reader.ReadValue(out shortcutFalls);
                reader.ReadValue(out incomingPower);
                reader.ReadValue(out powerOwner);
                reader.ReadValue(out powerLane);
                reader.ReadValue(out castPower);
                reader.ReadValue(out impactPower);
                reader.ReadValue(out powerWarning);
                reader.ReadValue(out castTime);
                reader.ReadValue(out impactTime);
                reader.ReadValue(out powerResolution);
                reader.ReadValue(out lastPowerHits);
                reader.ReadValue(out lastResolvedPower);
                reader.ReadValue(out lastUsedPower);
                return lane <= 2 && furColor < RaceSimulation.FurColorCount && held <= (byte)Gadget.Shock &&
                    lastPowerHits < RaceSimulation.Entrants && lastResolvedPower <= (byte)Gadget.Shock && lastUsedPower <= (byte)Gadget.Shock &&
                    incomingPower <= (byte)Gadget.Shock && castPower <= (byte)Gadget.Shock &&
                    impactPower <= (byte)Gadget.Shock && powerLane <= 2 && powerOwner <= RaceSimulation.Entrants &&
                    InRange(powerWarning, 0, RaceSimulation.RocketWindup) &&
                    InRange(castTime, 0, RaceSimulation.RocketWindup) && InRange(impactTime, 0, 0.55f) &&
                    pulseLane <= 2 && pulseOwner <= RaceSimulation.Entrants &&
                    InRange(slipstream, 0, 1) && InRange(pulseWarning, 0, 0.65f) &&
                    InRange(itemCooldown, 0, 1.2f) && InRange(routeBoost, 0, 2.4f) &&
                    place >= 1 && place <= RaceSimulation.Entrants &&
                    InRange(lateral, -1.01f, 1.01f) && InRange(Distance, -2, RaceSimulation.CourseLength) &&
                    InRange(height, 0, 3) && InRange(finish, -1, 125) &&
                    InRange(shield, 0, 5) && InRange(boost, 0, 2.2f) &&
                    InRange(slow, 0, 1.8f) && InRange(recovery, 0, 0.85f) && InRange(immunity, 0, 1.5f);
            }

            internal void Apply(RunnerState runner, RunnerPacket prior, float blend)
            {
                runner.Lane = lane - 1;
                runner.FurColor = furColor;
                runner.PowerResolution = powerResolution;
                runner.LastPowerHits = lastPowerHits;
                runner.LastResolvedPower = (Gadget)lastResolvedPower;
                runner.LastUsedPower = (Gadget)lastUsedPower;
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
                runner.PickupMask = pickupMask;
                runner.Slipstream = slipstream;
                runner.PulseWarning = pulseWarning;
                runner.PulseLane = pulseLane - 1;
                runner.PulseOwner = pulseOwner - 1;
                runner.ItemCooldown = itemCooldown;
                runner.RouteBoost = routeBoost;
                runner.PulseHits = pulseHits;
                runner.PulseDodges = pulseDodges;
                runner.PulseBlocks = pulseBlocks;
                runner.ShortcutClears = shortcutClears;
                runner.ShortcutFalls = shortcutFalls;
                runner.IncomingPower = (Gadget)incomingPower;
                runner.PowerOwner = powerOwner - 1;
                runner.PowerLane = powerLane - 1;
                runner.CastPower = (Gadget)castPower;
                runner.ImpactPower = (Gadget)impactPower;
                runner.PowerWarning = powerWarning;
                runner.CastTime = castTime;
                runner.ImpactTime = impactTime;
            }
        }
    }
}
