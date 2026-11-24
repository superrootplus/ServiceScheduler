using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ServiceScheduler
{
    public static class UiSmokeTests
    {
        private static int assertions;

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("ПРОВАЛ: " + message);
            Console.WriteLine("OK: " + message);
        }

        private static T Field<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new Exception("Не найдено поле " + name);
            return field.GetValue(target) as T;
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new Exception("Не найден метод " + name);
            return method.Invoke(target, arguments);
        }

        private static bool HasPaleGreenSelection(Style style, DependencyProperty selectedProperty,
            DependencyProperty backgroundProperty)
        {
            Trigger selected = style.Triggers.OfType<Trigger>().FirstOrDefault(t =>
                t.Property == selectedProperty && Equals(t.Value, true));
            if (selected == null) return false;
            Setter background = selected.Setters.OfType<Setter>().FirstOrDefault(s =>
                s.Property == backgroundProperty);
            SolidColorBrush brush = background == null ? null : background.Value as SolidColorBrush;
            return brush != null && brush.Color == Color.FromRgb(0xDC, 0xFC, 0xE7);
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                Check(TimePickerDialog.HourFromDial(9, 0) == 12 &&
                    TimePickerDialog.HourFromDial(21, 0) == 12 &&
                    TimePickerDialog.HourFromDial(9, 1) == 1 &&
                    TimePickerDialog.HourFromDial(21, 1) == 13,
                    "деление 12 выбирает 12 часов, остальные часы сохраняют половину суток");

                List<ServiceInfo> services = ServiceOperations.GetServices();
                TaskDialog dialog = new TaskDialog(null, services, null);
                CheckBox hide = Field<CheckBox>(dialog, "hideWindowsServicesCheck");
                ICollectionView view = Field<ICollectionView>(dialog, "serviceView");
                hide.IsChecked = true;
                List<ServiceInfo> filtered = view.Cast<ServiceInfo>().ToList();
                Check(filtered.Count <= services.Count &&
                    filtered.All(s => !s.IsWindowsService),
                    "фильтр скрывает стандартные службы и оставляет сторонние");
                Invoke(dialog, "ApplyServiceFilter", "Amnezia");
                List<ServiceInfo> combined = view.Cast<ServiceInfo>().ToList();
                Check(combined.All(s => !s.IsWindowsService &&
                    s.SearchText.IndexOf("Amnezia", StringComparison.CurrentCultureIgnoreCase) >= 0),
                    "текстовый поиск работает вместе с фильтром стандартных служб");
                hide.IsChecked = false;
                Invoke(dialog, "ApplyServiceFilter", "");
                Check(view.Cast<ServiceInfo>().Count() == services.Count,
                    "после отключения фильтра возвращается полный список служб");

                TaskInfo propertiesTask = new TaskInfo
                {
                    Name = "Spooler — Перезапуск — ежедневно 23-57",
                    Path = "\\ServiceManager\\Spooler — Перезапуск — ежедневно 23-57",
                    ServiceName = "Spooler",
                    IsOwn = true
                };
                TaskDialog propertiesDialog = new TaskDialog(null, services, propertiesTask);
                Check(propertiesDialog.Title == propertiesTask.Name,
                    "окно свойств содержит точное имя выбранного задания");

                LogWindow taskLog = new LogWindow(null, propertiesTask);
                TextBlock taskFilter = Field<TextBlock>(taskLog, "taskFilterText");
                bool matchingEntry = (bool)Invoke(taskLog, "MatchesTask", new LogEntryInfo
                {
                    TaskPath = propertiesTask.Path,
                    ServiceName = propertiesTask.ServiceName
                });
                bool otherEntry = (bool)Invoke(taskLog, "MatchesTask", new LogEntryInfo
                {
                    TaskPath = "\\ServiceManager\\Другое задание",
                    ServiceName = "OtherService"
                });
                Check(taskFilter.Text.Contains(propertiesTask.Path) && matchingEntry && !otherEntry,
                    "журнал результата отфильтрован по выбранному заданию");

                MainWindow window = new MainWindow();
                DataGrid grid = Field<DataGrid>(window, "grid");
                Check(HasPaleGreenSelection(grid.RowStyle, DataGridRow.IsSelectedProperty,
                        DataGridRow.BackgroundProperty) &&
                    HasPaleGreenSelection(grid.CellStyle, DataGridCell.IsSelectedProperty,
                        DataGridCell.BackgroundProperty),
                    "выделение строки и ячеек использует бледно-зелёный фон");

                Check(HelpContent.FullText.Contains("свойства именно этого задания") &&
                    HelpContent.FullText.Contains("Не показывать стандартные службы Windows") &&
                    HelpContent.FullText.Contains("Верхнее деление часов означает 12"),
                    "справка описывает точные переходы, фильтр служб и выбор часов");

                Console.WriteLine("ВСЕ ПРОВЕРКИ ИНТЕРФЕЙСА ПРОЙДЕНЫ: " + assertions);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }
    }
}
