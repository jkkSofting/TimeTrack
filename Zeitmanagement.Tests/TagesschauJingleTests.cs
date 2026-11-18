// Target: .NET Framework + C# 7.3
using System;
using System.Text;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class TagesschauJingleTests
    {
        private const int SampleRate = 44100;

        [Fact]
        public void The_wav_has_a_valid_riff_and_wave_header()
        {
            var wav = TagesschauJingle.BuildWav();

            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.Equal(16, BitConverter.ToInt32(wav, 16));
            Assert.Equal(1, BitConverter.ToInt16(wav, 20));       // PCM
            Assert.Equal(1, BitConverter.ToInt16(wav, 22));       // mono
            Assert.Equal(SampleRate, BitConverter.ToInt32(wav, 24));
            Assert.Equal(SampleRate * 2, BitConverter.ToInt32(wav, 28));
            Assert.Equal(2, BitConverter.ToInt16(wav, 32));       // block align
            Assert.Equal(16, BitConverter.ToInt16(wav, 34));      // bits per sample
            Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        }

        [Fact]
        public void The_data_length_matches_the_duration()
        {
            var wav = TagesschauJingle.BuildWav();
            var dataLength = BitConverter.ToInt32(wav, 40);

            Assert.Equal(wav.Length - 44, dataLength);

            var expectedBytes = TagesschauJingle.DurationSeconds * SampleRate * 2;
            Assert.InRange(dataLength, expectedBytes - SampleRate * 2 * 0.01, expectedBytes + SampleRate * 2 * 0.01);
        }

        [Fact]
        public void No_sample_exceeds_the_headroom_limit()
        {
            var wav = TagesschauJingle.BuildWav();
            var limit = 0.99 * short.MaxValue;

            var peak = 0;
            for (var offset = 44; offset + 1 < wav.Length; offset += 2)
            {
                var sample = Math.Abs((int)BitConverter.ToInt16(wav, offset));
                if (sample > peak)
                    peak = sample;
            }

            Assert.True(peak > 0, "The melody must not be silent.");
            Assert.True(peak <= limit, $"Peak {peak} exceeds {limit}.");
        }

        [Fact]
        public void The_duration_is_about_eight_to_ten_seconds()
        {
            Assert.InRange(TagesschauJingle.DurationSeconds, 8.0, 10.0);
        }
    }
}
