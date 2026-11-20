using System;

namespace RidgeRelay
{
    public sealed class RaceBot
    {
        private readonly Random random;
        private readonly bool takesShortcuts;
        private float nextDecision;
        private float pulseReactionAt;
        private bool sawPulse;

        public RaceBot(int seed)
        {
            random = new Random(seed);
            takesShortcuts = random.NextDouble() < 0.70;
        }

        public RaceCommand Decide(RaceSimulation race, int index)
        {
            RunnerState runner = race.Runners[index];
            if (race.Phase != RacePhase.Running || runner.Recovery > 0 || runner.FinishTime >= 0)
                return default;

            if (runner.PulseWarning > 0 && !sawPulse)
            {
                // Seeded attention lapses and reaction times leave an opening for a well-timed shot.
                pulseReactionAt = random.NextDouble() < 0.16
                    ? float.PositiveInfinity
                    : race.Clock + 0.14f + 0.24f * (float)random.NextDouble();
            }
            sawPulse = runner.PulseWarning > 0;
            bool reactToPulse = sawPulse && race.Clock >= pulseReactionAt;

            const float shortcutLip = 387;
            float approach = shortcutLip - runner.Distance;
            bool racingShortcut = takesShortcuts && runner.Slow <= 0 && runner.PulseWarning <= 0 &&
                                  approach > 0 && approach <= 12;
            if (racingShortcut && runner.Lane == -1 && approach <= 1.15f && runner.Height <= 0)
                return new RaceCommand(jump: true);

            if (race.Clock < nextDecision) return default;
            nextDecision = race.Clock + 0.08f + 0.1f * (float)random.NextDouble();

            if (reactToPulse && Math.Abs(runner.Lateral - runner.PulseLane) < 0.55f &&
                runner.Shield <= 0)
            {
                if (runner.Held == Gadget.ShellFilm && runner.ItemCooldown <= 0)
                    return new RaceCommand(useItem: true);
                int escape = FindSafeLane(runner, index);
                if (escape != 0 && runner.PulseWarning >= 0.15f)
                    return new RaceCommand(escape);
                if (runner.PulseWarning <= 0.34f && runner.Height <= 0)
                    return new RaceCommand(jump: true);
            }

            int laneDelta = 0;
            bool jump = false;
            bool urgent = false;
            for (int i = 0; i < RaceSimulation.Features.Count; i++)
            {
                CourseFeature feature = RaceSimulation.Features[i];
                float ahead = feature.Start - runner.Distance;
                if (ahead < 0 || ahead > 14) continue;

                if (feature.Item != Gadget.None)
                {
                    int toward = Math.Sign(feature.Lane - runner.Lane);
                    if (!race.HasDraftRival(index) && runner.Held == Gadget.None && ahead > 4 &&
                        race.IsPickupReady(i, index) && IsSafe(runner, runner.Lane + toward, 11) &&
                        random.NextDouble() < 0.35)
                        laneDelta = toward;
                    continue;
                }

                if (feature.Lane != runner.Lane) continue;
                if (feature.Gap && ahead < 9 && !(feature.Shortcut && racingShortcut))
                {
                    laneDelta = FindSafeLane(runner, index);
                    jump = laneDelta == 0 && ahead < 1.5f;
                    urgent = true;
                    break;
                }
                if (!feature.Gap && ahead < 2.8f)
                {
                    jump = true;
                    urgent = true;
                    break;
                }
            }

            if (!urgent)
            {
                if (racingShortcut) laneDelta = Math.Sign(-1 - runner.Lane);
                else if (runner.Distance > 423 && runner.Distance < RaceSimulation.TunnelLockStart &&
                         runner.Lane != RaceSimulation.TunnelFastLane)
                    laneDelta = 1;
                else if (laneDelta == 0 && !race.HasDraftRival(index) && runner.Slipstream < 0.2f)
                    laneDelta = PursueRival(race, index);
            }

            bool useItem = runner.ItemCooldown <= 0 && (runner.Held switch
            {
                Gadget.Tailwind => !urgent && !racingShortcut && IsSafe(runner, runner.Lane, 13),
                Gadget.ShellFilm => reactToPulse || TrapAhead(race, index),
                Gadget.BristlePad => RivalBehind(race, index),
                Gadget.PulseShot => race.HasPulseTarget(index),
                _ => false
            });
            return new RaceCommand(laneDelta, jump, useItem);
        }

        private static int FindSafeLane(RunnerState runner, int index)
        {
            int preferred = runner.Lane == 0 ? (index % 2 == 0 ? -1 : 1) : -runner.Lane;
            if (IsSafe(runner, runner.Lane + preferred, 10)) return preferred;
            if (IsSafe(runner, runner.Lane - preferred, 10)) return -preferred;
            return 0;
        }

        private static bool IsSafe(RunnerState runner, int lane, float horizon)
        {
            if (lane < -1 || lane > 1 || !RaceSimulation.CanChangeLane(runner.Distance, runner.Lane, lane))
                return false;
            foreach (CourseFeature feature in RaceSimulation.Features)
            {
                if (feature.Start > runner.Distance + horizon) break;
                if (feature.Lane == lane && feature.Item == Gadget.None && feature.End > runner.Distance)
                    return false;
            }
            return true;
        }

        private static int PursueRival(RaceSimulation race, int index)
        {
            RunnerState runner = race.Runners[index];
            int toward = 0;
            float closest = runner.Held == Gadget.PulseShot ? RaceSimulation.PulseRange : 8;
            for (int i = 0; i < RaceSimulation.Entrants; i++)
            {
                RunnerState rival = race.Runners[i];
                float ahead = rival.Distance - runner.Distance;
                int step = Math.Sign(rival.Lane - runner.Lane);
                if (ahead <= 1.6f || ahead > closest || step == 0 || rival.Recovery > 0 ||
                    rival.FinishTime >= 0 || !IsSafe(runner, runner.Lane + step, 12)) continue;
                toward = step;
                closest = ahead;
            }
            return toward;
        }

        private static bool TrapAhead(RaceSimulation race, int index)
        {
            RunnerState runner = race.Runners[index];
            foreach (RaceSimulation.Trap trap in race.Traps)
            {
                float ahead = trap.Distance - runner.Distance;
                if (trap.Owner != index && trap.Lane == runner.Lane && ahead > 0 && ahead < 7)
                    return true;
            }
            return false;
        }

        private static bool RivalBehind(RaceSimulation race, int index)
        {
            RunnerState runner = race.Runners[index];
            for (int i = 0; i < RaceSimulation.Entrants; i++)
            {
                RunnerState rival = race.Runners[i];
                float behind = runner.Distance - rival.Distance;
                if (behind >= 2 && behind <= 9 && rival.Lane == runner.Lane) return true;
            }
            return false;
        }
    }
}
