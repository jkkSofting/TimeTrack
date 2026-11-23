// Target: .NET Framework + C# 7.3
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class SingleInstanceGuardTests
    {
        // Every test uses its own name so tests (and a running TimeTrack) never see each other.
        private static string NewName() => "TimeTrack.Tests." + Guid.NewGuid().ToString("N");

        /// <summary>
        /// A mutex belongs to the thread that took it, so a "second process" has to be another
        /// thread. Runs the action on its own thread and returns what it returned.
        /// </summary>
        private static T OnOtherThread<T>(Func<T> func) => Task.Factory.StartNew(func, TaskCreationOptions.LongRunning).GetAwaiter().GetResult();

        [Fact]
        public void First_instance_becomes_the_running_instance()
        {
            using (var guard = new SingleInstanceGuard(NewName()))
            {
                Assert.True(guard.TryAcquire(TimeSpan.Zero));
                Assert.True(guard.IsPrimaryInstance);
            }
        }

        [Fact]
        public void Second_instance_does_not_get_the_lock()
        {
            var name = NewName();
            using (var first = new SingleInstanceGuard(name))
            {
                Assert.True(first.TryAcquire(TimeSpan.Zero));

                var secondGotIt = OnOtherThread(() =>
                {
                    using (var second = new SingleInstanceGuard(name))
                        return second.TryAcquire(TimeSpan.Zero);
                });

                Assert.False(secondGotIt);
            }
        }

        [Fact]
        public void Lock_is_free_again_after_the_running_instance_is_disposed()
        {
            var name = NewName();
            var first = new SingleInstanceGuard(name);
            Assert.True(first.TryAcquire(TimeSpan.Zero));
            first.Dispose();

            var secondGotIt = OnOtherThread(() =>
            {
                using (var second = new SingleInstanceGuard(name))
                    return second.TryAcquire(TimeSpan.Zero);
            });

            Assert.True(secondGotIt);
        }

        [Fact]
        public void Restarted_instance_waits_for_the_old_one_to_let_go()
        {
            var name = NewName();
            var first = new SingleInstanceGuard(name);
            Assert.True(first.TryAcquire(TimeSpan.Zero));

            var secondGotIt = Task.Factory.StartNew(() =>
            {
                using (var second = new SingleInstanceGuard(name))
                    return second.TryAcquire(TimeSpan.FromSeconds(10));
            }, TaskCreationOptions.LongRunning);

            Thread.Sleep(300);
            Assert.False(secondGotIt.IsCompleted);

            first.Dispose();

            Assert.True(secondGotIt.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(secondGotIt.Result);
        }

        [Fact]
        public void Signal_without_a_running_instance_reports_false()
        {
            using (var guard = new SingleInstanceGuard(NewName()))
                Assert.False(guard.SignalRunningInstance());
        }

        [Fact]
        public void Signal_reaches_the_listener_of_the_running_instance()
        {
            var name = NewName();
            using (var first = new SingleInstanceGuard(name))
            {
                Assert.True(first.TryAcquire(TimeSpan.Zero));
                var activated = new ManualResetEventSlim(false);
                first.StartListening(() => activated.Set());

                var signalled = OnOtherThread(() =>
                {
                    using (var second = new SingleInstanceGuard(name))
                        return second.SignalRunningInstance();
                });

                Assert.True(signalled);
                Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public void Listening_requires_the_lock()
        {
            using (var guard = new SingleInstanceGuard(NewName()))
                Assert.Throws<InvalidOperationException>(() => guard.StartListening(() => { }));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(new string[0], false)]
        [InlineData(new[] { "foo" }, false)]
        [InlineData(new[] { "--restart" }, true)]
        [InlineData(new[] { "x", "--RESTART" }, true)]
        public void IsRestart_detects_the_restart_argument(string[] args, bool expected)
        {
            Assert.Equal(expected, SingleInstanceGuard.IsRestart(args));
        }

        [Fact]
        public void Debug_builds_are_not_restricted()
        {
#if DEBUG
            Assert.False(SingleInstanceGuard.IsRestrictedBuild);
#else
            Assert.True(SingleInstanceGuard.IsRestrictedBuild);
#endif
        }
    }
}
