// Target: .NET Framework + C# 7.3
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class CarrierLabelTests
    {
        [Theory]
        [InlineData("BUDS Trinity Development (1500420C26FX001.01.01)", "BUDS Trinity Development")]
        [InlineData("10SOVDITK_SOVD-Server embedded (1500440A24RD001.01)", "10SOVDITK_SOVD-Server embedded")]
        [InlineData("Wartung Kodierdaten HJ2-26 (1500620C26FX011.01)", "Wartung Kodierdaten HJ2-26")]
        [InlineData("  Bezeichnung   ( 15004 )  ", "Bezeichnung")]
        public void Strips_trailing_id_in_parentheses(string input, string expected)
        {
            Assert.Equal(expected, CarrierLabel.StripId(input));
        }

        [Theory]
        [InlineData("Allgemein", "Allgemein")]
        [InlineData("Multiflasher VIII (?)", "Multiflasher VIII (?)")]
        [InlineData("Server (alt)", "Server (alt)")]
        [InlineData("Server (alt) neu", "Server (alt) neu")]
        [InlineData("(1500420C26FX001.01)", "(1500420C26FX001.01)")]
        [InlineData("—", "—")]
        public void Keeps_text_without_id(string input, string expected)
        {
            Assert.Equal(expected, CarrierLabel.StripId(input));
        }

        [Fact]
        public void Null_and_blank_give_empty_or_unchanged()
        {
            Assert.Equal("", CarrierLabel.StripId(null));
            Assert.Equal("  ", CarrierLabel.StripId("  "));
        }

        [Fact]
        public void Only_last_parentheses_are_stripped()
        {
            Assert.Equal("Projekt (Phase 2)", CarrierLabel.StripId("Projekt (Phase 2) (1500420C26)"));
        }

        [Theory]
        [InlineData("BUDS Trinity Development (1500420C26FX001.01.01)", "1500420C26FX001.01.01")]
        [InlineData("Bezeichnung   ( 15004 )  ", "15004")]
        [InlineData("Projekt (Phase 2) (1500420C26)", "1500420C26")]
        public void Extracts_trailing_id(string input, string expected)
        {
            Assert.Equal(expected, CarrierLabel.ExtractId(input));
        }

        [Theory]
        [InlineData("Allgemein")]
        [InlineData("Multiflasher VIII (?)")]
        [InlineData("Server (alt)")]
        [InlineData("(1500420C26FX001.01)")]
        [InlineData("—")]
        [InlineData("  ")]
        [InlineData(null)]
        public void No_id_gives_empty_text(string input)
        {
            Assert.Equal("", CarrierLabel.ExtractId(input));
        }
    }
}
