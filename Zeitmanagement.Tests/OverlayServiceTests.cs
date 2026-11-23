// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using Xunit;
using Zeitmanagement.MVVM;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Tests
{
    /// <summary>Tests für das Overlay-Grundgerüst (OverlayService und OverlayViewModelBase).</summary>
    // Alle Tests, die den statischen OverlayService.Instance benutzen, laufen nacheinander (gleiche Collection).
    [Collection("OverlayService")]
    public class OverlayServiceTests : IDisposable
    {
        private sealed class TestOverlay : OverlayViewModelBase
        {
            public int Saved;
            public int Cancelled;
            public bool SaveAllowed = true;
            public bool CloseOnSave;
            public bool CloseOnCancel = true;

            public TestOverlay(string title = "Test")
                : base(title)
            {
            }

            protected override bool CanSave() => SaveAllowed;

            protected override void OnSave()
            {
                Saved++;
                if (CloseOnSave)
                    Close();
            }

            protected override void OnCancel()
            {
                Cancelled++;
                if (CloseOnCancel)
                    base.OnCancel();
            }

            public void ChangeCanSave(bool allowed)
            {
                SaveAllowed = allowed;
                RaiseCanSaveChanged();
            }

            public void SetDanger(DelegateCommand command) => DangerCommand = command;
        }

        public OverlayServiceTests()
        {
            OverlayService.Instance.Close();
        }

        public void Dispose()
        {
            OverlayService.Instance.Close();
        }

        private static OverlayService Service => OverlayService.Instance;

        [Fact]
        public void Initially_nothing_is_open()
        {
            Assert.Null(Service.Current);
            Assert.False(Service.IsOpen);
        }

        [Fact]
        public void Show_sets_Current_and_IsOpen()
        {
            var vm = new TestOverlay();

            Service.Show(vm);

            Assert.Same(vm, Service.Current);
            Assert.True(Service.IsOpen);
        }

        [Fact]
        public void Show_null_throws()
        {
            Assert.Throws<ArgumentNullException>(() => Service.Show(null));
        }

        [Fact]
        public void Show_replaces_an_open_overlay()
        {
            var first = new TestOverlay("A");
            var second = new TestOverlay("B");
            Service.Show(first);

            Service.Show(second);

            Assert.Same(second, Service.Current);
            Assert.True(Service.IsOpen);
        }

        [Fact]
        public void Close_of_a_foreign_viewmodel_keeps_the_overlay_open()
        {
            var shown = new TestOverlay("A");
            var other = new TestOverlay("B");
            Service.Show(shown);

            Service.Close(other);

            Assert.Same(shown, Service.Current);
            Assert.True(Service.IsOpen);
        }

        [Fact]
        public void Close_of_the_current_viewmodel_closes()
        {
            var vm = new TestOverlay();
            Service.Show(vm);

            Service.Close(vm);

            Assert.Null(Service.Current);
            Assert.False(Service.IsOpen);
        }

        [Fact]
        public void Close_without_argument_closes_whatever_is_open()
        {
            Service.Show(new TestOverlay());

            Service.Close();

            Assert.False(Service.IsOpen);
        }

        [Fact]
        public void Close_without_overlay_is_harmless_and_raises_nothing()
        {
            var closed = 0;
            EventHandler handler = (s, e) => closed++;
            Service.Closed += handler;
            try
            {
                Service.Close();
                Service.Close(new TestOverlay());
            }
            finally
            {
                Service.Closed -= handler;
            }

            Assert.Equal(0, closed);
        }

        [Fact]
        public void Close_raises_Closed_once_but_replacing_does_not()
        {
            var closed = 0;
            EventHandler handler = (s, e) => closed++;
            Service.Closed += handler;
            try
            {
                Service.Show(new TestOverlay("A"));
                Service.Show(new TestOverlay("B"));
                Assert.Equal(0, closed);

                Service.Close();
                Assert.Equal(1, closed);
            }
            finally
            {
                Service.Closed -= handler;
            }
        }

        [Fact]
        public void PropertyChanged_is_raised_for_Current_and_IsOpen()
        {
            var raised = new List<string>();
            System.ComponentModel.PropertyChangedEventHandler handler = (s, e) => raised.Add(e.PropertyName);
            Service.PropertyChanged += handler;
            try
            {
                var first = new TestOverlay("A");
                Service.Show(first);
                Assert.Contains(nameof(OverlayService.Current), raised);
                Assert.Contains(nameof(OverlayService.IsOpen), raised);

                // Ersetzen: Current ändert sich, IsOpen bleibt true und meldet nichts.
                raised.Clear();
                Service.Show(new TestOverlay("B"));
                Assert.Contains(nameof(OverlayService.Current), raised);
                Assert.DoesNotContain(nameof(OverlayService.IsOpen), raised);

                raised.Clear();
                Service.Close();
                Assert.Contains(nameof(OverlayService.Current), raised);
                Assert.Contains(nameof(OverlayService.IsOpen), raised);
            }
            finally
            {
                Service.PropertyChanged -= handler;
            }
        }

        [Fact]
        public void SaveCommand_calls_OnSave_and_does_not_close_by_itself()
        {
            var vm = new TestOverlay();
            Service.Show(vm);

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, vm.Saved);
            Assert.True(Service.IsOpen);
        }

        [Fact]
        public void OnSave_can_close_the_overlay_after_success()
        {
            var vm = new TestOverlay { CloseOnSave = true };
            Service.Show(vm);

            vm.SaveCommand.Execute(null);

            Assert.Equal(1, vm.Saved);
            Assert.False(Service.IsOpen);
        }

        [Fact]
        public void CanSave_controls_CanExecute_of_SaveCommand()
        {
            var vm = new TestOverlay();
            Assert.True(vm.SaveCommand.CanExecute(null));

            vm.ChangeCanSave(false);
            Assert.False(vm.SaveCommand.CanExecute(null));

            vm.ChangeCanSave(true);
            Assert.True(vm.SaveCommand.CanExecute(null));
        }

        [Fact]
        public void CancelCommand_closes_by_default()
        {
            var vm = new TestOverlay();
            Service.Show(vm);

            vm.CancelCommand.Execute(null);

            Assert.Equal(1, vm.Cancelled);
            Assert.False(Service.IsOpen);
        }

        [Fact]
        public void OnCancel_can_be_overridden_to_stay_open()
        {
            var vm = new TestOverlay { CloseOnCancel = false };
            Service.Show(vm);

            vm.CancelCommand.Execute(null);

            Assert.Equal(1, vm.Cancelled);
            Assert.True(Service.IsOpen);
        }

        [Fact]
        public void Close_of_a_replaced_overlay_does_not_close_the_new_one()
        {
            var old = new TestOverlay("A");
            var current = new TestOverlay("B");
            Service.Show(old);
            Service.Show(current);

            old.Close();

            Assert.Same(current, Service.Current);
        }

        [Fact]
        public void Defaults_of_texts_and_DangerCommand()
        {
            var vm = new TestOverlay("Titel");

            Assert.Equal("Titel", vm.Title);
            Assert.Equal("Speichern", vm.SaveText);
            Assert.Equal("Abbrechen", vm.CancelText);
            Assert.Null(vm.DangerCommand);
        }

        [Fact]
        public void DangerCommand_can_be_set_and_notifies()
        {
            var vm = new TestOverlay();
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            var danger = new DelegateCommand(_ => { });

            vm.SetDanger(danger);

            Assert.Same(danger, vm.DangerCommand);
            Assert.Contains(nameof(OverlayViewModelBase.DangerCommand), raised);
        }
    }
}
