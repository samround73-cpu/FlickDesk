using System;
using System.IO;
using System.Media;
using System.Text;

namespace FlickDesk;

/// <summary>A soft, low "bloom" synthesised at startup (no audio files to ship).</summary>
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
        const double seconds = 0.7;
        int n = (int)(rate * seconds);
        var samples = new short[n];

        // One warm, rounded tone: A4 gliding a touch down to G4 with a quiet fifth above it.
        // A slow 25 ms fade-in (no click), a long soft tail and pure sines only, so nothing is bright or sharp.
        double phaseA = 0, phaseB = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate;
            double freq = 392 + (440 - 392) * Math.Exp(-t * 18);           // 440 Hz -> 392 Hz
            phaseA += 2 * Math.PI * freq / rate;
            phaseB += 2 * Math.PI * freq * 1.5 / rate;
            double attack = 0.5 - 0.5 * Math.Cos(Math.PI * Math.Min(1, t / 0.025));
            double env = attack * Math.Exp(-t * 5.5);
            double fifth = 0.18 * Math.Exp(-t * 9);                         // fades faster than the body
            double v = env * (Math.Sin(phaseA) + fifth * Math.Sin(phaseB));
            double tail = Math.Min(1, (seconds - t) / 0.05);                // no click at the very end
            samples[i] = (short)(Math.Clamp(v * tail, -1, 1) * short.MaxValue * 0.28);
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
