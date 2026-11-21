using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidgeRelay
{
    public static class RacerPresentationLayout
    {
        private const float Spacing = 0.8f;

        public static void Resolve(IReadOnlyList<RunnerState> runners, Vector2[] offsets)
        {
            int count = Mathf.Min(Mathf.Min(runners.Count, offsets.Length), RaceSimulation.Entrants);
            Span<Vector2> positions = stackalloc Vector2[RaceSimulation.Entrants];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = Vector2.zero;
                positions[i] = new Vector2(runners[i].Lateral * RaceSimulation.LaneWidth, runners[i].Distance);
                if (!Active(runners[i])) continue;
                for (int j = 0; j < count; j++)
                {
                    if (i == j || !Active(runners[j]) || Mathf.Abs(runners[i].Height - runners[j].Height) > 1.4f) continue;
                    float along = runners[i].Distance - runners[j].Distance;
                    float across = (runners[i].Lateral - runners[j].Lateral) * RaceSimulation.LaneWidth;
                    if (along * along + across * across >= Spacing * Spacing) continue;
                    offsets[i] = new Vector2((i & 1) == 0 ? -0.025f : 0.025f, i < 2 ? -0.025f : 0.025f);
                    positions[i] += offsets[i];
                    break;
                }
            }

            // Four racers need only a small deterministic packing solve. These
            // offsets affect drawing; timing, hit tests and lane inputs use the race.
            for (int iteration = 0; iteration < 18; iteration++)
            {
                for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    if (!Active(runners[i]) || !Active(runners[j]) ||
                        Mathf.Abs(runners[i].Height - runners[j].Height) > 1.4f) continue;
                    Vector2 difference = positions[i] - positions[j];
                    float distance = difference.magnitude;
                    if (distance >= Spacing) continue;
                    Vector2 direction = distance > 0.0001f ? difference / distance : Vector2.left;
                    Vector2 correction = direction * ((Spacing - distance) * 0.52f);
                    Move(i, correction, runners, offsets, positions);
                    Move(j, -correction, runners, offsets, positions);
                }
            }
        }

        private static bool Active(RunnerState runner) => runner.Recovery <= 0 && runner.FinishTime < 0;

        private static void Move(int slot, Vector2 correction, IReadOnlyList<RunnerState> runners,
            Vector2[] offsets, Span<Vector2> positions)
        {
            Vector2 prior = offsets[slot];
            Vector2 next = prior + correction;
            RunnerState runner = runners[slot];
            float left = runner.Lateral > 0.5f && runner.Distance >= RaceSimulation.TunnelStart &&
                runner.Distance <= RaceSimulation.TunnelEnd ? -0.36f : -0.5f;
            next.x = Mathf.Clamp(next.x, left, 0.5f);
            next.y = Mathf.Clamp(next.y, -0.55f, 0.55f);
            offsets[slot] = next;
            positions[slot] += next - prior;
        }
    }
}
