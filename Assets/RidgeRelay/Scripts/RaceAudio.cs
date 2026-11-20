using UnityEngine;

namespace RidgeRelay
{
    public sealed class RaceAudio : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip countdown, start, finish, pickup, gadget, hit, landing, fall, warning;
        private Gadget previousItem;
        private float previousHeight;
        private float previousSlow;
        private float previousRecovery;
        private float previousShield;
        private int previousCount;
        private RacePhase previousPhase;
        private float nextWarning;
        private bool finished;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            countdown = Tone("Countdown", 430, 510, 0.13f);
            start = Tone("Go", 520, 880, 0.28f);
            finish = Tone("Finish", 523, 1047, 0.55f);
            pickup = Tone("Pickup", 620, 970, 0.15f);
            gadget = Tone("Gadget", 260, 720, 0.23f);
            hit = Tone("Hit", 220, 85, 0.22f);
            landing = Tone("Land", 120, 60, 0.085f);
            fall = Tone("Fall", 300, 90, 0.38f);
            warning = Tone("Warning", 180, 290, 0.22f);
        }

        public void ResetRace()
        {
            previousItem = Gadget.None;
            previousHeight = previousSlow = previousRecovery = previousShield = nextWarning = 0;
            previousCount = 4;
            finished = false;
            previousPhase = RacePhase.Countdown;
            source.Stop();
        }

        public void Present(RaceSimulation race, int slot = 0)
        {
            RunnerState player = race.Runners[slot];
            if (!finished && player.FinishTime >= 0)
            {
                source.PlayOneShot(finish, 0.48f);
                finished = true;
            }
            if (race.Phase == RacePhase.Countdown)
            {
                int count = Mathf.CeilToInt(-race.Clock);
                if (count != previousCount) source.PlayOneShot(countdown, 0.32f);
                previousCount = count;
            }
            else if (!finished && race.Phase == RacePhase.Running)
            {
                if (previousPhase == RacePhase.Countdown) source.PlayOneShot(start, 0.46f);
                if (player.Held != previousItem)
                    source.PlayOneShot(player.Held == Gadget.None ? gadget : pickup, 0.34f);
                if (player.Recovery > 0 && previousRecovery <= 0) source.PlayOneShot(fall, 0.38f);
                if ((previousSlow <= 0 && player.Slow > 0) ||
                    (previousShield > 0.05f && player.Shield <= 0)) source.PlayOneShot(hit, 0.38f);
                if (previousHeight > 0 && player.Height <= 0) source.PlayOneShot(landing, 0.24f);

                if (race.Clock >= nextWarning)
                {
                    foreach (var trap in race.Traps)
                    {
                        float ahead = trap.Distance - player.Distance;
                        if (trap.Owner == slot || trap.Lane != player.Lane || ahead <= 0 || ahead > 12)
                            continue;
                        source.PlayOneShot(warning, 0.38f);
                        nextWarning = race.Clock + 0.8f;
                        break;
                    }
                }
            }

            previousPhase = race.Phase;
            previousItem = player.Held;
            previousHeight = player.Height;
            previousSlow = player.Slow;
            previousRecovery = player.Recovery;
            previousShield = player.Shield;
        }

        private static AudioClip Tone(string name, float initialPitch, float finalPitch, float duration)
        {
            const int sampleRate = 22050;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            var wave = new float[samples];
            float phase = 0;
            for (int i = 0; i < samples; i++)
            {
                float progress = (float)i / (samples - 1);
                phase += 2 * Mathf.PI * Mathf.Lerp(initialPitch, finalPitch, progress) / sampleRate;
                float envelope = Mathf.Min(1, progress * 30) * (1 - progress) * (1 - progress);
                wave[i] = (Mathf.Sin(phase) + 0.16f * Mathf.Sin(phase * 2)) * envelope * 0.65f;
            }
            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(wave, 0);
            return clip;
        }
    }
}


