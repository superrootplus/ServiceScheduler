using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceScheduler
{
    public sealed class LogWindow : Window
    {
        private TaskInfo selectedTask;
        private List<LogEntryInfo> entries = new List<LogEntryInfo>();
        private ICollectionView view;
        private DataGrid grid;
        private DatePicker fromDate;
        private DatePicker toDate;
        private TextBlock summary;
        private TextBlock taskFilterText;
        private Border header;

        public LogWindow(Window owner, TaskInfo task)
        {
            Owner = owner;
            selectedTask = task;
            Title = task == null ? "Журнал выполнения" : "Журнал задания";
            Width = 1240;
            Height = 720;
            MinWidth = 900;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = UiFactory.WindowBackground;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Foreground = UiFactory.Text;

            DockPanel root = new DockPanel();
            header = UiFactory.Header("Журнал выполнения",
                "Источник: журнал Windows ServiceScheduler");
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Border filters = BuildFilters();
            DockPanel.SetDock(filters, Dock.Top);
            root.Children.Add(filters);

            Border status = new Border
            {
                Background = Brushes.White,
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 7, 12, 7)
            };
            summary = new TextBlock { Text = "Готово" };
            status.Child = summary;
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            Border card = UiFactory.Card(BuildGrid(), new Thickness(0));
            card.Margin = new Thickness(12);
            root.Children.Add(card);
            Content = root;
            Loaded += delegate { RefreshEntries(); };
        }

        private Border BuildFilters()
        {
            DockPanel panel = new DockPanel { LastChildFill = true };
            WrapPanel dates = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            dates.Children.Add(UiFactory.Label("С", true));
            fromDate = DateField();
            dates.Children.Add(fromDate);
            dates.Children.Add(UiFactory.Label("По", true));
            toDate = DateField();
            dates.Children.Add(toDate);
            Button apply = UiFactory.TextButton("Показать", UiFactory.Blue,
                UiFactory.BlueHover, delegate { ApplyFilters(); }, false);
            apply.MinWidth = 100;
            apply.Height = 36;
            dates.Children.Add(apply);
            Button reset = UiFactory.TextButton("Сбросить даты", UiFactory.Gray,
                UiFactory.GrayHover, delegate
                {
                    fromDate.SelectedDate = null;
                    toDate.SelectedDate = null;
                    ApplyFilters();
                }, true);
            reset.MinWidth = 125;
            reset.Height = 36;
            dates.Children.Add(reset);
            DockPanel.SetDock(dates, Dock.Left);
            panel.Children.Add(dates);

            taskFilterText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiFactory.MutedText,
                Margin = new Thickness(16, 4, 4, 4)
            };
            panel.Children.Add(taskFilterText);
            UpdateTaskFilterText();
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8),
                Child = panel
            };
        }

        private DatePicker DateField()
        {
            return new DatePicker
            {
                Width = 145,
                Height = 36,
                Margin = new Thickness(7, 3, 14, 3),
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private DataGrid BuildGrid()
        {
            grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = UiFactory.Brush("#E5E7EB"),
                VerticalGridLinesBrush = UiFactory.Brush("#D1D5DB"),
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                RowBackground = Brushes.White,
                AlternatingRowBackground = UiFactory.Brush("#F9FAFB"),
                EnableRowVirtualization = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            grid.ColumnHeaderStyle = HeaderStyle();
            AddColumn("Дата и время", "TimeText", 165, false);
            AddColumn("Тип", "Level", 120, false);
            AddColumn("Служба", "ServiceName", 150, false);
            AddColumn("Задание", "TaskName", 260, false);
            AddColumn("Сообщение", "Message", 1, true);
            grid.MouseDoubleClick += delegate
            {
                LogEntryInfo entry = grid.SelectedItem as LogEntryInfo;
                if (entry == null) return;
                string text = "Дата и время: " + entry.TimeText + "\nТип: " + entry.Level +
                    "\nСлужба: " + (entry.ServiceName ?? "—") + "\nЗадание: " +
                    (string.IsNullOrEmpty(entry.TaskPath) ? "—" : entry.TaskPath) +
                    "\n\n" + entry.Message;
                UiFactory.ShowTextWindow(this, "Запись журнала", text);
            };
            return grid;
        }

        private static Style HeaderStyle()
        {
            Style style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty,
                UiFactory.Brush("#EEF2FF")));
            style.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty,
                UiFactory.Brush("#1E3A8A")));
            style.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty,
                FontWeights.SemiBold));
            style.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty,
                new Thickness(8)));
            return style;
        }

        private void AddColumn(string title, string binding, double width, bool wrap)
        {
            Style textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
            textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            if (wrap)
                textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,
                    TextWrapping.Wrap));
            else
                textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,
                    TextTrimming.CharacterEllipsis));
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = title,
                Binding = new Binding(binding),
                Width = wrap ? new DataGridLength(1, DataGridLengthUnitType.Star) :
                    new DataGridLength(width),
                ElementStyle = textStyle
            });
        }

        private void RefreshEntries()
        {
            summary.Text = "Чтение журнала Windows…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LogEntryInfo> loaded = Logger.ReadEntries(5000);
                string error = Logger.LastError;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    entries = loaded;
                    view = CollectionViewSource.GetDefaultView(entries);
                    grid.ItemsSource = view;
                    ApplyFilters();
                    if (!string.IsNullOrEmpty(error))
                        summary.Text += "  |  Ошибка чтения: " + error;
                }));
            });
        }

        private void ApplyFilters()
        {
            if (view == null) return;
            DateTime? from = fromDate.SelectedDate;
            DateTime? to = toDate.SelectedDate;
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            {
                MessageBox.Show(this, "Дата «С» не может быть позже даты «По».",
                    "Фильтр журнала", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DateTime toExclusive = to.HasValue ? to.Value.Date.AddDays(1) : DateTime.MaxValue;
            view.Filter = delegate(object item)
            {
                LogEntryInfo entry = item as LogEntryInfo;
                if (entry == null) return false;
                if (from.HasValue && entry.Time < from.Value.Date) return false;
                if (to.HasValue && entry.Time >= toExclusive) return false;
                return MatchesTask(entry);
            };
            view.Refresh();
            summary.Text = "Показано записей: " + view.Cast<LogEntryInfo>().Count() +
                " из " + entries.Count;
        }

        private bool MatchesTask(LogEntryInfo entry)
        {
            if (selectedTask == null) return true;
            if (!string.IsNullOrEmpty(entry.TaskPath))
                return string.Equals(entry.TaskPath, selectedTask.Path,
                    StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(entry.ServiceName))
                return string.Equals(entry.ServiceName, selectedTask.ServiceName,
                    StringComparison.OrdinalIgnoreCase);
            string message = entry.Message ?? string.Empty;
            return message.IndexOf(selectedTask.Path ?? string.Empty,
                StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("служба=" + selectedTask.ServiceName,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateTaskFilterText()
        {
            taskFilterText.Text = selectedTask == null ? "Все задания программы и системные события" :
                "Только задание:\n" + selectedTask.Path;
        }
    }
}
