// Target: .NET Framework + C# 7.3
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Projektseite: Filter-Panel (Typ, Kunde, Rundung, nur mit Buchungen), Zaehler, Statuszeile und Sortierzuordnung.</summary>
    public class ProjectFilterTests
    {
        private sealed class Item : IProjectFilterItem
        {
            public string Projektname { get; set; } = "P";
            public string Kunde { get; set; }
            public string Kostentraeger { get; set; }
            public bool IstExtern { get; set; }
            public bool QuickSelectAktiv { get; set; } = true;
            public bool RundungGlobal { get; set; } = true;
            public int RundungEigenMinuten { get; set; }
            public int Buchungen { get; set; } = 1;
        }

        [Fact]
        public void DefaultCriteriaMatchActiveItems()
        {
            var active = new Item { QuickSelectAktiv = true };
            var inactive = new Item { QuickSelectAktiv = false };
            Assert.True(ProjectFilter.Matches(active, new ProjectFilterCriteria()));
            Assert.False(ProjectFilter.Matches(inactive, new ProjectFilterCriteria()));
            Assert.True(ProjectFilter.Matches(inactive, new ProjectFilterCriteria { ShowInactive = true }));
        }

        [Fact]
        public void SearchMatchesNameCustomerCarrierIgnoringCase()
        {
            var item = new Item { Projektname = "Umbau Halle", Kunde = "ACME GmbH", Kostentraeger = "PSP-42" };
            Assert.True(ProjectFilter.MatchesSearch(item, "umbau"));
            Assert.True(ProjectFilter.MatchesSearch(item, "acme"));
            Assert.True(ProjectFilter.MatchesSearch(item, "  psp-4  "));
            Assert.False(ProjectFilter.MatchesSearch(item, "xyz"));
        }

        [Fact]
        public void BlankSearchMatchesAllIncludingNullFields()
        {
            var item = new Item { Projektname = null, Kunde = null, Kostentraeger = null };
            Assert.True(ProjectFilter.MatchesSearch(item, null));
            Assert.True(ProjectFilter.MatchesSearch(item, "   "));
            Assert.False(ProjectFilter.MatchesSearch(item, "a"));
        }

        [Fact]
        public void TypeExternOnlyExternal()
        {
            var ext = new Item { IstExtern = true };
            var intern = new Item { IstExtern = false };
            var c = new ProjectFilterCriteria { Type = ProjectTypeFilter.Extern };
            Assert.True(ProjectFilter.MatchesIgnoringActive(ext, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(intern, c));
        }

        [Fact]
        public void TypeInternOnlyInternal()
        {
            var ext = new Item { IstExtern = true };
            var intern = new Item { IstExtern = false };
            var c = new ProjectFilterCriteria { Type = ProjectTypeFilter.Intern };
            Assert.False(ProjectFilter.MatchesIgnoringActive(ext, c));
            Assert.True(ProjectFilter.MatchesIgnoringActive(intern, c));
        }

        [Fact]
        public void TypeAlleBoth()
        {
            var c = new ProjectFilterCriteria { Type = ProjectTypeFilter.Alle };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = true }, c));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = false }, c));
        }

        [Fact]
        public void CustomerFilterMatchesTrimmedIgnoringCase()
        {
            var item = new Item { Kunde = "ACME" };
            Assert.True(ProjectFilter.MatchesIgnoringActive(item, new ProjectFilterCriteria { Customer = "acme " }));
        }

        [Fact]
        public void AllCustomersMatchesEverything()
        {
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = "X" }, new ProjectFilterCriteria { Customer = ProjectFilter.AllCustomers }));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = null }, new ProjectFilterCriteria { Customer = null }));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = "" }, new ProjectFilterCriteria { Customer = "" }));
        }

        [Fact]
        public void NoCustomerMatchesNullAndWhitespace()
        {
            var c = new ProjectFilterCriteria { Customer = ProjectFilter.NoCustomer };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = null }, c));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = "   " }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = "ACME" }, c));
        }

        [Fact]
        public void CustomerFilterExcludesOthers()
        {
            var c = new ProjectFilterCriteria { Customer = "ACME" };
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = "Beta" }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { Kunde = null }, c));
        }

        [Fact]
        public void RoundingStandardOnlyInherited()
        {
            var c = new ProjectFilterCriteria { Rounding = ProjectRoundingFilter.Standard };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = true }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 15 }, c));
        }

        [Fact]
        public void RoundingEigeneOnlyOwnNonZero()
        {
            var c = new ProjectFilterCriteria { Rounding = ProjectRoundingFilter.Eigene };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 15 }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 0 }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = true, RundungEigenMinuten = 15 }, c));
        }

        [Fact]
        public void RoundingAusOnlyOwnZero()
        {
            var c = new ProjectFilterCriteria { Rounding = ProjectRoundingFilter.Aus };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 0 }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = true, RundungEigenMinuten = 0 }, c));
            // Unbekannter Eigenwert 7 wird wie Aus behandelt (RoundingOptions.Normalize -> 0).
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 7 }, c));
        }

        [Fact]
        public void RoundingAlleMatchesAll()
        {
            var c = new ProjectFilterCriteria { Rounding = ProjectRoundingFilter.Alle };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = true }, c));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 0 }, c));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { RundungGlobal = false, RundungEigenMinuten = 60 }, c));
        }

        [Fact]
        public void OnlyWithBookingsExcludesZero()
        {
            var on = new ProjectFilterCriteria { OnlyWithBookings = true };
            var off = new ProjectFilterCriteria { OnlyWithBookings = false };
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { Buchungen = 0 }, on));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Buchungen = 3 }, on));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Buchungen = 0 }, off));
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { Buchungen = 3 }, off));
        }

        [Fact]
        public void CriteriaAreCombinedWithAnd()
        {
            var c = new ProjectFilterCriteria
            {
                Type = ProjectTypeFilter.Extern,
                Customer = "ACME",
                Rounding = ProjectRoundingFilter.Standard
            };
            Assert.True(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = true, Kunde = "ACME", RundungGlobal = true }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = false, Kunde = "ACME", RundungGlobal = true }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = true, Kunde = "Beta", RundungGlobal = true }, c));
            Assert.False(ProjectFilter.MatchesIgnoringActive(new Item { IstExtern = true, Kunde = "ACME", RundungGlobal = false }, c));
        }

        [Fact]
        public void CountActiveIgnoresSearchAndShowInactive()
        {
            Assert.Equal(0, ProjectFilter.CountActive(new ProjectFilterCriteria { SearchText = "x", ShowInactive = true }));
            Assert.Equal(0, ProjectFilter.CountActive(new ProjectFilterCriteria { Customer = ProjectFilter.AllCustomers }));
            Assert.Equal(0, ProjectFilter.CountActive(new ProjectFilterCriteria { Customer = null }));
            Assert.Equal(0, ProjectFilter.CountActive(new ProjectFilterCriteria { Customer = "" }));
            Assert.Equal(4, ProjectFilter.CountActive(new ProjectFilterCriteria
            {
                Type = ProjectTypeFilter.Intern,
                Customer = "ACME",
                Rounding = ProjectRoundingFilter.Aus,
                OnlyWithBookings = true
            }));
        }

        [Fact]
        public void BuildCustomerListSortedDistinctWithAllFirst()
        {
            var list = ProjectFilter.BuildCustomerList(new[] { "Zeta", "acme", "Acme", " ", "Beta", null });
            Assert.Equal(5, list.Count);   // Alle Kunden, acme (einmal), Beta, Zeta, (ohne Kunde)
            Assert.Equal(ProjectFilter.AllCustomers, list[0]);
            Assert.Equal(1, list.Count(n => string.Equals(n, "acme", System.StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("Beta", list[2]);
            Assert.Equal("Zeta", list[3]);
            Assert.Equal(ProjectFilter.NoCustomer, list[4]);
        }

        [Fact]
        public void BuildCustomerListWithoutEmptyHasNoNoCustomerEntry()
        {
            var list = ProjectFilter.BuildCustomerList(new[] { "Beta", "Alpha" });
            Assert.DoesNotContain(ProjectFilter.NoCustomer, list);
            Assert.Equal(3, list.Count);
        }

        [Fact]
        public void BuildCustomerListNullIsOnlyAll()
        {
            var list = ProjectFilter.BuildCustomerList(null);
            Assert.Single(list);
            Assert.Equal(ProjectFilter.AllCustomers, list[0]);
        }

        [Fact]
        public void CountSplitsShownBaseAndHiddenInactive()
        {
            var items = new List<IProjectFilterItem>
            {
                new Item { IstExtern = true, QuickSelectAktiv = true },
                new Item { IstExtern = true, QuickSelectAktiv = true },
                new Item { IstExtern = false, QuickSelectAktiv = true },
                new Item { IstExtern = true, QuickSelectAktiv = false },
                new Item { IstExtern = true, QuickSelectAktiv = false }
            };

            var n = ProjectFilter.Count(items, new ProjectFilterCriteria { Type = ProjectTypeFilter.Extern, ShowInactive = false });
            Assert.Equal(2, n.Shown);
            Assert.Equal(3, n.Base);
            Assert.Equal(2, n.HiddenInactive);

            n = ProjectFilter.Count(items, new ProjectFilterCriteria { Type = ProjectTypeFilter.Extern, ShowInactive = true });
            Assert.Equal(4, n.Shown);
            Assert.Equal(5, n.Base);
            Assert.Equal(0, n.HiddenInactive);
        }

        [Fact]
        public void StatusTextCases()
        {
            Assert.Null(ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 3, Base = 3, HiddenInactive = 0 }));
            Assert.Equal("2 von 3 Projekten", ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 2, Base = 3, HiddenInactive = 0 }));
            Assert.Equal("0 von 1 Projekt", ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 0, Base = 1, HiddenInactive = 0 }));
            Assert.Equal("1 inaktives Projekt ausgeblendet", ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 3, Base = 3, HiddenInactive = 1 }));
            Assert.Equal("3 inaktive Projekte ausgeblendet", ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 3, Base = 3, HiddenInactive = 3 }));
            Assert.Equal("2 von 3 Projekten · 3 inaktive Projekte ausgeblendet", ProjectFilter.StatusText(new ProjectFilterCounts { Shown = 2, Base = 3, HiddenInactive = 3 }));
        }

        [Fact]
        public void SortPropertyNameMapsChoices()
        {
            Assert.Null(ProjectSort.PropertyName(ProjectSortChoice.Standard));
            Assert.Equal("Projektname", ProjectSort.PropertyName(ProjectSortChoice.Name));
            Assert.Equal("Kunde", ProjectSort.PropertyName(ProjectSortChoice.Kunde));
            Assert.Equal("Kostentraeger", ProjectSort.PropertyName(ProjectSortChoice.Kostentraeger));
            Assert.Equal("TypText", ProjectSort.PropertyName(ProjectSortChoice.Typ));
            Assert.Equal("Buchungen", ProjectSort.PropertyName(ProjectSortChoice.Buchungen));
            Assert.Equal("StundenAnzeige", ProjectSort.PropertyName(ProjectSortChoice.Stunden));
            Assert.Equal("RundungMinuten", ProjectSort.PropertyName(ProjectSortChoice.Rundung));
            Assert.Equal("QuickSelectAktiv", ProjectSort.PropertyName(ProjectSortChoice.Aktiv));
        }

        [Fact]
        public void SortChoiceFromPropertyIsInverseOfPropertyName()
        {
            foreach (var option in ProjectSort.Choices)
                Assert.Equal(option.Value, ProjectSort.ChoiceFromProperty(ProjectSort.PropertyName(option.Value)));
            Assert.Equal(ProjectSortChoice.Standard, ProjectSort.ChoiceFromProperty(null));
            Assert.Equal(ProjectSortChoice.Standard, ProjectSort.ChoiceFromProperty("Unbekannt"));
        }

        [Fact]
        public void SortDirectionMapsBothWays()
        {
            Assert.Equal(System.ComponentModel.ListSortDirection.Descending, ProjectSort.Direction(true));
            Assert.Equal(System.ComponentModel.ListSortDirection.Ascending, ProjectSort.Direction(false));
            Assert.True(ProjectSort.IsDescending(System.ComponentModel.ListSortDirection.Descending));
            Assert.False(ProjectSort.IsDescending(System.ComponentModel.ListSortDirection.Ascending));
        }

        [Fact]
        public void SortChoicesCoverEveryEnumValueOnce()
        {
            var values = ProjectSort.Choices.Select(c => c.Value).ToList();
            Assert.Equal(9, values.Count);
            Assert.Equal(System.Enum.GetValues(typeof(ProjectSortChoice)).Length, values.Count);
            Assert.Equal(values.Distinct().Count(), values.Count);
        }
    }
}
