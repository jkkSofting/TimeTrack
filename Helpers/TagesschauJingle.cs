using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// A short synthesized brass-style fanfare in G minor. The melody is generated at runtime
    /// (additive synthesis, 16-bit mono PCM WAV in memory), so no audio asset is shipped.
    /// </summary>
    internal sealed class TagesschauJingle : IDisposable
    {
        private const int SampleRate = 44100;
        private const int Channels = 1;
        private const int BitsPerSample = 16;
        private const int BytesPerSample = BitsPerSample / 8;

        private const double Beat = 0.7;               // seconds per beat
        private const double PeakAmplitude = 0.6;      // normalization target, avoids clipping

        // Attack and release per note, in seconds, to avoid clicks at note boundaries.
        private const double AttackSeconds = 0.015;
        private const double ReleaseSeconds = 0.08;

        // Relative strength of the fundamental and the 1st to 5th overtone (harmonics 1 to 6).
        private static readonly double[] PartialAmplitudes = { 1.0, 0.5, 0.33, 0.25, 0.2, 0.15 };

        #region Notes

        private struct Note
        {
            public readonly double Frequency; // Hz
            public readonly double Start;     // seconds from the beginning
            public readonly double Duration;  // seconds

            public Note(double frequency, double start, double duration)
            {
                Frequency = frequency;
                Start = start;
                Duration = duration;
            }
        }

        // Pitch names in Hz (equal temperament; the number is the octave, C4 = middle C).
        private const double G4 = 392.00;
        private const double C5 = 523.25;
        private const double D5 = 587.33;
        private const double F5 = 698.46;
        private const double G5 = 783.99;

        // Start and length are given in beats and converted to seconds.
        private static Note N(double frequency, double startBeat, double lengthBeats)
        {
            return new Note(frequency, startBeat * Beat, lengthBeats * Beat);
        }

        // 4/4, G minor (two flats): d'' - g' - d'' - c'' - f'' - g''
        private static readonly Note[] Melody =
        {
            N(D5, 0.0, 2.0),  // half note
            N(G4, 2.0, 3.0),  // half note tied to a quarter note across the barline
            N(D5, 5.0, 1.0),  // quarter notes
            N(C5, 6.0, 1.0),
            N(F5, 7.0, 1.0),
            N(G5, 8.0, 4.0),  // whole note
        };

        private static readonly double MelodyDuration = CalculateDuration(Melody);

        private static double CalculateDuration(Note[] notes)
        {
            var end = 0.0;
            foreach (var note in notes)
                end = Math.Max(end, note.Start + note.Duration);
            return end;
        }

        #endregion

        private static readonly Lazy<byte[]> CachedWav = new Lazy<byte[]>(BuildWav);

        private SoundPlayer _player;
        private MemoryStream _stream;
        private bool _disposed;

        /// <summary>Length of the melody in seconds.</summary>
        public static double DurationSeconds => MelodyDuration;

        /// <summary>Starts the fanfare asynchronously. Restarts it if it is already playing. Never throws.</summary>
        public void Play()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
                ReleasePlayer();

                _stream = new MemoryStream(CachedWav.Value, false);
                _player = new SoundPlayer(_stream);
                _player.Play(); // asynchronous, does not block the UI thread
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TagesschauJingle.Play failed: " + ex.Message);
                ReleasePlayer();
            }
        }

        /// <summary>Stops the fanfare if it is playing. Never throws.</summary>
        public void Stop()
        {
            try
            {
                _player?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TagesschauJingle.Stop failed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            ReleasePlayer();
            _disposed = true;
        }

        private void ReleasePlayer()
        {
            _player?.Dispose();
            _player = null;

            _stream?.Dispose();
            _stream = null;
        }

        #region WAV synthesis

        /// <summary>Builds the complete RIFF/WAVE file (16-bit PCM, mono, 44.1 kHz) in memory.</summary>
        internal static byte[] BuildWav()
        {
            var samples = Synthesize();
            var dataLength = samples.Length * BytesPerSample;

            using (var stream = new MemoryStream(44 + dataLength))
            using (var writer = new BinaryWriter(stream))
            {
                // RIFF header
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk (PCM)
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)Channels);
                writer.Write(SampleRate);
                writer.Write(SampleRate * Channels * BytesPerSample);
                writer.Write((short)(Channels * BytesPerSample));
                writer.Write((short)BitsPerSample);

                // data chunk
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);
                foreach (var sample in samples)
                    writer.Write(sample);

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static short[] Synthesize()
        {
            var sampleCount = (int)Math.Ceiling(MelodyDuration * SampleRate);
            var mix = new double[sampleCount];

            foreach (var note in Melody)
                AddNote(mix, note);

            // Normalize to the target peak so that nothing clips.
            var peak = 0.0;
            foreach (var value in mix)
                peak = Math.Max(peak, Math.Abs(value));

            var scale = peak > 0 ? PeakAmplitude / peak : 0.0;
            var samples = new short[sampleCount];
            for (var i = 0; i < sampleCount; i++)
                samples[i] = (short)Math.Round(mix[i] * scale * short.MaxValue);

            return samples;
        }

        private static void AddNote(double[] mix, Note note)
        {
            var first = (int)Math.Round(note.Start * SampleRate);
            var count = (int)Math.Round(note.Duration * SampleRate);

            for (var i = 0; i < count && first + i < mix.Length; i++)
            {
                var t = i / (double)SampleRate;
                var envelope = Envelope(t, note.Duration);

                var tone = 0.0;
                for (var partial = 0; partial < PartialAmplitudes.Length; partial++)
                {
                    var frequency = note.Frequency * (partial + 1);
                    tone += PartialAmplitudes[partial] * Math.Sin(2 * Math.PI * frequency * t);
                }

                mix[first + i] += envelope * tone;
            }
        }

        // Linear attack at the start and linear release at the end of each note.
        private static double Envelope(double t, double duration)
        {
            var attack = Math.Min(1.0, t / AttackSeconds);
            var release = Math.Min(1.0, (duration - t) / ReleaseSeconds);
            return Math.Max(0.0, Math.Min(attack, release));
        }

        #endregion
    }
}
