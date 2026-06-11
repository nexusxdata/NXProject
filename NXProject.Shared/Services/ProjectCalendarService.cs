using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NXProject.Models;

namespace NXProject.Services
{
    public static class ProjectCalendarService
    {
        public const string FileName = "nxproject_calender.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static ProjectCalendar Current { get; private set; } = new();

        public static string GetCalendarPath(string storageKey = "NXProject.Community")
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                storageKey);
            return Path.Combine(dir, FileName);
        }

        public static ProjectCalendar Load(string storageKey = "NXProject.Community")
        {
            var path = GetCalendarPath(storageKey);
            try
            {
                if (!File.Exists(path))
                {
                    Current = new ProjectCalendar();
                    Save(Current, storageKey);
                    return Current;
                }

                var json = File.ReadAllText(path);
                Current = Normalize(JsonSerializer.Deserialize<ProjectCalendar>(json));
            }
            catch
            {
                Current = new ProjectCalendar();
            }

            return Current;
        }

        public static void Save(ProjectCalendar calendar, string storageKey = "NXProject.Community")
        {
            Current = Normalize(calendar);
            var path = GetCalendarPath(storageKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOptions));
        }

        public static bool IsWorkingDay(DateTime date) => IsWorkingDay(date, Current);

        public static double WorkingHoursPerDay =>
            Current.WorkingHoursPerDay <= 0 ? 8.0 : Current.WorkingHoursPerDay;

        public static bool IsWorkingDay(DateTime date, ProjectCalendar? calendar)
        {
            calendar ??= Current;
            var day = date.Date;
            if (calendar.Holidays.Any(h => h.Date.Date == day))
                return false;

            return day.DayOfWeek switch
            {
                DayOfWeek.Saturday => calendar.TreatSaturdayAsWorkday,
                DayOfWeek.Sunday => calendar.TreatSundayAsWorkday,
                _ => true
            };
        }

        public static DateTime AddWorkingDays(DateTime start, int days) => AddWorkingDays(start, days, Current);

        public static DateTime AddWorkingDays(DateTime start, int days, ProjectCalendar? calendar)
        {
            if (days <= 0)
                return start.Date;

            var date = start.Date;
            var added = 0;
            while (added < days)
            {
                date = date.AddDays(1);
                if (IsWorkingDay(date, calendar))
                    added++;
            }

            return date;
        }

        public static int CountWorkingDays(DateTime start, DateTime finish) => CountWorkingDays(start, finish, Current);

        public static int CountWorkingDays(DateTime start, DateTime finish, ProjectCalendar? calendar)
        {
            var from = start.Date;
            var to = finish.Date;
            if (to <= from)
                return 0;

            var days = 0;
            for (var date = from.AddDays(1); date <= to; date = date.AddDays(1))
                if (IsWorkingDay(date, calendar))
                    days++;

            return days;
        }

        private static ProjectCalendar Normalize(ProjectCalendar? calendar)
        {
            calendar ??= new ProjectCalendar();
            var normalized = new ProjectCalendar
            {
                WorkingHoursPerDay = calendar.WorkingHoursPerDay <= 0 ? 8.0 : calendar.WorkingHoursPerDay,
                TreatSaturdayAsWorkday = calendar.TreatSaturdayAsWorkday,
                TreatSundayAsWorkday = calendar.TreatSundayAsWorkday
            };

            foreach (var holiday in calendar.Holidays
                         .Where(h => h.Date != default)
                         .GroupBy(h => h.Date.Date)
                         .Select(g => g.First())
                         .OrderBy(h => h.Date))
            {
                normalized.Holidays.Add(new ProjectHoliday
                {
                    Date = holiday.Date.Date,
                    Name = holiday.Name?.Trim() ?? string.Empty
                });
            }

            return normalized;
        }
    }
}
