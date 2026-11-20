using System;

namespace RidgeRelay
{
    public sealed class RaceBot
    {
        private readonly Random random;
        private float nextDecision;

        public RaceBot(int seed) => random = new Random(seed);

        public RaceCommand Decide(RaceSimulation race, int index)
        {
            RunnerState runner = race.Runners[index];
            if (race.Phase != RacePhase.Running || race.Clock < nextDecision || runner.Recovery > 0)
                return default;
            nextDecision = race.Clock + 0.08f + 0.1f * (float)random.NextDouble();

            int laneDelta = 0;
            bool jump = false;
            for (int i = 0; i < RaceSimulation.Features.Count; i++)
            {
                CourseFeature feature = RaceSimulation.Features[i];
                float ahead = feature.Start - runner.Distance;
                if (ahead < 0 || ahead > 14) continue;

                if (feature.Item != Gadget.None)
                {
                    if (runner.Held == Gadget.None && ahead > 4 &&
                        race.IsPickupReady(i) && random.NextDouble() < 0.3)
                        laneDelta = Math.Sign(feature.Lane - runner.Lane);
                    continue;
                }

                if (feature.Lane != runner.Lane) continue;
                if (feature.Gap && ahead < 9)
                {
                    laneDelta = runner.Lane == 0 ? (index % 2 == 0 ? -1 : 1) : -runner.Lane;
                    break;
                }
                if (!feature.Gap && ahead < 4.5f)
                {
                    jump = true;
                    break;
                }
            }

            bool useItem = runner.Held switch
            {
                Gadget.Tailwind => true,
                Gadget.ShellFilm => jump || race.Traps.Count > 0,
                Gadget.BristlePad => random.NextDouble() < 0.1,
                _ => false
            };
            return new RaceCommand(laneDelta, jump, useItem);
        }
    }
}

