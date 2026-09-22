using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Talks to the GitHub Releases API for https://github.com/jkkSofting/TimeTrack to find out
    /// whether a newer installer than the running version has been published, and can download it.
    /// </summary>
    internal static class UpdateChecker
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/jkkSofting/TimeTrack/releases/latest";

        public sealed class UpdateInfo
        {
            public Version Version { get; set; }
            public string VersionText { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseUrl { get; set; }
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }

            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }

            [DataMember(Name = "assets")]
            public GitHubAsset[] Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }

        static UpdateChecker()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>
        /// Returns info about the latest published release if it is newer than the running
        /// assembly version and ships an .exe asset, otherwise null.
        /// </summary>
        public static async Task<UpdateInfo> GetAvailableUpdateAsync()
        {
            using (var client = CreateClient())
            {
                var response = await client.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                GitHubRelease release;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                    release = (GitHubRelease)serializer.ReadObject(stream);
                }

                if (release == null || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName))
                    return null;

                if (!Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latestVersion))
                    return null;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (latestVersion <= currentVersion)
                    return null;

                var asset = release.Assets?.FirstOrDefault(a =>
                    a.Name != null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                    return null;

                return new UpdateInfo
                {
                    Version = latestVersion,
                    VersionText = latestVersion.ToString(),
                    DownloadUrl = asset.BrowserDownloadUrl,
                    ReleaseUrl = release.HtmlUrl
                };
            }
        }

        /// <summary>
        /// Downloads the installer for the given update into the user's temp folder and returns
        /// the local file path.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(UpdateInfo update)
        {
            var fileName = "TimeTracker-Update-" + update.VersionText + ".exe";
            var destinationPath = Path.Combine(Path.GetTempPath(), fileName);

            using (var client = CreateClient())
            using (var response = await client.GetAsync(update.DownloadUrl).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var fileStream = File.Create(destinationPath))
                {
                    await response.Content.CopyToAsync(fileStream).ConfigureAwait(false);
                }
            }

            return destinationPath;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TimeTrack-Updater");
            return client;
        }
    }
}
