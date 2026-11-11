// Target: .NET Framework + C# 7.3
using System;
using System.IO;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class SafeFileTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "TimeTrackTests", Guid.NewGuid().ToString("N"));

        public SafeFileTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Fact]
        public void A_new_file_is_created_without_leftovers()
        {
            var path = Path.Combine(_dir, "neu.pdf");

            SafeFile.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
        }

        [Fact]
        public void An_existing_file_is_replaced()
        {
            var path = Path.Combine(_dir, "alt.pdf");
            File.WriteAllBytes(path, new byte[] { 9, 9, 9, 9 });

            SafeFile.WriteAllBytes(path, new byte[] { 1 });

            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void A_locked_target_fails_keeps_the_old_content_and_leaves_no_temp_file()
        {
            var path = Path.Combine(_dir, "gesperrt.pdf");
            File.WriteAllBytes(path, new byte[] { 7, 7 });

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => SafeFile.WriteAllBytes(path, new byte[] { 1 }));
            }

            Assert.Equal(new byte[] { 7, 7 }, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(_dir));
        }

        [Fact]
        public void A_missing_folder_fails_cleanly()
        {
            var path = Path.Combine(_dir, "gibt-es-nicht", "x.pdf");

            Assert.ThrowsAny<IOException>(() => SafeFile.WriteAllBytes(path, new byte[] { 1 }));
            Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void Bad_arguments_are_rejected()
        {
            Assert.Throws<ArgumentException>(() => SafeFile.WriteAllBytes(" ", new byte[0]));
            Assert.Throws<ArgumentNullException>(() => SafeFile.WriteAllBytes(Path.Combine(_dir, "x"), null));
        }
    }
}
