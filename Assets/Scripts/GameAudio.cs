using UnityEngine;

// All game sounds. By default they are synthesized at start-up (no audio files needed):
//   modal synthesis for metal/wood impacts, pink noise + bubbles for steam, layered crackle for fire,
//   motor hum + abrasive noise for the grinder, all with a little room reverb.
// To use real recordings instead, drop files named like the keys below into Assets/Resources/Audio/
// (e.g. hammer.wav) - a file with the matching name replaces the synthesized sound automatically.
//   One-shots: hammer, splash, clack_wood, clack_metal, pickup, whoosh, shatter, coin,
//              chime_perfect, chime_good, buzz_bad, fanfare_high, fanfare_mid, fanfare_low
//   Loops:     loop_fire, loop_steam, loop_grind; music_game (optional, follows the Music setting - none ships yet)
//   Ambience:  amb_river, amb_crowd, amb_wind (loops), amb_bird, amb_step (one-shots) - the city around the smithy,
//              quiet beds that follow the time of day (SetAmbience), with the odd bird, distant hammer or guard steps.
public class GameAudio : MonoBehaviour
{
    private const int Rate = 44100;
    private const float TwoPi = 6.2831853f;

    public float masterVolume = 0.8f;
    // The player's SFX setting on top (Master is AudioListener.volume - see GameSettings).
    private float Vol => masterVolume * GameSettings.SfxVolume;
    private AudioSource musicSrc; // only if a music_game track is in Resources/Audio

    private readonly System.Random rng = new System.Random(20260920);
    private AudioSource[] pool;
    private int poolIndex;
    private AudioSource fireSrc, steamSrc, grindSrc;
    private float fireTarget, steamTarget, grindTarget, grindPitch = 1f;

    private AudioClip hammer, splash, clackWood, clackMetal, pickup, whoosh, shatter, coin;
    private AudioClip chimePerfect, chimeGood, buzzBad, fanfareHigh, fanfareMid, fanfareLow;

    private void Awake()
    {
        pool = new AudioSource[6];
        for (int i = 0; i < pool.Length; i++) pool[i] = NewSource(false);

        hammer = Pick("hammer", MakeHammer);
        splash = Pick("splash", MakeSplash);
        clackWood = Pick("clack_wood", () => MakeClack(false));
        clackMetal = Pick("clack_metal", () => MakeClack(true));
        pickup = Pick("pickup", MakePickup);
        whoosh = Pick("whoosh", MakeWhoosh);
        shatter = Pick("shatter", MakeShatter);
        coin = Pick("coin", MakeCoin);
        chimePerfect = Pick("chime_perfect", () => MakeChime(new[] { 1046f, 1318f, 1568f, 2093f }, 0.075f, 1.1f, 4.5f));
        chimeGood = Pick("chime_good", () => MakeChime(new[] { 587f, 880f }, 0.1f, 0.8f, 6f));
        buzzBad = Pick("buzz_bad", MakeBuzz);
        fanfareHigh = Pick("fanfare_high", () => MakeFanfare(new[] { 392f, 523f, 659f, 784f }, new[] { 523f, 659f, 784f, 1046f }, 8));
        fanfareMid = Pick("fanfare_mid", () => MakeFanfare(new[] { 523f, 659f, 784f }, new[] { 523f, 659f, 784f }, 6));
        fanfareLow = Pick("fanfare_low", MakeFanfareLow);

        fireSrc = NewSource(true); fireSrc.clip = Pick("loop_fire", MakeFireLoop);
        steamSrc = NewSource(true); steamSrc.clip = Pick("loop_steam", MakeSteamLoop);
        grindSrc = NewSource(true); grindSrc.clip = Pick("loop_grind", MakeGrindLoop);
        foreach (var s in new[] { fireSrc, steamSrc, grindSrc }) { s.volume = 0f; s.Play(); }
        riverSrc = NewSource(true); riverSrc.clip = Pick("amb_river", MakeRiverLoop);
        crowdSrc = NewSource(true); crowdSrc.clip = Pick("amb_crowd", MakeCrowdLoop);
        windSrc = NewSource(true); windSrc.clip = Pick("amb_wind", MakeWindLoop);
        bird = Pick("amb_bird", MakeBird);
        step = Pick("amb_step", MakeStep);
        foreach (var s in new[] { riverSrc, crowdSrc, windSrc }) { s.volume = 0f; s.Play(); }
        var music = Resources.Load<AudioClip>("Audio/music_game");
        if (music != null) { musicSrc = NewSource(true); musicSrc.clip = music; musicSrc.volume = 0.5f * GameSettings.MusicVolume; musicSrc.Play(); }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        fireSrc.volume = Mathf.MoveTowards(fireSrc.volume, fireTarget * 0.55f * Vol, dt * 3f);
        steamSrc.volume = Mathf.MoveTowards(steamSrc.volume, steamTarget * 0.6f * Vol, dt * 5f);
        grindSrc.volume = Mathf.MoveTowards(grindSrc.volume, grindTarget * 0.5f * Vol, dt * 8f);
        grindSrc.pitch = Mathf.Lerp(grindSrc.pitch, grindPitch, dt * 8f);
        riverSrc.volume = Mathf.MoveTowards(riverSrc.volume, riverTarget * Vol, dt * 0.2f);
        crowdSrc.volume = Mathf.MoveTowards(crowdSrc.volume, crowdTarget * Vol, dt * 0.2f);
        windSrc.volume = Mathf.MoveTowards(windSrc.volume, windTarget * Vol, dt * 0.2f);
        if (musicSrc != null) musicSrc.volume = 0.5f * GameSettings.MusicVolume;
        UpdateAmbientEvents(Time.deltaTime);
    }

    // ------------------------------------------------------------------ the city around the smithy

    private AudioSource riverSrc, crowdSrc, windSrc;
    private float riverTarget, crowdTarget, windTarget, ambHour = 12f, nextAmbient = 6f;
    private bool ambRain;
    private AudioClip bird, step;

    // Called every frame with the clock and the weather: the river always, a murmur of people by day (a quieter,
    // tavern-ish one in the evening), wind that rises at night and in the rain.
    public void SetAmbience(float hour, bool rain)
    {
        ambHour = hour;
        ambRain = rain;
        float day = Mathf.Clamp01((hour - 6f) / 2f) * (1f - Mathf.Clamp01((hour - 18f) / 3f)); // 0 at night .. 1 by day
        riverTarget = 0.13f + 0.03f * (1f - day);
        crowdTarget = (0.03f + 0.11f * day) * (rain ? 0.4f : 1f);
        crowdSrc.pitch = hour >= 17f ? 0.9f : 1f; // lower, fewer voices from the taverns
        windTarget = 0.035f + 0.07f * (1f - day) + (rain ? 0.08f : 0f);
    }

    // Now and then something small: birds in the morning and by day, a hammer at another forge across town, and at
    // night the watch's footsteps going by.
    private void UpdateAmbientEvents(float dt)
    {
        nextAmbient -= dt;
        if (nextAmbient > 0f) return;
        nextAmbient = Random.Range(5f, 12f);
        bool night = ambHour >= 21f || ambHour < 5f, dayTime = ambHour >= 6f && ambHour < 18.5f;
        float r = Random.value;
        if (dayTime && !ambRain && r < 0.45f) StartCoroutine(Sequence(bird, Random.Range(2, 4), 0.25f, 0.055f, Random.Range(0.9f, 1.2f)));
        else if (dayTime && r < 0.7f) StartCoroutine(Sequence(hammer, Random.Range(2, 5), 0.55f, 0.035f, Random.Range(0.8f, 0.95f)));
        else if (night && r < 0.4f) StartCoroutine(Sequence(step, Random.Range(5, 8), 0.48f, 0.06f, Random.Range(0.9f, 1.05f)));
    }

    private System.Collections.IEnumerator Sequence(AudioClip clip, int count, float gap, float volume, float pitch)
    {
        for (int i = 0; i < count; i++)
        {
            Play(clip, volume, pitch * Random.Range(0.97f, 1.03f));
            yield return new WaitForSeconds(gap * Random.Range(0.85f, 1.2f));
        }
    }

    // ------------------------------------------------------------------ game-facing API

    public void Hammer() => Play(hammer, 1f, Random.Range(0.94f, 1.08f));
    public void Splash() => Play(splash, 0.9f, 1f);
    public void Pickup() => Play(pickup, 0.7f, Random.Range(0.95f, 1.1f));
    public void Whoosh() => Play(whoosh, 0.35f, Random.Range(0.9f, 1.1f));
    public void Shatter() => Play(shatter, 0.9f, 1f);
    public void Coin() => Play(coin, 0.8f, 1f);

    // A part settling onto the tang: wood-like for a bad fit, metallic click for a good one.
    public void Clack(bool good) => Play(good ? clackMetal : clackWood, 0.9f, Random.Range(0.95f, 1.05f));

    public void PlayRating(Rating rating)
    {
        Play(rating == Rating.Perfect ? chimePerfect : rating == Rating.Good ? chimeGood : buzzBad, 0.8f, 1f);
    }

    public void Fanfare(string grade)
    {
        Play(grade == "S" || grade == "A" ? fanfareHigh : grade == "B" || grade == "C" ? fanfareMid : fanfareLow, 0.9f, 1f);
    }

    public void SetFire(float level) => fireTarget = Mathf.Clamp01(level);
    public void SetSteam(float level) => steamTarget = Mathf.Clamp01(level);
    public void SetGrind(float level, float pitch) { grindTarget = Mathf.Clamp01(level); grindPitch = pitch; }

    public void StopLoops()
    {
        fireTarget = steamTarget = grindTarget = 0f;
    }

    // ------------------------------------------------------------------ playback helpers

    private AudioSource NewSource(bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        return src;
    }

    private void Play(AudioClip clip, float volume, float pitch)
    {
        var src = pool[poolIndex++ % pool.Length];
        src.pitch = pitch;
        src.PlayOneShot(clip, volume * Vol);
    }

    // A real recording in Resources/Audio wins over the synthesized version.
    private static AudioClip Pick(string key, System.Func<AudioClip> synth)
    {
        var real = Resources.Load<AudioClip>("Audio/" + key);
        return real != null ? real : synth();
    }

    // ------------------------------------------------------------------ DSP building blocks

    private float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
    private float Rand() => (float)rng.NextDouble();

    // Pink (1/f) noise - much closer to natural hiss and rumble than white noise.
    private class Pink
    {
        private readonly System.Random r;
        private float b0, b1, b2, b3, b4, b5, b6;
        public Pink(System.Random rng) { r = rng; }
        public float Next()
        {
            float w = (float)(r.NextDouble() * 2.0 - 1.0);
            b0 = 0.99886f * b0 + w * 0.0555179f;
            b1 = 0.99332f * b1 + w * 0.0750759f;
            b2 = 0.96900f * b2 + w * 0.1538520f;
            b3 = 0.86650f * b3 + w * 0.3104856f;
            b4 = 0.55000f * b4 + w * 0.5329522f;
            b5 = -0.7616f * b5 - w * 0.0168980f;
            float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
            b6 = w * 0.115926f;
            return pink * 0.11f;
        }
    }

    // Second-order filter (RBJ cookbook).
    private class Biquad
    {
        private float b0, b1, b2, a1, a2, z1, z2;

        private void Set(float nb0, float nb1, float nb2, float na0, float na1, float na2)
        {
            b0 = nb0 / na0; b1 = nb1 / na0; b2 = nb2 / na0; a1 = na1 / na0; a2 = na2 / na0;
        }

        public void Bandpass(float f, float q)
        {
            float w = TwoPi * Mathf.Min(f, 20000f) / Rate, alpha = Mathf.Sin(w) / (2f * q), cw = Mathf.Cos(w);
            Set(alpha, 0f, -alpha, 1f + alpha, -2f * cw, 1f - alpha);
        }

        public void Lowpass(float f, float q)
        {
            float w = TwoPi * Mathf.Min(f, 20000f) / Rate, alpha = Mathf.Sin(w) / (2f * q), cw = Mathf.Cos(w);
            Set((1f - cw) / 2f, 1f - cw, (1f - cw) / 2f, 1f + alpha, -2f * cw, 1f - alpha);
        }

        public void Highpass(float f, float q)
        {
            float w = TwoPi * Mathf.Min(f, 20000f) / Rate, alpha = Mathf.Sin(w) / (2f * q), cw = Mathf.Cos(w);
            Set((1f + cw) / 2f, -(1f + cw), (1f + cw) / 2f, 1f + alpha, -2f * cw, 1f - alpha);
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    // Small room: four damped combs into two allpasses (Schroeder). Adds a natural tail to impacts.
    private float[] Reverb(float[] dry, float mix, float tail, float feedback = 0.8f)
    {
        int n = dry.Length + (int)(tail * Rate);
        var result = new float[n];
        int[] combLen = { 1310, 1636, 1813, 1927 };
        var comb = new float[4][];
        var combPos = new int[4];
        var combLp = new float[4];
        for (int k = 0; k < 4; k++) comb[k] = new float[combLen[k]];
        int[] apLen = { 225, 75 };
        var ap = new float[2][];
        var apPos = new int[2];
        for (int k = 0; k < 2; k++) ap[k] = new float[apLen[k]];

        for (int i = 0; i < n; i++)
        {
            float x = i < dry.Length ? dry[i] : 0f;
            float wet = 0f;
            for (int k = 0; k < 4; k++)
            {
                float y = comb[k][combPos[k]];
                combLp[k] = y * 0.7f + combLp[k] * 0.3f; // damping: highs die away faster, like a real room
                comb[k][combPos[k]] = x * 0.3f + combLp[k] * feedback;
                combPos[k] = (combPos[k] + 1) % combLen[k];
                wet += y;
            }
            for (int k = 0; k < 2; k++)
            {
                float delayed = ap[k][apPos[k]];
                float y = -0.7f * wet + delayed;
                ap[k][apPos[k]] = wet + 0.7f * delayed;
                apPos[k] = (apPos[k] + 1) % apLen[k];
                wet = y;
            }
            result[i] = x * (1f - mix * 0.5f) + wet * mix;
        }
        return result;
    }

    // Blend the end of a longer render into its start so the loop has no seam.
    private static float[] MakeLoopable(float[] raw, int crossfade)
    {
        int n = raw.Length - crossfade;
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = raw[i];
        for (int i = 0; i < crossfade; i++)
        {
            float w = i / (float)crossfade;
            d[i] = raw[i] * w + raw[n + i] * (1f - w);
        }
        return d;
    }

    private AudioClip Finish(string name, float[] data, float peak, bool loop = false)
    {
        float max = 0.0001f;
        foreach (var v in data) max = Mathf.Max(max, Mathf.Abs(v));
        float gain = peak / max;
        int fade = loop ? 0 : Mathf.Min(data.Length / 2, (int)(Rate * 0.003f));
        for (int i = 0; i < data.Length; i++)
        {
            data[i] *= gain;
            if (fade > 0)
            {
                if (i < fade) data[i] *= i / (float)fade;
                int fromEnd = data.Length - 1 - i;
                if (fromEnd < fade) data[i] *= fromEnd / (float)fade;
            }
        }
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ impacts

    // Hammer on an anvil: bright noisy strike, ringing steel modes, body thud, a small bounce, and room tail.
    private AudioClip MakeHammer()
    {
        int len = (int)(Rate * 0.75f);
        var d = new float[len];
        float[] f = { 780f, 1760f, 2900f, 4300f, 6100f, 8200f };
        float[] a = { 1f, 0.8f, 0.6f, 0.45f, 0.3f, 0.18f };
        float[] dec = { 5f, 7f, 10f, 16f, 24f, 34f };
        var bp = new Biquad();
        bp.Bandpass(3500f, 0.6f);

        float[] strikeAt = { 0f, 0.014f };
        float[] strikeAmp = { 1f, 0.35f }; // the hammer rebounds and taps once more
        for (int s = 0; s < 2; s++)
        {
            int s0 = (int)(strikeAt[s] * Rate);
            for (int i = s0; i < len; i++)
            {
                float t = (i - s0) / (float)Rate;
                if (t > 0.65f) break;
                float v = 0f;
                for (int k = 0; k < f.Length; k++)
                    v += a[k] * Mathf.Sin(TwoPi * f[k] * t) * Mathf.Exp(-dec[k] * t);
                v += 1.1f * bp.Process(Noise()) * Mathf.Exp(-t * 220f);
                v += 0.8f * Mathf.Sin(TwoPi * 170f * t) * Mathf.Exp(-t * 45f) + 0.5f * Mathf.Sin(TwoPi * 88f * t) * Mathf.Exp(-t * 30f);
                d[i] += v * strikeAmp[s];
            }
        }
        return Finish("hammer", Reverb(d, 0.22f, 0.35f), 0.95f);
    }

    // Short knocks: hollow wood block, or a small steel click.
    private AudioClip MakeClack(bool metal)
    {
        var d = new float[(int)(Rate * 0.3f)];
        float[] f = metal ? new[] { 1800f, 3350f, 5200f } : new[] { 520f, 1380f, 2300f };
        float[] dec = metal ? new[] { 45f, 70f, 100f } : new[] { 55f, 80f, 110f };
        float[] a = { 1f, 0.6f, 0.35f };
        var bp = new Biquad();
        bp.Bandpass(metal ? 6000f : 2500f, 0.8f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float v = 0f;
            for (int k = 0; k < 3; k++) v += a[k] * Mathf.Sin(TwoPi * f[k] * t) * Mathf.Exp(-dec[k] * t);
            v += 1.2f * bp.Process(Noise()) * Mathf.Exp(-t * 400f);
            v += 0.5f * Mathf.Sin(TwoPi * (metal ? 300f : 200f) * t) * Mathf.Exp(-t * 60f);
            d[i] = v;
        }
        return Finish(metal ? "clackMetal" : "clackWood", Reverb(d, 0.1f, 0.15f), 0.8f);
    }

    private AudioClip MakePickup()
    {
        var d = new float[(int)(Rate * 0.12f)];
        var bp = new Biquad();
        bp.Bandpass(2200f, 0.8f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            d[i] = 0.9f * Mathf.Sin(TwoPi * 760f * t) * Mathf.Exp(-t * 130f) + bp.Process(Noise()) * Mathf.Exp(-t * 500f);
        }
        return Finish("pickup", d, 0.5f);
    }

    // A bell-like partial set: attack of a couple of milliseconds, higher partials die away sooner.
    private void Bell(float[] d, float freq, float start, float amp, float decay)
    {
        float[] ratio = { 1f, 2f, 2.76f, 5.4f, 8.93f };
        float[] a = { 1f, 0.45f, 0.3f, 0.15f, 0.06f };
        float[] dm = { 1f, 1.3f, 1.7f, 2.4f, 3.4f };
        int s0 = (int)(start * Rate);
        for (int i = s0; i < d.Length; i++)
        {
            float t = (i - s0) / (float)Rate;
            if (t > 3f) break;
            float attack = 1f - Mathf.Exp(-t * 1500f);
            float v = 0f;
            for (int k = 0; k < ratio.Length; k++)
            {
                float e = Mathf.Exp(-t * decay * dm[k]);
                if (e < 0.001f) continue;
                v += a[k] * Mathf.Sin(TwoPi * freq * ratio[k] * t) * e;
            }
            d[i] += amp * attack * v;
        }
    }

    private AudioClip MakeChime(float[] notes, float gap, float length, float decay)
    {
        var d = new float[(int)(Rate * (length + gap * notes.Length))];
        for (int n = 0; n < notes.Length; n++) Bell(d, notes[n], n * gap, 0.5f, decay);
        return Finish("chime", Reverb(d, 0.15f, 0.3f), 0.7f);
    }

    private AudioClip MakeCoin()
    {
        var d = new float[(int)(Rate * 0.8f)];
        Bell(d, 2093f, 0f, 0.5f, 7f);
        Bell(d, 2794f, 0.07f, 0.45f, 6f);
        return Finish("coin", Reverb(d, 0.14f, 0.2f), 0.6f);
    }

    // Wrong fit / bad timing: a dull thud with a low, detuned buzz.
    private AudioClip MakeBuzz()
    {
        var d = new float[(int)(Rate * 0.5f)];
        var lp = new Biquad();
        lp.Lowpass(650f, 0.7f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float saw1 = 2f * ((110f * t) % 1f) - 1f;
            float saw2 = 2f * ((116f * t) % 1f) - 1f;
            float v = lp.Process((saw1 + saw2) * 0.5f) * Mathf.Exp(-t * 7f);
            v += 0.8f * Mathf.Sin(TwoPi * 70f * t) * Mathf.Exp(-t * 20f);
            d[i] = v;
        }
        return Finish("buzz", d, 0.55f);
    }

    // ------------------------------------------------------------------ water, steam, whoosh, glass

    // Red-hot steel plunged into water: whump, a hiss that starts loud and dies away with churning turbulence,
    // rising bubbles and fine sizzling.
    private AudioClip MakeSplash()
    {
        int len = (int)(Rate * 1.6f);
        var d = new float[len];
        var pink = new Pink(rng);
        var lp = new Biquad(); lp.Lowpass(6500f, 0.7f);
        var hp = new Biquad(); hp.Highpass(600f, 0.7f);
        float turb = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)Rate;
            turb += 0.002f * (Noise() - turb);
            float hiss = hp.Process(lp.Process(pink.Next())) * 3f;
            float env = (1f - Mathf.Exp(-t * 120f)) * (0.55f * Mathf.Exp(-t * 2.2f) + 0.45f * Mathf.Exp(-t * 0.9f));
            d[i] = hiss * env * (1f + 12f * turb);
            d[i] += 0.5f * Mathf.Sin(TwoPi * 90f * t) * Mathf.Exp(-t * 10f) * (1f - Mathf.Exp(-t * 300f));
        }
        AddBubbles(d, 45, 0.9f, 1.6f, 0.25f, 0.6f);
        AddSizzle(d, 70, 1.2f, 0.15f, 0.35f);
        return Finish("splash", Reverb(d, 0.12f, 0.25f), 0.9f);
    }

    // Bubbles: short sine chirps that rise in pitch, denser at the start.
    private void AddBubbles(float[] d, int count, float spread, float pow, float ampMin, float ampMax)
    {
        for (int b = 0; b < count; b++)
        {
            int start = (int)(spread * Mathf.Pow(Rand(), pow) * Rate);
            float f0 = 250f + Rand() * 550f;
            float dec = 70f + Rand() * 80f;
            float amp = ampMin + Rand() * (ampMax - ampMin);
            for (int j = 0; j < Rate / 15 && start + j < d.Length; j++)
            {
                float tt = j / (float)Rate;
                d[start + j] += amp * Mathf.Sin(TwoPi * f0 * (tt + 3.5f * tt * tt)) * Mathf.Exp(-tt * dec);
            }
        }
    }

    private void AddSizzle(float[] d, int count, float spread, float ampMin, float ampMax)
    {
        for (int c = 0; c < count; c++)
        {
            int start = (int)(spread * Mathf.Pow(Rand(), 1.4f) * Rate);
            float f = 3000f + Rand() * 4500f;
            float dec = 1000f + Rand() * 1500f;
            float amp = ampMin + Rand() * (ampMax - ampMin);
            for (int j = 0; j < Rate / 60 && start + j < d.Length; j++)
            {
                float tt = j / (float)Rate;
                d[start + j] += amp * Mathf.Sin(TwoPi * f * tt) * Mathf.Exp(-tt * dec);
            }
        }
    }

    private AudioClip MakeSteamLoop()
    {
        int n = Rate * 2, m = (int)(Rate * 0.15f);
        var raw = new float[n + m];
        var pink = new Pink(rng);
        var lp = new Biquad(); lp.Lowpass(6000f, 0.7f);
        var hp = new Biquad(); hp.Highpass(500f, 0.7f);
        float[] mf = { 0.5f, 1.5f, 3.5f, 7f };
        float[] ma = { 0.16f, 0.12f, 0.08f, 0.05f };
        float[] mp = { Rand() * TwoPi, Rand() * TwoPi, Rand() * TwoPi, Rand() * TwoPi };
        float turb = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            turb += 0.002f * (Noise() - turb);
            float mod = 0.7f;
            for (int k = 0; k < 4; k++) mod += ma[k] * Mathf.Sin(TwoPi * mf[k] * t + mp[k]);
            raw[i] = hp.Process(lp.Process(pink.Next())) * 2.5f * mod * (1f + 10f * turb);
        }
        AddBubbles(raw, 14, (raw.Length - Rate / 10) / (float)Rate, 1f, 0.12f, 0.3f);
        AddSizzle(raw, 30, (raw.Length - Rate / 30) / (float)Rate, 0.08f, 0.2f);
        return Finish("steam", MakeLoopable(raw, m), 0.6f, true);
    }

    // ---- ambience ----
    // Running water: pink noise, band-limited to a soft rush, slowly swelling, with little bubbles and trickles.
    private AudioClip MakeRiverLoop()
    {
        int n = Rate * 6, m = Rate / 2;
        var raw = new float[n + m];
        var pink = new Pink(rng);
        var lp = new Biquad(); lp.Lowpass(1400f, 0.6f);
        var hp = new Biquad(); hp.Highpass(160f, 0.7f);
        float p1 = Rand() * TwoPi, p2 = Rand() * TwoPi;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            float mod = 0.8f + 0.12f * Mathf.Sin(TwoPi * 0.17f * t + p1) + 0.08f * Mathf.Sin(TwoPi * 0.43f * t + p2);
            raw[i] = hp.Process(lp.Process(pink.Next())) * mod;
        }
        AddBubbles(raw, 40, (raw.Length - Rate / 10) / (float)Rate, 1f, 0.02f, 0.06f);
        return Finish("river", MakeLoopable(raw, m), 0.5f, true);
    }

    // A crowd far off: a handful of "voices", each noise through a formant-ish band that comes and goes in syllables.
    private AudioClip MakeCrowdLoop()
    {
        int n = Rate * 7, m = Rate / 2;
        var raw = new float[n + m];
        for (int v = 0; v < 7; v++)
        {
            var bp = new Biquad(); bp.Bandpass(260f + Rand() * 520f, 2.5f);
            var bp2 = new Biquad(); bp2.Bandpass(900f + Rand() * 900f, 3f);
            float syll = 3.5f + Rand() * 3f, phase = Rand() * TwoPi, talk = Rand();
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                // Sentences and pauses, and syllables within them.
                float sentence = Mathf.Clamp01(Mathf.Sin(TwoPi * (0.11f + 0.05f * talk) * t + phase) * 2f + 0.3f);
                float env = sentence * Mathf.Max(0f, Mathf.Sin(TwoPi * syll * t + phase * 3f));
                float x = Noise();
                raw[i] += (bp.Process(x) + 0.4f * bp2.Process(x)) * env;
            }
        }
        var lp = new Biquad(); lp.Lowpass(1800f, 0.7f);
        for (int i = 0; i < raw.Length; i++) raw[i] = lp.Process(raw[i]);
        return Finish("crowd", MakeLoopable(raw, m), 0.45f, true);
    }

    // Wind: pink noise through a wide band that drifts up and down, gusting.
    private AudioClip MakeWindLoop()
    {
        int n = Rate * 8, m = Rate;
        var raw = new float[n + m];
        var pink = new Pink(rng);
        var bp = new Biquad();
        float p = Rand() * TwoPi;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            if (i % 64 == 0) bp.Bandpass(380f + 220f * Mathf.Sin(TwoPi * 0.09f * t + p), 0.9f);
            float gust = 0.6f + 0.4f * Mathf.Sin(TwoPi * 0.13f * t + p * 2f) * Mathf.Sin(TwoPi * 0.05f * t);
            raw[i] = bp.Process(pink.Next()) * gust;
        }
        return Finish("wind", MakeLoopable(raw, m), 0.5f, true);
    }

    // A small bird: a quick chirp that dips and rises, with a tiny break in the middle.
    private AudioClip MakeBird()
    {
        int len = (int)(Rate * 0.22f);
        var d = new float[len];
        float f0 = 3200f + Rand() * 900f, ph = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)Rate, k = t / 0.22f;
            float f = f0 * (1f - 0.25f * Mathf.Sin(Mathf.PI * k)) + 180f * Mathf.Sin(TwoPi * 38f * t);
            ph += TwoPi * f / Rate;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.05f)) * (k < 0.45f || k > 0.55f ? 1f : 0.1f);
            d[i] = Mathf.Sin(ph) * env;
        }
        return Finish("bird", d, 0.5f);
    }

    // A boot on cobbles: a dull low thud and a little scuff.
    private AudioClip MakeStep()
    {
        int len = (int)(Rate * 0.16f);
        var d = new float[len];
        var lp = new Biquad(); lp.Lowpass(520f, 0.8f);
        var hp = new Biquad(); hp.Highpass(1800f, 0.7f);
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)Rate;
            d[i] = lp.Process(Noise()) * Mathf.Exp(-t * 38f) * 1.6f + hp.Process(Noise()) * Mathf.Exp(-(t - 0.03f) * (t - 0.03f) * 4000f) * 0.25f;
        }
        return Finish("step", d, 0.6f);
    }

    // Air moving past: pink noise through a resonant band that sweeps up and back down.
    private AudioClip MakeWhoosh()
    {
        float len = 0.9f;
        var d = new float[(int)(Rate * len)];
        var pink = new Pink(rng);
        var bp = new Biquad();
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float shape = Mathf.Sin(Mathf.PI * t / len);
            shape *= shape;
            if (i % 32 == 0) bp.Bandpass(300f + 2200f * shape, 1.2f);
            d[i] = bp.Process(pink.Next()) * shape;
        }
        return Finish("whoosh", d, 0.6f);
    }

    // Glass/metal breaking: a sharp crack, a few heavy chunks, then a shower of tinkles that thins out.
    private AudioClip MakeShatter()
    {
        var d = new float[(int)(Rate * 1.3f)];
        var bp = new Biquad();
        bp.Bandpass(3000f, 0.5f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            d[i] = 1.0f * bp.Process(Noise()) * Mathf.Exp(-t * 90f);
        }
        for (int c = 0; c < 6; c++)
        {
            int start = (int)(Rand() * 0.12f * Rate);
            float f = 800f + Rand() * 800f;
            for (int j = 0; j < Rate / 4 && start + j < d.Length; j++)
            {
                float tt = j / (float)Rate;
                d[start + j] += 0.5f * Mathf.Sin(TwoPi * f * tt) * Mathf.Exp(-tt * 30f);
            }
        }
        for (int c = 0; c < 90; c++)
        {
            float at = 0.7f * Mathf.Pow(Rand(), 2.2f);
            int start = (int)(at * Rate);
            float f = 2500f + Rand() * 6500f;
            float dec = 40f + Rand() * 160f;
            float amp = (0.15f + Rand() * 0.35f) * (1f - at);
            for (int j = 0; j < Rate / 4 && start + j < d.Length; j++)
            {
                float tt = j / (float)Rate;
                float env = Mathf.Exp(-tt * dec);
                d[start + j] += amp * env * (Mathf.Sin(TwoPi * f * tt) + 0.5f * Mathf.Sin(TwoPi * f * 2.7f * tt));
            }
        }
        return Finish("shatter", Reverb(d, 0.15f, 0.3f), 0.85f);
    }

    // ------------------------------------------------------------------ fire and grinding loops

    // Deep flame rumble, a breathing mid-range roar and lots of little crackles and pops.
    private AudioClip MakeFireLoop()
    {
        int n = Rate * 3, m = (int)(Rate * 0.2f);
        var raw = new float[n + m];
        var pink = new Pink(rng);
        var roar = new Biquad(); roar.Bandpass(600f, 0.5f);
        float lp = 0f;
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            lp += 0.012f * (Noise() - lp);
            float rumble = lp * 6f * (0.8f + 0.3f * Mathf.Sin(TwoPi * 0.67f * t) + 0.15f * Mathf.Sin(TwoPi * 1.9f * t));
            float breath = 0.65f + 0.2f * Mathf.Sin(TwoPi * 1.3f * t) + 0.15f * Mathf.Sin(TwoPi * 2.9f * t);
            raw[i] = rumble + roar.Process(pink.Next()) * 1.0f * breath;
        }
        for (int c = 0; c < 130; c++)
        {
            int start = rng.Next(0, raw.Length - Rate / 15);
            bool pop = Rand() < 0.2f;
            float amp = pop ? 0.8f + Rand() * 0.4f : 0.25f + Rand() * 0.45f;
            float dec = pop ? 150f + Rand() * 200f : 600f + Rand() * 1200f;
            float f = pop ? 350f + Rand() * 550f : 1200f + Rand() * 4300f;
            for (int j = 0; j < Rate / 15 && start + j < raw.Length; j++)
            {
                float tt = j / (float)Rate;
                float env = Mathf.Exp(-tt * dec);
                raw[start + j] += amp * env * (0.6f * Noise() + 0.6f * Mathf.Sin(TwoPi * f * tt));
            }
        }
        return Finish("fire", MakeLoopable(raw, m), 0.7f, true);
    }

    // A bench grinder: motor hum with harmonics, abrasive noise pulsing with the wheel, a faint metallic screech, sparks.
    // Every periodic part uses whole cycles per second so the loop is seamless; pitch follows the wheel speed.
    private AudioClip MakeGrindLoop()
    {
        int n = Rate, m = (int)(Rate * 0.1f);
        var raw = new float[n + m];
        var pink = new Pink(rng);
        var abrasive = new Biquad(); abrasive.Bandpass(2600f, 0.7f);
        var screech = new Biquad(); screech.Bandpass(5200f, 4f);
        var scrape = new Biquad(); scrape.Bandpass(900f, 0.8f);
        float[] hf = { 100f, 200f, 300f, 500f, 700f };
        float[] ha = { 0.5f, 0.35f, 0.25f, 0.12f, 0.08f };
        for (int i = 0; i < raw.Length; i++)
        {
            float t = i / (float)Rate;
            float hum = 0f;
            for (int k = 0; k < hf.Length; k++)
                hum += ha[k] * Mathf.Sin(TwoPi * hf[k] * t + 0.02f * Mathf.Sin(TwoPi * 6f * t) * hf[k] / 100f);
            float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(TwoPi * 13f * t));
            float sc = Mathf.Sin(TwoPi * 3f * t);
            raw[i] = hum * 0.6f
                   + abrasive.Process(Noise()) * 1.2f * pulse
                   + screech.Process(Noise()) * 0.5f * (0.5f + 0.5f * sc * sc)
                   + scrape.Process(pink.Next()) * 0.5f;
        }
        for (int c = 0; c < 45; c++)
        {
            int start = rng.Next(0, raw.Length - Rate / 100);
            float f = 4000f + Rand() * 5000f;
            float dec = 1600f + Rand() * 1900f;
            float amp = 0.25f + Rand() * 0.35f;
            for (int j = 0; j < Rate / 100 && start + j < raw.Length; j++)
            {
                float tt = j / (float)Rate;
                raw[start + j] += amp * Mathf.Sin(TwoPi * f * tt) * Mathf.Exp(-tt * dec);
            }
        }
        return Finish("grind", MakeLoopable(raw, m), 0.7f, true);
    }

    // ------------------------------------------------------------------ fanfares

    // A simple brass-like voice: a stack of harmonics with a soft attack, vibrato and a brightness that fades.
    private void Brass(float[] d, float freq, float start, float length, float amp, int harmonics)
    {
        int s0 = (int)(start * Rate);
        for (int i = s0; i < d.Length; i++)
        {
            float t = (i - s0) / (float)Rate;
            float release = t < length ? 1f : Mathf.Exp(-(t - length) * 8f);
            if (release < 0.002f) break;
            float env = (1f - Mathf.Exp(-t * 35f)) * release * (0.85f + 0.15f * Mathf.Exp(-t * 3f));
            float vib = 1f + 0.004f * Mathf.Sin(TwoPi * 5.2f * t) * (1f - Mathf.Exp(-t * 3f));
            float v = 0f;
            for (int h = 1; h <= harmonics; h++)
            {
                float bright = h <= 3 ? 1f : 0.5f + 0.5f * Mathf.Exp(-t * 4f);
                v += Mathf.Sin(TwoPi * freq * h * t * vib + h * 0.3f) / Mathf.Pow(h, 0.9f) * bright;
            }
            d[i] += amp * env * v;
        }
    }

    private AudioClip MakeFanfare(float[] arpeggio, float[] chord, int harmonics)
    {
        var d = new float[(int)(Rate * 2.6f)];
        for (int n = 0; n < arpeggio.Length; n++) Brass(d, arpeggio[n], n * 0.13f, 0.12f, 0.4f, harmonics);
        float chordAt = arpeggio.Length * 0.13f + 0.05f;
        foreach (var f in chord) Brass(d, f, chordAt, 1.1f, 0.3f, harmonics);
        return Finish("fanfare", Reverb(d, 0.25f, 0.8f, 0.82f), 0.75f);
    }

    private AudioClip MakeFanfareLow()
    {
        var d = new float[(int)(Rate * 2.2f)];
        float[] notes = { 330f, 294f, 262f };
        for (int n = 0; n < notes.Length; n++) Brass(d, notes[n], n * 0.3f, 0.3f, 0.4f, 4);
        return Finish("fanfare", Reverb(d, 0.25f, 0.6f, 0.8f), 0.7f);
    }
}
