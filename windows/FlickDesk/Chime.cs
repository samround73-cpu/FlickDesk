using System;
using System.IO;
using System.Media;
using System.Text;

namespace FlickDesk;

/// <summary>A soft, glassy two-note chime synthesised at startup (no audio files to ship).</summary>
internal static class Chime
{
    // Kept alive for the app's lifetime: async playback reads from the player's buffer.
    private static readonly Lazy<SoundPlayer> Player = new(() =>
    {
        var p = new SoundPlayer(new MemoryStream(Build()));
        p.Load();
        return p;
    });

    public static void Play()
    {
        try { Player.Value.Play(); } // async, returns immediately
        catch (Exception) { /* no audio device – ignore */ }
    }

    private static byte[] Build()
    {
        const int rate = 44100;
        const double seconds = 0.55;
        int n = (int)(rate * seconds);
        var samples = new short[n];

        // E6 then B6 a beat later, each with a quiet octave partial; fast attack, exponential decay.
        (double freq, double start, double gain)[] notes = { (1318.5, 0.0, 0.32), (1975.5, 0.07, 0.26) };
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate;
            double v = 0;
            foreach (var (freq, start, gain) in notes)
            {
                double u = t - start;
                if (u < 0) continue;
                double env = Math.Min(1, u / 0.004) * Math.Exp(-u * 9);
                v += gain * env * (Math.Sin(2 * Math.PI * freq * u) + 0.25 * Math.Sin(2 * Math.PI * freq * 2 * u));
            }
            samples[i] = (short)(Math.Clamp(v, -1, 1) * short.MaxValue * 0.6);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        int dataBytes = n * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
