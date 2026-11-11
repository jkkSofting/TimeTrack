// Target: .NET Framework + C# 7.3
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class CarrierMatchingTests
    {
        private static bool Contains(string localCarrier, string pspId)
        {
            return CarrierKeys.TextContainsKey(CarrierKeys.Normalize(localCarrier), CarrierKeys.Normalize(pspId));
        }

        [Fact]
        public void Normalize_trims_unicode_whitespace_and_ignores_case()
        {
            Assert.Equal("1500410A24RD001.01", CarrierKeys.Normalize("  1500410a24rd001.01  "));
            Assert.Equal("", CarrierKeys.Normalize(null));
            Assert.Equal("", CarrierKeys.Normalize("   "));
        }

        [Fact]
        public void Normalize_uses_unicode_normal_form_c()
        {
            Assert.Equal(CarrierKeys.Normalize("Käse"), CarrierKeys.Normalize("Käse"));
        }

        [Fact]
        public void Normalize_keeps_internal_spaces_and_dots()
        {
            Assert.Equal("A B.C", CarrierKeys.Normalize(" a b.c "));
        }

        [Fact]
        public void Equal_value_matches()
        {
            Assert.True(Contains("1500410A24RD001.01", "1500410A24RD001.01"));
        }

        [Fact]
        public void Local_carrier_only_has_to_contain_the_psp_element()
        {
            Assert.True(Contains("1500410A24RD001.01 Entwicklung Step1", "1500410A24RD001.01"));
            Assert.True(Contains("SAE-1500410A24RD001.01", "1500410A24RD001.01"));
            Assert.True(Contains("Kostenstelle: 1500410A24RD001.01.", "1500410A24RD001.01"));
            Assert.True(Contains("(1500410A24RD001.01)", "1500410A24RD001.01"));
        }

        [Fact]
        public void Whitespace_case_and_nbsp_variants_match()
        {
            Assert.True(Contains("  1500410a24rd001.01 ", "1500410A24RD001.01"));
            Assert.True(Contains(" 1500410A24RD001.01 ", "1500410A24RD001.01"));
        }

        [Fact]
        public void Child_element_does_not_match_its_parent_or_root()
        {
            // "…001.01.01" enthält zwar den Text von Wurzel und "…001.01", ist aber ein anderes (Kind-)PSP-Element.
            Assert.False(Contains("1500410A24RD001.01.01", "1500410A24RD001.01"));
            Assert.False(Contains("1500410A24RD001.01.01", "1500410A24RD001"));
            Assert.True(Contains("1500410A24RD001.01.01", "1500410A24RD001.01.01"));
        }

        [Fact]
        public void Parent_only_carrier_does_not_match_a_child()
        {
            Assert.False(Contains("1500410A24RD001", "1500410A24RD001.01"));
        }

        [Fact]
        public void Missing_dot_extra_space_and_leading_zero_changes_do_not_match()
        {
            Assert.False(Contains("1500410A24RD00101", "1500410A24RD001.01"));
            Assert.False(Contains("1500410A24RD001. 01", "1500410A24RD001.01"));
            Assert.False(Contains("1500410A24RD001.1", "1500410A24RD001.01"));
            Assert.False(Contains("1500410A24RD001.011", "1500410A24RD001.01"));
        }

        [Fact]
        public void Id_inside_a_longer_token_does_not_match()
        {
            Assert.False(Contains("X1500410A24RD001.01", "1500410A24RD001.01"));
            Assert.False(Contains("1500410A24RD001.01X", "1500410A24RD001.01"));
            Assert.False(Contains("ABC.1500410A24RD001.01", "1500410A24RD001.01"));
        }

        [Fact]
        public void Numeric_looking_ids_stay_text()
        {
            Assert.False(Contains("12345", "0012345"));
            Assert.True(Contains("PSP 0012345", "0012345"));
        }

        [Fact]
        public void A_local_carrier_with_several_ids_contains_each_of_them()
        {
            var text = "1500410A24RD001.01 / 1500630C26IC001.02.06";
            Assert.True(Contains(text, "1500410A24RD001.01"));
            Assert.True(Contains(text, "1500630C26IC001.02.06"));
            Assert.False(Contains(text, "1500630C26IC001.02"));
        }

        [Fact]
        public void Allowlist_hash_is_deterministic_and_independent_of_order_case_and_whitespace()
        {
            var a = new CarrierAllowlist(new[] { "1500410A24RD001.01", "1500630C26IC001.02.06" });
            var b = new CarrierAllowlist(new[] { " 1500630c26ic001.02.06", "1500410A24RD001.01 ", "1500410A24RD001.01" });
            var c = new CarrierAllowlist(new[] { "1500410A24RD001.01" });

            Assert.Equal(a.Hash, b.Hash);
            Assert.NotEqual(a.Hash, c.Hash);
            Assert.Equal(2, a.NormalizedCarriers.Count);
        }

        [Fact]
        public void Allowlist_reports_spellings_that_only_normalization_makes_equal()
        {
            var list = new CarrierAllowlist(new[] { "abc.01", "ABC.01 ", "XYZ.02" });
            var ambiguous = list.AmbiguousSpellings().ToList();

            Assert.Single(ambiguous);
            Assert.Equal("ABC.01", ambiguous[0].Key);
            Assert.Equal(2, ambiguous[0].Value.Count);
        }

        [Fact]
        public void Linking_returns_each_project_once_and_independent_of_flags()
        {
            var projects = new[]
            {
                new LocalProjectInfo { Id = 1, Name = "A", Carrier = "1500410A24RD001.01", IsExternal = true },
                new LocalProjectInfo { Id = 2, Name = "B", Carrier = "x 1500410A24RD001.01 y", IsExternal = false },
                new LocalProjectInfo { Id = 3, Name = "C", Carrier = "1500410A24RD001.01.01" }
            };

            var linked = CarrierLinking.LinkedProjects(projects, "1500410A24RD001.01");
            Assert.Equal(new[] { 1, 2 }, linked.Select(p => p.Id).ToArray());
        }
    }
}
