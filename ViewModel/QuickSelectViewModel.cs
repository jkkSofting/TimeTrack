using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zeitmanagement.ViewModel
{
    internal class QuickSelectViewModel:BaseViewModel
    {
        public ObservableCollection<QuickSelectItemViewModel> QuickSelectItems { get; set; } = new ObservableCollection<QuickSelectItemViewModel>();
        TimeTracker db;
        public QuickSelectViewModel()
        {
            db = MainViewModel.DbInstance;

            QuickSelectItems.Clear();
            for (int i = 0; i < 10; i++)
            {
                var item = new QuickSelectItemViewModel(SetBookingInformation, SaveProjectnames);

                var availableProjects = db.GetProjects();

                foreach (var project in availableProjects)
                {
                    item.AvailableProjects.Add(project.Projektname);
                }

                QuickSelectItems.Add(item);

            }

            LoadProjectnames();
        }

        public override void Refresh()
        {
            
        }

        private void SetBookingInformation(string selectedProject)
        {
            var calledProject = QuickSelectItems.FirstOrDefault(q => q.SelectedProject != null && q.SelectedProject.Equals(selectedProject));

            if (calledProject == null)
            {
                return;
            }

            var lastActiveProject = QuickSelectItems.LastOrDefault(q => q.IsActive && !q.SelectedProject.Equals(selectedProject));

            if (lastActiveProject != null)
            {
                //Do stuff to end last active project
                lastActiveProject.End = DateTime.Now.ToString("HH:mm");
                lastActiveProject.IsActive = false;

                UpdateDatabase(lastActiveProject);

                calledProject.Start = DateTime.Now.ToString("HH:mm");
                calledProject.IsActive = true;
            }
            else
            {
                if (calledProject.IsActive)
                {
                    calledProject.End = DateTime.Now.ToString("HH:mm");
                    calledProject.IsActive = false;

                    UpdateDatabase(calledProject);
                }
                else
                {
                    calledProject.Start = DateTime.Now.ToString("HH:mm");
                    calledProject.IsActive = true;
                }
            }
        }

        private void UpdateDatabase(QuickSelectItemViewModel selectedItem)
        {
            MainViewModel.DbInstance.AddTimeEntry(DateTime.Today, selectedItem.Start, selectedItem.End, selectedItem.SelectedProject, "Quick Select");
        }

        private void SaveProjectnames()
        {
            // Save the currently selected project names for each QuickSelectItemViewModel
            // We'll use Application Settings for persistence (Properties.Settings.Default)
            // Store as a semicolon-separated string

            var selectedProjects = QuickSelectItems
                .Select(item => item.SelectedProject ?? string.Empty)
                .ToArray();

            string serialized = string.Join(";", selectedProjects);

            // Save to settings (ensure you have a string property named QuickSelectProjects in your settings)
            Properties.Settings.Default.QuickSelectProjects = serialized;
            Properties.Settings.Default.Save();
        }

        public void LoadProjectnames()
        {
            // Load the saved project names and assign them to the QuickSelectItems
            string serialized = Properties.Settings.Default.QuickSelectProjects;
            if (string.IsNullOrEmpty(serialized))
                return;

            var selectedProjects = serialized.Split(';');
            for (int i = 0; i < QuickSelectItems.Count && i < selectedProjects.Length; i++)
            {
                QuickSelectItems[i].SelectedProject = selectedProjects[i];
            }
        }
    }
}
