// Target: .NET Framework + C# 7.3
using System;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Projektseite: gerundete Stunden je Projekt (Summe über die einzelnen, aufgerundeten Buchungen).</summary>
    public class ProjectsHoursTests
    {
        [Fact]
        public void EmptyListIsZero()
        {
            Assert.Equal(0.0, ProjectHours.RoundedHours(Enumerable.Empty<int>(), 15));
            Assert.Equal(0.0, ProjectHours.RoundedHours(null, 15));
        }

        [Fact]
        public void RoundingOffKeepsNetSum()
        {
            Assert.Equal(90.0 / 60.0, ProjectHours.RoundedHours(new[] { 30, 60 }, 0), 10);
            Assert.Equal(7.0 / 60.0, ProjectHours.RoundedHours(new[] { 7 }, 0), 10);
        }

        [Fact]
        public void SingleEntryIsRoundedUpToUnit()
        {
            Assert.Equal(5.0 / 60.0, ProjectHours.RoundedHours(new[] { 5 }, 5), 10);
            Assert.Equal(15.0 / 60.0, ProjectHours.RoundedHours(new[] { 11 }, 5), 10);
            Assert.Equal(15.0 / 60.0, ProjectHours.RoundedHours(new[] { 10 }, 15), 10);
            Assert.Equal(1.0, ProjectHours.RoundedHours(new[] { 1 }, 60), 10);
        }

        [Fact]
        public void ExactMultipleStaysUnchanged()
        {
            Assert.Equal(60.0 / 60.0, ProjectHours.RoundedHours(new[] { 60 }, 15), 10);
            Assert.Equal(30.0 / 60.0, ProjectHours.RoundedHours(new[] { 30 }, 15), 10);
        }

        [Fact]
        public void EachEntryIsRoundedSeparately()
        {
            // 3 Buchungen à 10 Minuten: einzeln auf 15 gerundet = 45 Minuten, nicht 30 (Summe 30 auf 30 gerundet).
            Assert.Equal(45.0 / 60.0, ProjectHours.RoundedHours(new[] { 10, 10, 10 }, 15), 10);
            // Gemischte Buchungen: 20 -> 30, 5 -> 15, 45 -> 45 (bei 15), Summe 90 Minuten.
            Assert.Equal(90.0 / 60.0, ProjectHours.RoundedHours(new[] { 20, 5, 45 }, 15), 10);
        }

        [Fact]
        public void RoundingDependsOnUnit()
        {
            // 7 -> 10 und 22 -> 25 bei 5 Minuten (35 Minuten); 7 -> 15 und 22 -> 30 bei 15 Minuten (45 Minuten).
            Assert.Equal(35.0 / 60.0, ProjectHours.RoundedHours(new[] { 7, 22 }, 5), 10);
            Assert.Equal(45.0 / 60.0, ProjectHours.RoundedHours(new[] { 7, 22 }, 15), 10);
        }

        [Fact]
        public void UnknownRoundingValueActsLikeOff()
        {
            Assert.Equal(7.0 / 60.0, ProjectHours.RoundedHours(new[] { 7 }, 7), 10);
        }

        [Fact]
        public void ToMinutesRoundsToWholeMinutes()
        {
            Assert.Equal(90, ProjectHours.ToMinutes(TimeSpan.FromMinutes(90)));
            Assert.Equal(1, ProjectHours.ToMinutes(TimeSpan.FromSeconds(89)));
            Assert.Equal(0, ProjectHours.ToMinutes(TimeSpan.FromSeconds(10)));
        }
    }
}
