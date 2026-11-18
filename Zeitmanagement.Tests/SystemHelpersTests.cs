// Target: .NET Framework + C# 7.3
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class SystemHelpersTests
    {
        [Fact]
        public void SessionLockState_IsLocked_does_not_throw()
        {
            Assert.Null(Record.Exception(() => SessionLockState.IsLocked()));
        }

        [Fact]
        public void SystemAudio_IsAudible_does_not_throw()
        {
            Assert.Null(Record.Exception(() => SystemAudio.IsAudible()));
        }
    }
}
