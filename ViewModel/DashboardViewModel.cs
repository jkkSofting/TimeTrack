using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zeitmanagement.ViewModel
{
    internal class DashboardViewModel:BaseViewModel
    {
        private string _projectsCounter;

        public string ProjectsCounter
        {
            get => _projectsCounter;
            set => SetProperty(ref _projectsCounter, value);
        }

        private string _bookingsCounter;

        public string BookingsCounter
        {
            get => _bookingsCounter;
            set => SetProperty(ref _bookingsCounter, value);
        }

        private string _hoursCounter;

        public string HoursCounter
        {
            get => _hoursCounter;
            set => SetProperty(ref _hoursCounter, value);
        }

        public DashboardViewModel()
        {
            //MainViewModel.DbInstance.AddOrEnsureProject("ODX-Stripper", "Softing", "KT-4711");
            //MainViewModel.DbInstance.AddTimeEntry(new DateTime(2025, 8, 26), "09:00", "12:30", "ODX-Stripper", "Wutanfall in produktiv");
            //MainViewModel.DbInstance.AddTimeEntry(new DateTime(2025, 8, 26), "13:30", "15:00", "ODX-Stripper", "Trigger verflucht");
        }

        public override void Refresh()
        {
            var projects = MainViewModel.DbInstance.GetProjects();

            double sum = 0;
            int entryCounter = 0;


            foreach (var project in projects)
            {
                var entries = MainViewModel.DbInstance.GetTimeEntriesForProject(project.Projektname);
                double localSum = 0;

                foreach (var entry in entries)
                {
                    entryCounter++;
                    var format = "HH:mm";
                    var culture = CultureInfo.InvariantCulture;

                    DateTime startTime = DateTime.ParseExact(entry.Startzeit, format, culture);
                    DateTime endTime = DateTime.ParseExact(entry.Endzeit, format, culture);

                    TimeSpan diff = endTime - startTime;

                    localSum += diff.TotalHours;
                }

                sum += localSum;
            }

            ProjectsCounter = projects.Count().ToString();
            BookingsCounter = entryCounter.ToString();
            HoursCounter = sum.ToString("0");
            
        }
    }
}
