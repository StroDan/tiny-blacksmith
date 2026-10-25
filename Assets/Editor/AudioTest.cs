using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Renders every synthesized game sound to Logs/audio/*.wav and prints a health report
// ([AUDIO] lines): length, peak, RMS, clipping, NaNs, DC offset, rough pitch/brightness, decay time,
// and the loop seam for the looping sounds.
// Run via: Unity.exe -batchmode -projectPath <path> -executeMethod AudioTest.Run -quit -logFile <log>
public static class AudioTest
{
    [MenuItem("Tiny Blacksmith/Run Audio Test")]
    public static void Run()
    {
        var go = new GameObject("AudioTest");
        var audio = go.AddComponent<GameAudio>();
        typeof(GameAudio).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(audio, null);

        Directory.CreateDirectory("Logs/audio");
        int problems = 0;
        var report = new StringBuilder();

        foreach (var field in typeof(GameAudio).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(AudioClip)) continue;
            var clip = (AudioClip)field.GetValue(audio);
            if (clip == null) { Debug.LogError($"[AUDIO] {field.Name}: clip is NULL"); problems++; continue; }
            problems += Analyze(field.Name, clip, false, report);
        }

        // The three loops live on AudioSources.
        foreach (var src in go.GetComponents<AudioSource>())
        {
            if (!src.loop || src.clip == null) continue;
            problems += Analyze("loop_" + src.clip.name, src.clip, true, report);
        }

        Debug.Log("[AUDIO] " + (problems == 0 ? "ALL SOUNDS OK" : problems + " PROBLEM(S)") + "\n" + report);
        Object.DestroyImmediate(go);
        EditorApplication.Exit(problems == 0 ? 0 : 1);
    }

    private static int Analyze(string name, AudioClip clip, bool loop, StringBuilder report)
    {
        var d = new float[clip.samples * clip.channels];
        clip.GetData(d, 0);
        int rate = clip.frequency;
        int problems = 0;

        double sum = 0, sumSq = 0;
        float peak = 0f;
        int nan = 0, clipped = 0, zeroCross = 0;
        for (int i = 0; i < d.Length; i++)
        {
            float v = d[i];
            if (float.IsNaN(v) || float.IsInfinity(v)) { nan++; continue; }
            sum += v; sumSq += v * v;
            float a = Mathf.Abs(v);
            if (a > peak) peak = a;
            if (a > 0.99f) clipped++;
            if (i > 0 && (d[i - 1] < 0f) != (v < 0f)) zeroCross++;
        }
        float rms = Mathf.Sqrt((float)(sumSq / d.Length));
        float dc = (float)(sum / d.Length);
        float seconds = d.Length / (float)rate;
        float pitchHz = zeroCross / (2f * seconds); // zero-crossing rate: roughly "brightness"

        // Decay: last time the short-term level is within 40 dB of the peak.
        int win = rate / 100;
        float lastLoud = 0f;
        for (int i = 0; i + win < d.Length; i += win)
        {
            float s = 0f;
            for (int j = 0; j < win; j++) s += d[i + j] * d[i + j];
            if (Mathf.Sqrt(s / win) > peak * 0.01f) lastLoud = (i + win) / (float)rate;
        }

        string seam = "";
        if (loop)
        {
            float jump = Mathf.Abs(d[0] - d[d.Length - 1]);
            seam = $" seamJump={jump:0.000}";
            if (jump > 0.2f) { seam += " (audible click!)"; problems++; }
        }

        string flags = "";
        if (nan > 0) { flags += $" NaN/Inf={nan}"; problems++; }
        if (clipped > 0) { flags += $" CLIPPED={clipped}"; problems++; }
        if (peak < 0.2f) { flags += " TOO QUIET"; problems++; }
        if (Mathf.Abs(dc) > 0.05f) { flags += $" DC={dc:0.000}"; problems++; }
        if (rms < 0.01f) { flags += " SILENT"; problems++; }

        string line = $"[AUDIO] {name,-22} {seconds,5:0.00}s peak={peak:0.00} rms={20f * Mathf.Log10(rms):0}dB zcr={pitchHz,6:0}Hz audible={lastLoud:0.00}s{seam}{flags}";
        Debug.Log(line);
        report.AppendLine(line);

        WriteWav($"Logs/audio/{name}.wav", d, rate);
        return problems;
    }

    private static void WriteWav(string path, float[] samples, int rate)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int dataBytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
            foreach (var s in samples) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
