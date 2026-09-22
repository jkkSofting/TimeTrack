using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Owns the periodic background check against GitHub Releases and exposes the outcome to
    /// whichever view (tray notification, settings page) wants to react to it. The actual
    /// download/install is only ever started by an explicit user action.
    /// </summary>
    internal static class UpdateService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);

        private static DispatcherTimer _timer;
        private static bool _checkInProgress;

        public static UpdateChecker.UpdateInfo LatestUpdate { get; private set; }

        /// <summary>Raised on the UI thread whenever a check finds a newer version than before.</summary>
        public static event Action<UpdateChecker.UpdateInfo> UpdateAvailable;

        public static void Start()
        {
            if (_timer != null)
                return;

            _timer = new DispatcherTimer { Interval = CheckInterval };
            _timer.Tick += async (s, e) => await CheckIfEnabledAsync().ConfigureAwait(true);
            _timer.Start();

            _ = RunFirstCheckAsync();
        }

        private static async Task RunFirstCheckAsync()
        {
            await Task.Delay(FirstCheckDelay).ConfigureAwait(true);
            await CheckIfEnabledAsync().ConfigureAwait(true);
        }

        private static Task CheckIfEnabledAsync()
        {
            return Properties.Settings.Default.AutoUpdateCheckEnabled ? CheckNowAsync() : Task.CompletedTask;
        }

        /// <summary>
        /// Checks GitHub for a newer release, regardless of the "automatic check" setting - this
        /// also backs the manual "Check now" button. A check already running is simply skipped.
        /// </summary>
        public static async Task<UpdateChecker.UpdateInfo> CheckNowAsync()
        {
            if (_checkInProgress)
                return LatestUpdate;

            _checkInProgress = true;
            try
            {
                var update = await UpdateChecker.GetAvailableUpdateAsync().ConfigureAwait(true);
                LatestUpdate = update;

                if (update != null)
                    UpdateAvailable?.Invoke(update);

                return update;
            }
            catch
            {
                return null;
            }
            finally
            {
                _checkInProgress = false;
            }
        }

        /// <summary>
        /// Downloads the installer for the given update and launches it, then shuts the running
        /// app down so the installer can overwrite its files.
        /// </summary>
        public static async Task DownloadAndInstallAsync(UpdateChecker.UpdateInfo update)
        {
            var installerPath = await UpdateChecker.DownloadInstallerAsync(update).ConfigureAwait(true);

            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
    }
}
