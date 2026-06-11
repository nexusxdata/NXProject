using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Controls
{
    public partial class CalendarSettingsControl : UserControl, INotifyPropertyChanged
    {
        private readonly string _storageKey;

        public CalendarSettingsControl(string storageKey = "NXProject.Community")
        {
            InitializeComponent();
            _storageKey = storageKey;
            Calendar = ProjectCalendarService.Load(storageKey);
            CalendarPath = ProjectCalendarService.GetCalendarPath(storageKey);
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProjectCalendar Calendar { get; }
        public string CalendarPath { get; }

        public event EventHandler? Saved;

        private void OnAddTodayClick(object sender, RoutedEventArgs e)
        {
            Calendar.Holidays.Add(new ProjectHoliday
            {
                Date = DateTime.Today,
                Name = "Feriado"
            });
            OnPropertyChanged(nameof(Calendar));
        }

        private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
        {
            var grid = FindHolidayGrid(this);
            if (grid?.SelectedItem is ProjectHoliday holiday)
                Calendar.Holidays.Remove(holiday);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            ProjectCalendarService.Save(Calendar, _storageKey);
            Saved?.Invoke(this, EventArgs.Empty);
        }

        private static DataGrid? FindHolidayGrid(DependencyObject root)
        {
            if (root is DataGrid grid)
                return grid;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindHolidayGrid(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
