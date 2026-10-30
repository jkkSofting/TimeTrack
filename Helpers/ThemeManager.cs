using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// The motifs the user can choose from (stored by name in the settings). Each one comes in
    /// a light and a dark variant, see <see cref="ThemeMode"/>.
    /// </summary>
    internal enum AppTheme
    {
        Standard,
        Forest,
        Ocean,
        Halloween,
        Christmas,
        Easter,
        VfbStuttgart,
        WerderBremen
    }

    /// <summary>Light or dark variant of the theme (stored by name in the settings).</summary>
    internal enum ThemeMode
    {
        /// <summary>Follows the Windows app mode.</summary>
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Applies the app's colour scheme: a motif (<see cref="AppTheme"/>) in its light or dark
    /// variant. Every variant is a palette dictionary Themes/Palettes/[Motif].[Light|Dark].xaml
    /// with the same keys ("Theme.Text", "Theme.Card", ...); all views and styles reference
    /// those keys via DynamicResource, so swapping the palette in the application resources
    /// recolours the whole app on the fly.
    /// The motif is the one chosen in the settings, except during a holiday season (Halloween,
    /// Christmas, Easter: <see cref="SeasonDaysBefore"/> days before until
    /// <see cref="SeasonDaysAfter"/> days after) if seasonal themes are enabled; light or dark
    /// always follows the chosen mode. <see cref="ThemeMode.System"/> follows the Windows
    /// light/dark app mode and updates when it changes.
    /// The penguin mascot wears a matching accessory ("Theme.MascotAccessory", drawn in
    /// Views/Mascot.xaml), e.g. a witch hat for Halloween or a scarf for the football themes.
    /// </summary>
    internal static class ThemeManager
    {
        public const int SeasonDaysBefore = 14;
        public const int SeasonDaysAfter = 7;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>Number of category colours every palette defines (Theme.Data0..).</summary>
        public const int DataColorCount = 8;

        private static readonly DispatcherTimer DateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        private static ResourceDictionary _palette;
        private static Color[] _dataPalette = new Color[0];
        private static bool _initialized;

        /// <summary>Raised after a different palette has been applied.</summary>
        public static event EventHandler ThemeChanged;

        /// <summary>The motif that is actually shown (the chosen one or the holiday one).</summary>
        public static AppTheme Effective { get; private set; } = AppTheme.Standard;

        /// <summary>The holiday motif currently overriding the chosen one, if any.</summary>
        public static AppTheme? ActiveSeason { get; private set; }

        /// <summary>True if the dark variant is shown.</summary>
        public static bool IsDark { get; private set; } = true;

        /// <summary>
        /// Category colours of the current palette (Theme.Data0..7) for projects in charts and
        /// graphs, to be handed out in this order. Each palette's set is validated so that
        /// neighbours stay distinguishable (also with red-green colour blindness) and every colour
        /// has at least 3:1 contrast on the cards. A new list instance after every theme change.
        /// </summary>
        public static IReadOnlyList<Color> DataPalette => _dataPalette;

        public static AppTheme Selected => Parse(Properties.Settings.Default.Theme);

        public static ThemeMode SelectedMode =>
            Enum.TryParse(Properties.Settings.Default.ThemeMode, out ThemeMode mode) ? mode : ThemeMode.System;

        public static IReadOnlyList<AppTheme> AllThemes { get; } = (AppTheme[])Enum.GetValues(typeof(AppTheme));

        public static bool IsSeasonal(AppTheme theme) =>
            theme == AppTheme.Halloween || theme == AppTheme.Christmas || theme == AppTheme.Easter;

        public static string DisplayName(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Standard: return "Standard";
                case AppTheme.Forest: return "Wald";
                case AppTheme.Ocean: return "Meer";
                case AppTheme.Halloween: return "Halloween";
                case AppTheme.Christmas: return "Weihnachten";
                case AppTheme.Easter: return "Ostern";
                case AppTheme.VfbStuttgart: return "VfB Stuttgart";
                case AppTheme.WerderBremen: return "Werder Bremen";
                default: return theme.ToString();
            }
        }

        public static string DisplayName(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light: return "Hell";
                case ThemeMode.Dark: return "Dunkel";
                default: return "Wie Windows";
            }
        }

        /// <summary>Unknown names (also the former "System"/"Light"/"Dark") fall back to the standard motif.</summary>
        public static AppTheme Parse(string value) =>
            Enum.TryParse(value, out AppTheme theme) && Enum.IsDefined(typeof(AppTheme), theme) ? theme : AppTheme.Standard;

        /// <summary>Call once at startup, before the first window is created.</summary>
        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Properties.Settings.Default.PropertyChanged += OnSettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            DateTimer.Tick += (s, e) => Refresh(); // a season may start or end while the app runs
            DateTimer.Start();

            // Colour the native title bar of every window that opens from now on.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => ApplyWindowChrome((Window)s)));

            Refresh(force: true);
        }

        public static void Shutdown()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            DateTimer.Stop();
        }

        /// <summary>Re-evaluates settings, Windows mode and date and swaps the palette if needed.</summary>
        public static void Refresh(bool force = false)
        {
            var settings = Properties.Settings.Default;
            var season = settings.SeasonalThemesEnabled ? SeasonOn(DateTime.Today) : null;
            var theme = season ?? Selected;
            var dark = ResolveDark(SelectedMode);

            if (!force && theme == Effective && dark == IsDark && season == ActiveSeason)
                return;

            ActiveSeason = season;
            var changed = force || theme != Effective || dark != IsDark;
            Effective = theme;
            IsDark = dark;

            if (changed)
            {
                ApplyPalette(theme, dark);
                foreach (Window window in Application.Current.Windows)
                    ApplyWindowChrome(window);
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Loads a palette on its own, e.g. for the preview tiles in the settings.</summary>
        public static ResourceDictionary LoadPalette(AppTheme theme, bool dark) =>
            new ResourceDictionary { Source = new Uri($"pack://application:,,,/Zeitmanagement;component/Themes/Palettes/{theme}.{(dark ? "Dark" : "Light")}.xaml") };

        /// <summary>A brush of the current palette, e.g. "Theme.Text".</summary>
        public static Brush GetBrush(string key) =>
            (Brush)Application.Current.TryFindResource(key) ?? Brushes.Transparent;

        /// <summary>The holiday theme whose season includes <paramref name="date"/>, if any.</summary>
        public static AppTheme? SeasonOn(DateTime date)
        {
            foreach (var theme in new[] { AppTheme.Halloween, AppTheme.Christmas, AppTheme.Easter })
            {
                var (from, to) = SeasonRange(theme, date.Year);
                if (date.Date >= from && date.Date <= to)
                    return theme;
            }

            return null;
        }

        /// <summary>First and last day on which a holiday theme is shown automatically in the given year.</summary>
        public static (DateTime From, DateTime To) SeasonRange(AppTheme theme, int year)
        {
            DateTime day;
            switch (theme)
            {
                case AppTheme.Halloween: day = new DateTime(year, 10, 31); break;
                case AppTheme.Christmas: day = new DateTime(year, 12, 24); break;
                case AppTheme.Easter: day = EasterSunday(year); break;
                default: throw new ArgumentOutOfRangeException(nameof(theme), theme, "Not a holiday theme.");
            }

            return (day.AddDays(-SeasonDaysBefore), day.AddDays(SeasonDaysAfter));
        }

        /// <summary>Easter Sunday (Gregorian calendar), anonymous Gregorian algorithm.</summary>
        public static DateTime EasterSunday(int year)
        {
            var a = year % 19;
            var b = year / 100;
            var c = year % 100;
            var d = b / 4;
            var e = b % 4;
            var f = (b + 8) / 25;
            var g = (b - f + 1) / 3;
            var h = (19 * a + b - d - g + 15) % 30;
            var i = c / 4;
            var k = c % 4;
            var l = (32 + 2 * e + 2 * i - h - k) % 7;
            var m = (a + 11 * h + 22 * l) / 451;
            var month = (h + l - 7 * m + 114) / 31;
            var dayOfMonth = (h + l - 7 * m + 114) % 31 + 1;
            return new DateTime(year, month, dayOfMonth);
        }

        private static bool ResolveDark(ThemeMode mode) =>
            mode == ThemeMode.System ? !IsWindowsInLightMode() : mode == ThemeMode.Dark;

        private static bool IsWindowsInLightMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reading the Windows app mode failed: {ex}");
                return false;
            }
        }

        private static void ApplyPalette(AppTheme theme, bool dark)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            var palette = LoadPalette(theme, dark);

            // App.xaml merges a default palette first; replace whichever palette is in place.
            var index = _palette != null ? merged.IndexOf(_palette) : merged.ToList().FindIndex(IsPalette);
            if (index >= 0)
                merged[index] = palette;
            else
                merged.Insert(0, palette);

            _palette = palette;
            _dataPalette = Enumerable.Range(0, DataColorCount)
                .Select(i => ((SolidColorBrush)palette["Theme.Data" + i]).Color)
                .ToArray();

            var resources = Application.Current.Resources;
            resources["Theme.MascotAccessory"] = resources["MascotAccessory." + theme] ?? resources["MascotAccessory.None"];
        }

        private static bool IsPalette(ResourceDictionary dictionary) =>
            dictionary.Source != null && dictionary.Source.OriginalString.Contains("Themes/Palettes/");

        /// <summary>Gives the native title bar the colours of the current theme (Windows 10 2004+/11).</summary>
        public static void ApplyWindowChrome(Window window)
        {
            if (window.WindowStyle == WindowStyle.None)
                return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                var dark = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

                // Windows 11 only; older versions ignore it and keep the dark/light default.
                if (Application.Current.TryFindResource("Theme.TitleBarColor") is Color caption)
                {
                    var value = ToColorRef(caption);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref value, sizeof(int));
                }
                if (Application.Current.TryFindResource("Theme.TitleBarTextColor") is Color text)
                {
                    var value = ToColorRef(text);
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref value, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                // Cosmetic only.
                Debug.WriteLine($"Colouring the title bar failed: {ex}");
            }
        }

        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        private static void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Properties.Settings.Theme)
                || e.PropertyName == nameof(Properties.Settings.ThemeMode)
                || e.PropertyName == nameof(Properties.Settings.SeasonalThemesEnabled))
                Refresh();
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Raised on a system thread; the Windows app mode change arrives as "General".
            if (e.Category == UserPreferenceCategory.General)
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => Refresh()));
        }
    }
}
