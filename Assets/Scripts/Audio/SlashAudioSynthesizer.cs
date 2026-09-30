using UnityEngine;

namespace VRSlashGame.Audio
{
    /// <summary>
    /// Procedurally synthesizes punchy slice, miss, and combo audio clips at runtime.
    /// Eliminates external asset dependencies and ensures zero missing audio errors.
    /// </summary>
    public static class SlashAudioSynthesizer
    {
        private const int SAMPLE_RATE = 44100;

        /// <summary>
        /// Watermelon / Red fruit: Heavy wet squish/splash with punchy sub-bass transient and chaotic liquid splatter.
        /// </summary>
        public static AudioClip CreateHeavySquishClip(string name = "Fruit_HeavySquish")
        {
            float duration = 0.28f;
            int totalSamples = Mathf.FloorToInt(SAMPLE_RATE * duration);
            float[] samples = new float[totalSamples];

            float phaseLow = 0f;
            float phaseMid = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / totalSamples;

                // Low bass impact thump (160 Hz -> 50 Hz)
                float freqLow = Mathf.Lerp(160f, 50f, Mathf.Pow(t, 0.4f));
                phaseLow += 2f * Mathf.PI * freqLow / SAMPLE_RATE;
                float subThump = Mathf.Sin(phaseLow) * Mathf.Exp(-t * 16f);

                // Squishy liquid splatter: frequency-modulated mid tone + noise
                float freqMid = Mathf.Lerp(620f, 180f, t);
                phaseMid += 2f * Mathf.PI * freqMid / SAMPLE_RATE;
                float liquidTone = Mathf.Sin(phaseMid + Mathf.Sin(phaseLow * 2.5f) * 2.0f);
                float wetNoise = (Random.value * 2f - 1f) * Mathf.Exp(-t * 9f);

                float body = Mathf.Lerp(liquidTone, wetNoise, 0.65f);
                float envelope = Mathf.Clamp01((float)i / (SAMPLE_RATE * 0.005f)) * Mathf.Exp(-t * 10f);

                samples[i] = Mathf.Clamp((subThump * 0.65f + body * 0.55f) * envelope, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, totalSamples, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Coconut / Green fruit: Hard crisp crack with sharp impulse transient and resonant woody fracture ring.
        /// </summary>
        public static AudioClip CreateHardCrackClip(string name = "Fruit_HardCrack")
        {
            float duration = 0.16f;
            int totalSamples = Mathf.FloorToInt(SAMPLE_RATE * duration);
            float[] samples = new float[totalSamples];

            float phaseRing1 = 0f;
            float phaseRing2 = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / totalSamples;

                // Extremely sharp impulse click (0 - 3 ms)
                float click = (i < SAMPLE_RATE * 0.003f) ? (Random.value * 2f - 1f) * 1.5f : 0f;

                // Resonant wood/shell frequencies: 1200 Hz and 2350 Hz
                phaseRing1 += 2f * Mathf.PI * 1250f / SAMPLE_RATE;
                phaseRing2 += 2f * Mathf.PI * 2380f / SAMPLE_RATE;
                float ring = Mathf.Sin(phaseRing1) * 0.6f + Mathf.Sin(phaseRing2) * 0.4f;

                float envelope = Mathf.Exp(-t * 26f); // very rapid, crisp decay
                float sample = (click + ring) * envelope * 0.85f;

                samples[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, totalSamples, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Banana / Yellow fruit: Soft fleshy slice with smooth tonal friction and warm velvety harmonics.
        /// </summary>
        public static AudioClip CreateSoftFleshySliceClip(string name = "Fruit_SoftSlice")
        {
            float duration = 0.22f;
            int totalSamples = Mathf.FloorToInt(SAMPLE_RATE * duration);
            float[] samples = new float[totalSamples];

            float phase = 0f;
            float phaseHarmonic = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / totalSamples;

                // Smooth sliding tone from 750 Hz down to 320 Hz
                float freq = Mathf.Lerp(750f, 320f, Mathf.Pow(t, 0.6f));
                phase += 2f * Mathf.PI * freq / SAMPLE_RATE;
                phaseHarmonic += 2f * Mathf.PI * (freq * 1.5f) / SAMPLE_RATE;

                float tone = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phaseHarmonic) * 0.3f;
                float airNoise = (Random.value * 2f - 1f) * 0.25f;

                // Soft 12ms attack, smooth decay
                float attack = Mathf.Clamp01((float)i / (SAMPLE_RATE * 0.012f));
                float release = Mathf.Exp(-t * 11f);
                float envelope = attack * release;

                samples[i] = Mathf.Clamp((tone + airNoise) * envelope * 0.8f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, totalSamples, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Directional spawn chime emitted at the fruit's spawn position to indicate launch.
        /// </summary>
        public static AudioClip CreateSpawnChimeClip(string name = "Fruit_SpawnCue")
        {
            float duration = 0.24f;
            int totalSamples = Mathf.FloorToInt(SAMPLE_RATE * duration);
            float[] samples = new float[totalSamples];

            float phase1 = 0f;
            float phase2 = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / totalSamples;

                // Upward ascending acoustic ping (780 Hz -> 1560 Hz octave leap)
                float freq = Mathf.Lerp(780f, 1560f, Mathf.Pow(t, 0.5f));
                phase1 += 2f * Mathf.PI * freq / SAMPLE_RATE;
                phase2 += 2f * Mathf.PI * (freq * 2.02f) / SAMPLE_RATE; // slight bell detune

                float tone = Mathf.Sin(phase1) * 0.7f + Mathf.Sin(phase2) * 0.3f;
                float attack = Mathf.Clamp01((float)i / (SAMPLE_RATE * 0.006f));
                float envelope = attack * Mathf.Exp(-t * 13f);

                samples[i] = Mathf.Clamp(tone * envelope * 0.65f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, totalSamples, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Fly-by whoosh sound triggered when an unsliced fruit passes close to the player.
        /// </summary>
        public static AudioClip CreateFlyByWhooshClip(string name = "Fruit_FlyByWhoosh")
        {
            float duration = 0.45f;
            int totalSamples = Mathf.FloorToInt(SAMPLE_RATE * duration);
            float[] samples = new float[totalSamples];

            float lastNoise = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / totalSamples;

                // Smooth bell curve envelope centered at t = 0.45
                float envelope = Mathf.Exp(-Mathf.Pow((t - 0.45f) * 4.5f, 2f));

                // Doppler downward pitch slide simulation (filtered noise)
                float rawNoise = Random.value * 2f - 1f;
                float filterAlpha = Mathf.Lerp(0.35f, 0.08f, t); // low-pass cutoff decreases over time
                lastNoise = Mathf.Lerp(lastNoise, rawNoise, filterAlpha);

                // Modulated whistling air tone
                float airFreq = Mathf.Lerp(580f, 220f, t);
                float airTone = Mathf.Sin(2f * Mathf.PI * airFreq * i / SAMPLE_RATE) * 0.35f;

                samples[i] = Mathf.Clamp((lastNoise * 0.85f + airTone) * envelope * 0.8f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, totalSamples, 1, SAMPLE_RATE, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Revolving stereo chord progression for 5x and 10x combo milestones.
        /// 2 channels (stereo) with revolving spatial panning (L -> Center -> R -> L).
        /// </summary>
        public static AudioClip CreateRevolvingMilestoneClip(int comboLevel, string name = "Combo_MilestoneRevolving")
        {
            float duration = 0.65f;
            int totalSamplesPerChannel = Mathf.FloorToInt(SAMPLE_RATE * duration);
            int channels = 2;
            float[] interleavedSamples = new float[totalSamplesPerChannel * channels];

            // Harmonic chord frequencies: C major chord
            // 5x combo: C5 (523.25), E5 (659.25), G5 (783.99), C6 (1046.50)
            // 10x combo: Octave higher celebratory fanfare
            float root = comboLevel >= 10 ? 659.25f : 523.25f;
            float f1 = root;
            float f2 = root * 1.2599f;  // major 3rd
            float f3 = root * 1.4983f;  // 5th
            float f4 = root * 2.0f;     // octave

            float phase1 = 0f, phase2 = 0f, phase3 = 0f, phase4 = 0f;

            for (int i = 0; i < totalSamplesPerChannel; i++)
            {
                float t = (float)i / totalSamplesPerChannel;

                phase1 += 2f * Mathf.PI * f1 / SAMPLE_RATE;
                phase2 += 2f * Mathf.PI * f2 / SAMPLE_RATE;
                phase3 += 2f * Mathf.PI * f3 / SAMPLE_RATE;
                phase4 += 2f * Mathf.PI * f4 / SAMPLE_RATE;

                // Cascade notes: staggered attacks
                float a1 = Mathf.Clamp01((t - 0.00f) * 40f);
                float a2 = Mathf.Clamp01((t - 0.06f) * 40f);
                float a3 = Mathf.Clamp01((t - 0.12f) * 40f);
                float a4 = Mathf.Clamp01((t - 0.18f) * 40f);

                float chordTone = 
                    Mathf.Sin(phase1) * a1 * 0.30f +
                    Mathf.Sin(phase2) * a2 * 0.28f +
                    Mathf.Sin(phase3) * a3 * 0.26f +
                    Mathf.Sin(phase4) * a4 * 0.32f;

                float envelope = Mathf.Exp(-t * 4.5f);
                float rawSignal = chordTone * envelope * 0.9f;

                // Revolving stereo panning angle: sweeps 360 degrees across the duration
                float panAngle = t * 2f * Mathf.PI * 1.5f; // 1.5 rotations
                float panL = (Mathf.Cos(panAngle) + 1f) * 0.5f;
                float panR = (Mathf.Sin(panAngle) + 1f) * 0.5f;

                // Normalize stereo energy
                float norm = Mathf.Sqrt(panL * panL + panR * panR + 0.001f);
                panL /= norm;
                panR /= norm;

                interleavedSamples[i * 2 + 0] = Mathf.Clamp(rawSignal * panL, -1f, 1f); // Left
                interleavedSamples[i * 2 + 1] = Mathf.Clamp(rawSignal * panR, -1f, 1f); // Right
            }

            AudioClip clip = AudioClip.Create(name, totalSamplesPerChannel, channels, SAMPLE_RATE, false);
            clip.SetData(interleavedSamples, 0);
            return clip;
        }
    }
}
