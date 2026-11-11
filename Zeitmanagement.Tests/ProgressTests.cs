// Target: .NET Framework + C# 7.3
using System;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    /// <summary>Stand in Prozent und Fortschrittsbalken je Zeile der Übersicht.</summary>
    public class ProgressTests
    {
        private static ControllingOverviewRow Row(decimal? plan, decimal? actual)
        {
            return new ControllingOverviewRow
            {
                Observation = new ControllingObservation
                {
                    CarrierKey = "P", CarrierText = "P", SourceTimestamp = new DateTime(2026, 11, 9), PlannedCost = plan, ActualCost = actual
                }
            };
        }

        [Fact]
        public void Progress_is_the_consumption_as_percent_and_bar_fraction()
        {
            var row = Row(3000m, 1260m);

            Assert.True(row.HasConsumption);
            Assert.Equal("42,0 %", row.ConsumptionPercentText);
            Assert.Equal(0.42, row.ProgressValue, 6);
            Assert.False(row.IsNearLimit);
            Assert.False(row.IsOverrun);
        }

        [Fact]
        public void An_overrun_fills_the_bar_but_the_text_keeps_the_real_value()
        {
            var row = Row(10000m, 12000m);

            Assert.Equal("120,0 %", row.ConsumptionPercentText);
            Assert.Equal(1.0, row.ProgressValue);
            Assert.True(row.IsOverrun);
            Assert.False(row.IsNearLimit);
        }

        [Fact]
        public void Near_the_limit_warns_from_90_percent_up_to_the_plan()
        {
            Assert.True(Row(1000m, 900m).IsNearLimit);
            Assert.True(Row(1000m, 1000m).IsNearLimit);     // genau 100 % ist noch im Budget
            Assert.False(Row(1000m, 899m).IsNearLimit);
            Assert.False(Row(1000m, 1001m).IsNearLimit);    // das ist eine Überschreitung
        }

        [Theory]
        [InlineData(null, 10)]       // Plan nicht geliefert
        [InlineData(0, 10)]          // kein positives Budget
        [InlineData(-5, 10)]
        public void Without_a_positive_plan_there_is_no_percentage_and_an_empty_bar_with_a_reason(int? plan, int actual)
        {
            var row = Row(plan.HasValue ? (decimal?)plan.Value : null, actual);

            Assert.False(row.HasConsumption);
            Assert.Equal("–", row.ConsumptionPercentText);
            Assert.Equal(0.0, row.ProgressValue);
            Assert.Equal(CostMath.NoBudgetText, row.ConsumptionText);   // die Begründung bleibt als Text sichtbar
        }

        [Fact]
        public void A_known_plan_with_missing_actual_says_not_delivered_instead_of_zero_percent()
        {
            var row = Row(1000m, null);

            Assert.False(row.HasConsumption);
            Assert.Equal(0.0, row.ProgressValue);
            Assert.Equal(CostMath.MissingText, row.ConsumptionText);
        }

        [Fact]
        public void A_credit_never_gives_a_negative_bar()
        {
            var row = Row(100m, -10m);

            Assert.Equal("-10,0 %", row.ConsumptionPercentText);   // der Text zeigt den echten Wert
            Assert.Equal(0.0, row.ProgressValue);
        }
    }
}
