using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Zeitmanagement.Helpers;
using Zeitmanagement.MVVM;

namespace Zeitmanagement.ViewModel
{
    /// <summary>
    /// The "Hilfe &amp; Neuigkeiten" page: the changelog (<see cref="Changelog"/>) and the help topics
    /// (<see cref="HelpContent"/>), switchable with a segmented control and filtered by one search box.
    /// Also tracks whether the user has seen the changelog of the installed version yet, for the
    /// "Neu" badge in the navigation.
    /// </summary>
    internal class HelpViewModel : BaseViewModel
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        private readonly Action<string> _openPage;
        private readonly Version _installedVersion;
        /// <summary>Last changelog version the user had seen when the app started, null if never.</summary>
        private readonly Version _seenAtStartup;

        public HelpViewModel(Action<string> openPage)
        {
            _openPage = openPage;
            _installedVersion = Assembly.GetExecutingAssembly().GetName().Version;
            _seenAtStartup = ParseVersion(Properties.Settings.Default.LastSeenChangelogVersion);

            InstalledVersionText = Normalize(_installedVersion).ToString(3);
            HasUnseenNews = _seenAtStartup == null || _seenAtStartup < Normalize(_installedVersion);

            OpenPageCommand = new DelegateCommand(page => _openPage(page as string), page => page is string);
            ClearSearchCommand = new DelegateCommand(_ => SearchText = string.Empty);

            ApplyFilter();
        }

        public string InstalledVersionText { get; }

        /// <summary>
        /// True if the app was updated since the user last looked at the changelog (not on the very
        /// first start, where there is nothing to compare against): worth a tray hint at startup.
        /// </summary>
        public bool WasUpdated => _seenAtStartup != null && _seenAtStartup < Normalize(_installedVersion);

        private bool _hasUnseenNews;
        /// <summary>Shows the "Neu" badge in the navigation until the changelog tab has been opened.</summary>
        public bool HasUnseenNews
        {
            get => _hasUnseenNews;
            private set => SetProperty(ref _hasUnseenNews, value);
        }

        private bool _isChangelogTab = true;
        public bool IsChangelogTab
        {
            get => _isChangelogTab;
            set
            {
                if (!SetProperty(ref _isChangelogTab, value))
                    return;
                RaisePropertyChanged(nameof(IsHelpTab));
                RaisePropertyChanged(nameof(SearchPlaceholder));
                RaisePropertyChanged(nameof(IsEmpty));
                if (value)
                    MarkChangelogSeen();
            }
        }

        public bool IsHelpTab
        {
            get => !IsChangelogTab;
            set => IsChangelogTab = !value;
        }

        public string SearchPlaceholder => IsChangelogTab ? "Neuigkeiten durchsuchen" : "Hilfe durchsuchen";

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? string.Empty))
                    return;
                RaisePropertyChanged(nameof(HasSearchText));
                ApplyFilter();
            }
        }

        public bool HasSearchText => SearchText.Length > 0;

        private IReadOnlyList<ReleaseItem> _releases;
        public IReadOnlyList<ReleaseItem> Releases
        {
            get => _releases;
            private set => SetProperty(ref _releases, value);
        }

        private IReadOnlyList<HelpTopic> _topics;
        public IReadOnlyList<HelpTopic> Topics
        {
            get => _topics;
            private set => SetProperty(ref _topics, value);
        }

        /// <summary>True if the search leaves nothing to show on the current tab.</summary>
        public bool IsEmpty => IsChangelogTab ? Releases.Count == 0 : Topics.Count == 0;

        public DelegateCommand OpenPageCommand { get; }
        public DelegateCommand ClearSearchCommand { get; }

        public override void Refresh()
        {
            if (IsChangelogTab)
                MarkChangelogSeen();
        }

        /// <summary>Switches to the changelog tab, e.g. when the page is opened from the update hint.</summary>
        public void ShowChangelog()
        {
            SearchText = string.Empty;
            IsChangelogTab = true;
        }

        private void MarkChangelogSeen()
        {
            if (!HasUnseenNews)
                return;

            HasUnseenNews = false;
            Properties.Settings.Default.LastSeenChangelogVersion = InstalledVersionText;
            Properties.Settings.Default.Save();
        }

        private void ApplyFilter()
        {
            var terms = SearchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            Releases = Changelog.Releases
                .OrderByDescending(r => r.Version)
                .Select(r => CreateReleaseItem(r, terms))
                .Where(r => r != null)
                .ToList();

            Topics = HelpContent.Topics
                .Where(t => Matches(terms, new[] { t.Title, t.Summary }.Concat(t.Steps)))
                .ToList();

            RaisePropertyChanged(nameof(IsEmpty));
        }

        /// <summary>
        /// Wraps a release for display. While searching, keeps only the matching changes (all of
        /// them if the version or title matches) and drops releases without any match.
        /// </summary>
        private ReleaseItem CreateReleaseItem(ChangelogRelease release, string[] terms)
        {
            var headerMatches = Matches(terms, new[] { release.VersionText, release.Title });
            var changes = release.Changes
                .Where(c => headerMatches || Matches(terms, new[] { c.Text, c.Kind.ToString() }))
                .ToList();
            if (changes.Count == 0)
                return null;

            var installed = Normalize(_installedVersion);
            var isUnseen = release.Version <= installed &&
                           (_seenAtStartup == null ? release.Version == installed : release.Version > _seenAtStartup);

            return new ReleaseItem(
                release.VersionText,
                release.Date.ToString("d. MMMM yyyy", German),
                release.Title,
                changes,
                isInstalled: release.Version == installed,
                isUnseen: isUnseen,
                isUpcoming: release.Version > installed);
        }

        /// <summary>Every search term must appear in at least one of the texts (ignoring case).</summary>
        private static bool Matches(string[] terms, IEnumerable<string> texts)
        {
            var list = texts.Where(t => !string.IsNullOrEmpty(t)).ToList();
            return terms.All(term => list.Any(t => t.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0));
        }

        private static Version ParseVersion(string text) =>
            Version.TryParse(text, out var version) ? Normalize(version) : null;

        /// <summary>Drops the fourth version part, so "2026.43.0.0" from the assembly equals "2026.43.0" from the changelog.</summary>
        private static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    internal sealed class ReleaseItem
    {
        public ReleaseItem(string version, string dateText, string title, IReadOnlyList<ChangelogChange> changes,
            bool isInstalled, bool isUnseen, bool isUpcoming)
        {
            Version = version;
            DateText = dateText;
            Title = title;
            Changes = changes;
            IsInstalled = isInstalled;
            IsUnseen = isUnseen;
            IsUpcoming = isUpcoming;
        }

        public string Version { get; }
        public string DateText { get; }
        public string Title { get; }
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public IReadOnlyList<ChangelogChange> Changes { get; }
        public bool IsInstalled { get; }
        /// <summary>Released since the user last looked at the changelog.</summary>
        public bool IsUnseen { get; }
        /// <summary>Newer than the installed version: only happens in development builds.</summary>
        public bool IsUpcoming { get; }
    }
}
