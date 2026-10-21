using System;
using System.IO;
using System.Media;
using System.Text;

namespace FlickDesk;

/// <summary>A quiet 8-bit "pickup" blip synthesised at startup (no audio files to ship).</summary>
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
        const double seconds = 0.26;
        int n = (int)(rate * seconds);
        var samples = new short[n];

        // Two quick notes on a 25% pulse wave, like an old console's pickup sound: E5 then B5.
        // The volume steps down in 16 levels (as 4-bit sound chips did) and stays low overall.
        (double freq, double start, double length)[] notes = { (659.3, 0.0, 0.07), (987.8, 0.07, 0.19) };
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate;
            double v = 0;
            foreach (var (freq, start, length) in notes)
            {
                double u = t - start;
                if (u < 0 || u >= length) continue;
                double phase = (u * freq) % 1.0;
                double pulse = phase < 0.25 ? 1 : -1;
                double level = Math.Floor(15 * Math.Exp(-u * 14) + 0.5) / 15;    // 4-bit volume envelope
                v += pulse * level;
            }
            samples[i] = (short)(Math.Clamp(v, -1, 1) * short.MaxValue * 0.11);
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
