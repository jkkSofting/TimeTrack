// Target: .NET Framework + C# 7.3
using System;
using System.Linq;
using Xunit;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Tests
{
    /// <summary>Tests für die Overlays „Projekt anlegen/bearbeiten“ und „Standardrundung“ (reine VM-Logik mit Test-DB).</summary>
    [Collection("OverlayService")]
    public class ProjectEditOverlayTests : IDisposable
    {
        private readonly TestDb _t = new TestDb();

        public void Dispose()
        {
            OverlayService.Instance.Close();
            _t.Dispose();
        }

        private TimeTracker.ProjectRow Row(string name) => _t.Db.GetProjects().Single(p => p.Projektname == name);

        private static void Fill(ProjectEditOverlayViewModel vm, string name = "Alpha", string kunde = "Kunde", string kt = "KT-1")
        {
            vm.Projektname = name;
            vm.Kunde = kunde;
            vm.Kostentraeger = kt;
        }

        [Fact]
        public void New_titles_and_defaults()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);

            Assert.Equal("Neues Projekt", vm.Title);
            Assert.Equal("Hinzufügen", vm.SaveText);
            Assert.True(vm.IsRoundingStandard);
            Assert.Equal(15, vm.OwnMinutes);
            Assert.Equal("Standard verwenden (aktuell: 15 Minuten)", vm.StandardOptionText);
        }

        [Fact]
        public void Standard_option_text_follows_global_rounding()
        {
            _t.Db.SetGlobalRoundingMinutes(0);
            Assert.Equal("Standard verwenden (aktuell: Aus)", ProjectEditOverlayViewModel.ForNew(_t.Db).StandardOptionText);

            _t.Db.SetGlobalRoundingMinutes(60);
            Assert.Equal("Standard verwenden (aktuell: 60 Minuten)", ProjectEditOverlayViewModel.ForNew(_t.Db).StandardOptionText);
        }

        [Fact]
        public void CanSave_requires_name_customer_and_carrier()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Assert.False(vm.SaveCommand.CanExecute(null));

            vm.Projektname = "Alpha";
            Assert.False(vm.SaveCommand.CanExecute(null));
            vm.Kunde = "Kunde";
            Assert.False(vm.SaveCommand.CanExecute(null));
            vm.Kostentraeger = "   ";
            Assert.False(vm.SaveCommand.CanExecute(null));
            vm.Kostentraeger = "KT-1";
            Assert.True(vm.SaveCommand.CanExecute(null));

            vm.Kunde = "";
            Assert.False(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void New_with_standard_inherits_global_rounding()
        {
            _t.Db.SetGlobalRoundingMinutes(10);
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm);
            vm.IstExtern = true;
            OverlayService.Instance.Show(vm);

            vm.SaveCommand.Execute(null);

            var row = Row("Alpha");
            Assert.True(row.RundungGlobal);
            Assert.Equal(10, row.RundungMinuten);
            Assert.True(row.IstExtern);
            Assert.Equal("Kunde", row.Kunde);
            Assert.Equal("KT-1", row.Kostentraeger);
            Assert.Null(OverlayService.Instance.Current);
        }

        [Fact]
        public void New_with_own_value_is_saved_as_own()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm);
            vm.IsRoundingOwn = true;
            vm.OwnMinutes = 5;

            vm.SaveCommand.Execute(null);

            var row = Row("Alpha");
            Assert.False(row.RundungGlobal);
            Assert.Equal(5, row.RundungEigenMinuten);
            Assert.Equal(5, row.RundungMinuten);
        }

        [Fact]
        public void New_with_off_disables_rounding_for_project_only()
        {
            _t.Db.SetGlobalRoundingMinutes(15);
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm);
            vm.IsRoundingOff = true;

            vm.SaveCommand.Execute(null);

            var row = Row("Alpha");
            Assert.False(row.RundungGlobal);
            Assert.Equal(0, row.RundungEigenMinuten);
            Assert.Equal(0, row.RundungMinuten);
        }

        [Fact]
        public void Radio_flags_are_mutually_exclusive()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);

            vm.IsRoundingOwn = true;
            Assert.True(vm.IsRoundingOwn);
            Assert.False(vm.IsRoundingStandard);
            Assert.False(vm.IsRoundingOff);

            vm.IsRoundingStandard = false; // Abwählen ändert nichts
            Assert.True(vm.IsRoundingOwn);

            vm.IsRoundingOff = true;
            Assert.False(vm.IsRoundingOwn);
            Assert.True(vm.IsRoundingOff);
        }

        [Fact]
        public void New_trims_values()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm, "  Alpha ", " Kunde ", " KT-1 ");

            vm.SaveCommand.Execute(null);

            var row = Row("Alpha");
            Assert.Equal("Kunde", row.Kunde);
            Assert.Equal("KT-1", row.Kostentraeger);
        }

        [Fact]
        public void New_with_existing_name_shows_error_and_keeps_overlay_and_original()
        {
            _t.Db.AddOrEnsureProject("Alpha", "Original", "KT-0");
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm, "Alpha", "Neu", "KT-9");
            OverlayService.Instance.Show(vm);

            vm.SaveCommand.Execute(null);

            Assert.True(vm.HasError);
            Assert.Same(vm, OverlayService.Instance.Current);
            Assert.Equal("Original", Row("Alpha").Kunde);
        }

        [Fact]
        public void Edit_takes_initial_state_from_project()
        {
            var inherit = ProjectEditOverlayViewModel.ForEdit(_t.Db, "Alpha", "K", "KT", true, true, 0);
            Assert.Equal("Projekt bearbeiten", inherit.Title);
            Assert.Equal("Speichern", inherit.SaveText);
            Assert.Equal("Alpha", inherit.Projektname);
            Assert.Equal("K", inherit.Kunde);
            Assert.Equal("KT", inherit.Kostentraeger);
            Assert.True(inherit.IstExtern);
            Assert.True(inherit.IsRoundingStandard);
            Assert.True(inherit.SaveCommand.CanExecute(null));

            var own = ProjectEditOverlayViewModel.ForEdit(_t.Db, "Alpha", "K", "KT", false, false, 60);
            Assert.True(own.IsRoundingOwn);
            Assert.Equal(60, own.OwnMinutes);

            var off = ProjectEditOverlayViewModel.ForEdit(_t.Db, "Alpha", "K", "KT", false, false, 0);
            Assert.True(off.IsRoundingOff);
            Assert.Equal(15, off.OwnMinutes); // Vorgabe beim Wechsel auf „Eigene Rundung“
        }

        [Fact]
        public void Edit_saves_changes_renames_and_calls_back()
        {
            _t.Db.AddOrEnsureProject("Alpha", "K", "KT", rundungMinuten: 0, rundungGlobal: true);
            var row = Row("Alpha");
            int callbacks = 0;
            var vm = ProjectEditOverlayViewModel.ForEdit(_t.Db, row.Projektname, row.Kunde, row.Kostentraeger,
                row.IstExtern, row.RundungGlobal, row.RundungEigenMinuten, () => callbacks++);
            OverlayService.Instance.Show(vm);

            vm.Projektname = "Beta";
            vm.IsRoundingOwn = true;
            vm.OwnMinutes = 10;
            vm.SaveCommand.Execute(null);

            Assert.DoesNotContain(_t.Db.GetProjects(), p => p.Projektname == "Alpha");
            var saved = Row("Beta");
            Assert.False(saved.RundungGlobal);
            Assert.Equal(10, saved.RundungMinuten);
            Assert.Equal(1, callbacks);
            Assert.Null(OverlayService.Instance.Current);
        }

        [Fact]
        public void Edit_back_to_standard_sets_inherit_flag()
        {
            _t.Db.AddOrEnsureProject("Alpha", "K", "KT", rundungMinuten: 60, rundungGlobal: false);
            _t.Db.SetGlobalRoundingMinutes(5);
            var vm = ProjectEditOverlayViewModel.ForEdit(_t.Db, "Alpha", "K", "KT", false, false, 60);

            vm.IsRoundingStandard = true;
            vm.SaveCommand.Execute(null);

            var row = Row("Alpha");
            Assert.True(row.RundungGlobal);
            Assert.Equal(5, row.RundungMinuten);
        }

        [Fact]
        public void Edit_rename_to_duplicate_shows_error_and_does_not_close()
        {
            _t.Db.AddOrEnsureProject("Alpha", "K", "KT");
            _t.Db.AddOrEnsureProject("Beta", "K", "KT");
            int callbacks = 0;
            var vm = ProjectEditOverlayViewModel.ForEdit(_t.Db, "Beta", "K", "KT", false, true, 0, () => callbacks++);
            OverlayService.Instance.Show(vm);

            vm.Projektname = "Alpha";
            vm.SaveCommand.Execute(null);

            Assert.True(vm.HasError);
            Assert.False(string.IsNullOrWhiteSpace(vm.ErrorText));
            Assert.Same(vm, OverlayService.Instance.Current);
            Assert.Equal(0, callbacks);
            Assert.Equal(2, _t.Db.GetProjects().Count());

            // Weiter tippen räumt den Fehler weg
            vm.Projektname = "Gamma";
            Assert.False(vm.HasError);
        }

        [Fact]
        public void Cancel_closes_without_saving()
        {
            var vm = ProjectEditOverlayViewModel.ForNew(_t.Db);
            Fill(vm);
            OverlayService.Instance.Show(vm);

            vm.CancelCommand.Execute(null);

            Assert.Null(OverlayService.Instance.Current);
            Assert.Empty(_t.Db.GetProjects());
        }

        // ---------- Standardrundung ----------

        [Fact]
        public void Global_overlay_starts_with_current_value_and_lists_all_options()
        {
            _t.Db.SetGlobalRoundingMinutes(10);
            var vm = new GlobalRoundingOverlayViewModel(_t.Db, 3);

            Assert.Equal("Standardrundung", vm.Title);
            Assert.Equal(10, vm.SelectedMinutes);
            Assert.Equal(new[] { 0, 5, 10, 15, 60 }, vm.Choices.Select(c => c.Minutes).ToArray());
        }

        [Fact]
        public void Global_overlay_saves_value_closes_and_calls_back()
        {
            int callbacks = 0;
            var vm = new GlobalRoundingOverlayViewModel(_t.Db, 2, () => callbacks++);
            OverlayService.Instance.Show(vm);

            vm.SelectedMinutes = 60;
            vm.SaveCommand.Execute(null);

            Assert.Equal(60, _t.Db.GetGlobalRoundingMinutes());
            Assert.Equal(1, callbacks);
            Assert.Null(OverlayService.Instance.Current);
        }

        [Fact]
        public void Global_overlay_can_switch_rounding_off()
        {
            var vm = new GlobalRoundingOverlayViewModel(_t.Db, 0);
            vm.SelectedMinutes = 0;
            vm.SaveCommand.Execute(null);

            Assert.Equal(0, _t.Db.GetGlobalRoundingMinutes());
        }

        [Fact]
        public void Global_overlay_ignores_values_that_are_not_allowed()
        {
            var vm = new GlobalRoundingOverlayViewModel(_t.Db, 0);
            vm.SelectedMinutes = 7;

            Assert.Equal(15, vm.SelectedMinutes);
        }

        [Fact]
        public void Global_overlay_describes_inheriting_projects()
        {
            Assert.Contains("kein Projekt", new GlobalRoundingOverlayViewModel(_t.Db, 0).InheritText);
            Assert.Contains("1 Projekt", new GlobalRoundingOverlayViewModel(_t.Db, 1).InheritText);
            Assert.Contains("7 Projekte", new GlobalRoundingOverlayViewModel(_t.Db, 7).InheritText);
        }

        [Fact]
        public void Cancel_of_global_overlay_keeps_old_value()
        {
            var vm = new GlobalRoundingOverlayViewModel(_t.Db, 0);
            OverlayService.Instance.Show(vm);
            vm.SelectedMinutes = 5;

            vm.CancelCommand.Execute(null);

            Assert.Equal(15, _t.Db.GetGlobalRoundingMinutes());
            Assert.Null(OverlayService.Instance.Current);
        }
    }
}
