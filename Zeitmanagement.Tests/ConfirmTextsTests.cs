// Target: .NET Framework + C# 7.3
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class ConfirmTextsTests
    {
        [Theory]
        [InlineData(0, "Das Projekt hat keine Buchungen.")]
        [InlineData(1, "Die 1 Buchung des Projekts")]
        [InlineData(3, "Die 3 Buchungen des Projekts")]
        public void MoveProjectText_names_booking_count(int count, string expectedFragment)
        {
            string text = ConfirmTexts.MoveProjectText("Alpha", count);

            Assert.Contains("„Alpha“", text);
            Assert.Contains(expectedFragment, text);
        }

        [Fact]
        public void PurgeProjectText_warns_about_irreversibility()
        {
            string text = ConfirmTexts.PurgeProjectText("Alpha", 2);

            Assert.Contains("„Alpha“", text);
            Assert.Contains("2 Buchungen", text);
            Assert.Contains("nicht rückgängig", text);
        }

        [Fact]
        public void EmptyTrashText_contains_both_counts()
        {
            string text = ConfirmTexts.EmptyTrashText(4, 7);

            Assert.Contains("4 Projekt(e)", text);
            Assert.Contains("7 einzelne Buchung(en)", text);
        }
    }
}
