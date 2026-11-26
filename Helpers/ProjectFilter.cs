using System;
using System.Collections.Generic;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    internal enum ProjectTypeFilter { Alle, Intern, Extern }

    internal enum ProjectRoundingFilter { Alle, Standard, Eigene, Aus }

    /// <summary>Sortierung im Filter-Panel. Standard = keine Sortierung; Spaltenkopf-Klicks sortieren dann wie gewohnt.</summary>
    internal enum ProjectSortChoice { Standard, Name, Kunde, Kostentraeger, Typ, Buchungen, Stunden }

    /// <summary>Was ein Projekt fuer den Filter preisgeben muss (vom ViewModel-Item implementiert, in Tests ersetzbar).</summary>
    internal interface IProjectFilterItem
    {
        string Projektname { get; }
        string Kunde { get; }
        string Kostentraeger { get; }
        bool IstExtern { get; }
        bool QuickSelectAktiv { get; }
        bool RundungGlobal { get; }
        int RundungEigenMinuten { get; }
        int Buchungen { get; }
    }

    /// <summary>Eintrag der Rundungs-ComboBox (DisplayMemberPath="Text").</summary>
    internal sealed class ProjectRoundingChoice
    {
        public ProjectRoundingChoice(ProjectRoundingFilter value, string text) { Value = value; Text = text; }
        public ProjectRoundingFilter Value { get; }
        public string Text { get; }
    }

    /// <summary>Eintrag der Sortier-ComboBox (DisplayMemberPath="Text").</summary>
    internal sealed class ProjectSortOption
    {
        public ProjectSortOption(ProjectSortChoice value, string text) { Value = value; Text = text; }
        public ProjectSortChoice Value { get; }
        public string Text { get; }
    }

    internal sealed class ProjectFilterCriteria
    {
        public string SearchText { get; set; }
        public bool ShowInactive { get; set; }
        public ProjectTypeFilter Type { get; set; } = ProjectTypeFilter.Alle;
        /// <summary>ProjectFilter.AllCustomers (Standard), ProjectFilter.NoCustomer oder ein Kundenname.</summary>
        public string Customer { get; set; } = ProjectFilter.AllCustomers;
        public ProjectRoundingFilter Rounding { get; set; } = ProjectRoundingFilter.Alle;
        public bool OnlyWithBookings { get; set; }
    }

    internal struct ProjectFilterCounts
    {
        public int Shown;           // Treffer mit allen Kriterien
        public int Base;            // Treffer, wenn nur die Aktiv-Regel (ShowInactive) zaehlt
        public int HiddenInactive;  // inaktive Projekte, die alle ANDEREN Kriterien erfuellen, aber ausgeblendet sind
    }

    internal static class ProjectFilter
    {
        public const string AllCustomers = "Alle Kunden";
        public const string NoCustomer = "(ohne Kunde)";

        public static readonly ProjectRoundingChoice[] RoundingChoices =
        {
            new ProjectRoundingChoice(ProjectRoundingFilter.Alle, "Alle Rundungen"),
            new ProjectRoundingChoice(ProjectRoundingFilter.Standard, "Standard (geerbt)"),
            new ProjectRoundingChoice(ProjectRoundingFilter.Eigene, "Eigene Rundung"),
            new ProjectRoundingChoice(ProjectRoundingFilter.Aus, "Keine Rundung")
        };

        /// <summary>Alle Kriterien inkl. Aktiv-Regel.</summary>
        public static bool Matches(IProjectFilterItem item, ProjectFilterCriteria c)
        {
            if (item == null || c == null) return false;
            if (!c.ShowInactive && !item.QuickSelectAktiv) return false;
            return MatchesIgnoringActive(item, c);
        }

        /// <summary>Alle Kriterien AUSSER der Aktiv-Regel (fuer den Hinweis "n inaktive ausgeblendet").</summary>
        public static bool MatchesIgnoringActive(IProjectFilterItem item, ProjectFilterCriteria c)
        {
            if (item == null || c == null) return false;
            if (!MatchesSearch(item, c.SearchText)) return false;

            if (c.Type == ProjectTypeFilter.Extern && !item.IstExtern) return false;
            if (c.Type == ProjectTypeFilter.Intern && item.IstExtern) return false;

            if (!MatchesCustomer(item, c.Customer)) return false;

            switch (c.Rounding)
            {
                case ProjectRoundingFilter.Standard:
                    if (!item.RundungGlobal) return false;
                    break;
                case ProjectRoundingFilter.Eigene:
                    if (item.RundungGlobal || RoundingOptions.Normalize(item.RundungEigenMinuten) == 0) return false;
                    break;
                case ProjectRoundingFilter.Aus:
                    if (item.RundungGlobal || RoundingOptions.Normalize(item.RundungEigenMinuten) != 0) return false;
                    break;
            }

            if (c.OnlyWithBookings && item.Buchungen <= 0) return false;
            return true;
        }

        /// <summary>Gleiche Semantik wie bisher ProjectsViewModel.MatchesSearch: Name, Kunde, Kostentraeger, ohne Gross-/Kleinschreibung.</summary>
        public static bool MatchesSearch(IProjectFilterItem item, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return true;
            var q = searchText.Trim();
            return Contains(item.Projektname, q) || Contains(item.Kunde, q) || Contains(item.Kostentraeger, q);
        }

        private static bool Contains(string value, string q)
        {
            return (value?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private static bool MatchesCustomer(IProjectFilterItem item, string customer)
        {
            if (string.IsNullOrEmpty(customer) || customer == AllCustomers) return true;
            if (customer == NoCustomer) return string.IsNullOrWhiteSpace(item.Kunde);
            return string.Equals(item.Kunde?.Trim(), customer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Anzahl der gesetzten Panel-Filter (Typ, Kunde, Rundung, Nur mit Buchungen). Suche/Inaktiv zaehlen nicht.</summary>
        public static int CountActive(ProjectFilterCriteria c)
        {
            if (c == null) return 0;
            int n = 0;
            if (c.Type != ProjectTypeFilter.Alle) n++;
            if (!string.IsNullOrEmpty(c.Customer) && c.Customer != AllCustomers) n++;
            if (c.Rounding != ProjectRoundingFilter.Alle) n++;
            if (c.OnlyWithBookings) n++;
            return n;
        }

        /// <summary>Eintraege der Kunden-ComboBox: "Alle Kunden", dann die Kunden alphabetisch (ohne Duplikate, ohne Gross-/Kleinschreibung), zuletzt "(ohne Kunde)" falls ein Projekt keinen Kunden hat.</summary>
        public static List<string> BuildCustomerList(IEnumerable<string> customers)
        {
            var list = new List<string> { AllCustomers };
            bool anyEmpty = false;
            var names = new List<string>();
            foreach (var raw in customers ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) { anyEmpty = true; continue; }
                names.Add(raw.Trim());
            }
            list.AddRange(names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
            if (anyEmpty) list.Add(NoCustomer);
            return list;
        }

        public static ProjectFilterCounts Count(IEnumerable<IProjectFilterItem> items, ProjectFilterCriteria c)
        {
            var r = new ProjectFilterCounts();
            if (items == null || c == null) return r;
            foreach (var it in items)
            {
                bool other = MatchesIgnoringActive(it, c);
                if (!other) continue;
                if (c.ShowInactive || it.QuickSelectAktiv) r.Shown++;
                else r.HiddenInactive++;
            }
            // Base: nur Aktiv-Regel
            foreach (var it in items)
                if (c.ShowInactive || it.QuickSelectAktiv) r.Base++;
            return r;
        }

        /// <summary>
        /// Statuszeile unter dem Titel; null, wenn es nichts zu sagen gibt.
        /// "4 von 12 Projekten" nur, wenn shown != baseTotal; "1 inaktives Projekt ausgeblendet"/"n inaktive Projekte ausgeblendet" bei hiddenInactive > 0; getrennt durch " · ".
        /// </summary>
        public static string StatusText(ProjectFilterCounts n)
        {
            var parts = new List<string>();
            if (n.Shown != n.Base)
                parts.Add(n.Shown + " von " + n.Base + (n.Base == 1 ? " Projekt" : " Projekten"));
            if (n.HiddenInactive > 0)
                parts.Add(n.HiddenInactive == 1 ? "1 inaktives Projekt ausgeblendet" : n.HiddenInactive + " inaktive Projekte ausgeblendet");
            return parts.Count == 0 ? null : string.Join(" · ", parts);   // U+00B7 = Mittelpunkt
        }
    }

    /// <summary>Sortierung der Projektliste: Zuordnung Auswahl -> Property-Name (fuer ICollectionView.SortDescriptions).</summary>
    internal static class ProjectSort
    {
        public static readonly ProjectSortOption[] Choices =
        {
            new ProjectSortOption(ProjectSortChoice.Standard, "Spaltenkopf"),
            new ProjectSortOption(ProjectSortChoice.Name, "Name"),
            new ProjectSortOption(ProjectSortChoice.Kunde, "Kunde"),
            new ProjectSortOption(ProjectSortChoice.Kostentraeger, "Kostenträger"),
            new ProjectSortOption(ProjectSortChoice.Typ, "Typ"),
            new ProjectSortOption(ProjectSortChoice.Buchungen, "Buchungen"),
            new ProjectSortOption(ProjectSortChoice.Stunden, "Stunden")
        };

        /// <summary>Property-Name am ProjectItemVM fuer die Sortierung; null bei Standard (keine eigene Sortierung).</summary>
        public static string PropertyName(ProjectSortChoice choice)
        {
            switch (choice)
            {
                case ProjectSortChoice.Name: return "Projektname";
                case ProjectSortChoice.Kunde: return "Kunde";
                case ProjectSortChoice.Kostentraeger: return "Kostentraeger";
                case ProjectSortChoice.Typ: return "TypText";
                case ProjectSortChoice.Buchungen: return "Buchungen";
                case ProjectSortChoice.Stunden: return "StundenAnzeige";
                default: return null;
            }
        }
    }
}
