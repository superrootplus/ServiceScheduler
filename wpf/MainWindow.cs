using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServiceScheduler
{
    public sealed class MainWindow : Window
    {
        private List<ServiceInfo> services = new List<ServiceInfo>();
        private List<TaskInfo> tasks = new List<TaskInfo>();
        private DataGrid grid;
        private CheckBox showAllCheck;
        private TextBlock statusText;
        private int refreshVersion;

        public MainWindow()
        {
            Title = "Планировщик служб Windows 2.1";
            Width = 1440;
            Height = 800;
            MinWidth = 1120;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = UiFactory.WindowBackground;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Foreground = UiFactory.Text;

            DockPanel root = new DockPanel();
            root.Children.Add(BuildMenu());
            DockPanel.SetDock(root.Children[0], Dock.Top);

            Border header = UiFactory.Header(
                "Планировщик служб Windows",
                "Версия " + Program.Version + "  •  Управление службами через Планировщик заданий");
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Border toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            Border status = BuildStatusBar();
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            Border gridCard = UiFactory.Card(BuildGrid(), new Thickness(0));
            gridCard.Margin = new Thickness(12, 4, 12, 10);
            root.Children.Add(gridCard);
            Content = root;

            Loaded += delegate
            {
                RefreshServices();
                RefreshTasks();
            };
        }

        private Menu BuildMenu()
        {
            Menu menu = new Menu { FontSize = 14, Background = Brushes.White };
            MenuItem file = new MenuItem { Header = "Файл" };
            file.Items.Add(MenuCommand("Выход", delegate { Close(); }));
            menu.Items.Add(file);

            MenuItem taskMenu = new MenuItem { Header = "Задания" };
            taskMenu.Items.Add(MenuCommand("Добавить…", delegate { AddTask(); }));
            taskMenu.Items.Add(MenuCommand("Изменить…", delegate { EditTask(); }));
            taskMenu.Items.Add(MenuCommand("Удалить", delegate { DeleteTask(); }));
            taskMenu.Items.Add(new Separator());
            taskMenu.Items.Add(MenuCommand("Выполнить сейчас", delegate { RunNow(); }));
            taskMenu.Items.Add(MenuCommand("Включить / отключить", delegate { ToggleTask(); }));
            menu.Items.Add(taskMenu);

            MenuItem view = new MenuItem { Header = "Вид" };
            view.Items.Add(MenuCommand("Обновить  (F5)", delegate { RefreshTasks(); }));
            menu.Items.Add(view);

            MenuItem tools = new MenuItem { Header = "Инструменты" };
            tools.Items.Add(MenuCommand("Журнал выполнения", delegate { ShowLog(); }));
            tools.Items.Add(MenuCommand("Открыть Просмотр событий Windows",
                delegate { Process.Start("eventvwr.msc"); }));
            tools.Items.Add(new Separator());
            tools.Items.Add(MenuCommand("Защитить / обновить исполнителя заданий",
                delegate { UpdateExecutor(); }));
            menu.Items.Add(tools);

            MenuItem help = new MenuItem { Header = "Справка" };
            help.Items.Add(MenuCommand("Руководство пользователя", delegate
            {
                UiFactory.ShowTextWindow(this, "Руководство пользователя", HelpContent.FullText);
            }));
            help.Items.Add(MenuCommand("О программе", delegate
            {
                MessageBox.Show(this,
                    "Планировщик служб Windows\nВерсия " + Program.Version +
                    "\n\nНативный интерфейс Windows (WPF).\n" +
                    "Запуск, остановка, перезапуск и жёсткие операции со службами\n" +
                    "через стандартный Планировщик заданий.\n\n" +
                    "Один exe, запуск от администратора.",
                    "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
            }));
            menu.Items.Add(help);
            return menu;
        }

        private static MenuItem MenuCommand(string header, RoutedEventHandler click)
        {
            MenuItem item = new MenuItem { Header = header };
            item.Click += click;
            return item;
        }

        private Border BuildToolbar()
        {
            DockPanel dock = new DockPanel { LastChildFill = false };
            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            buttons.Children.Add(UiFactory.IconButton("\uE710", "Добавить задание",
                UiFactory.Green, UiFactory.GreenHover, delegate { AddTask(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE70F", "Изменить задание",
                UiFactory.Blue, UiFactory.BlueHover, delegate { EditTask(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE74D", "Удалить задание",
                UiFactory.Red, UiFactory.RedHover, delegate { DeleteTask(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE768", "Выполнить сейчас",
                UiFactory.Green, UiFactory.GreenHover, delegate { RunNow(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE7E8", "Включить / отключить задание",
                UiFactory.Orange, UiFactory.OrangeHover, delegate { ToggleTask(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE72C", "Обновить список (F5)",
                UiFactory.Blue, UiFactory.BlueHover, delegate { RefreshTasks(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE8A5", "Журнал выполнения",
                UiFactory.Gray, UiFactory.GrayHover, delegate { ShowLog(); }));
            buttons.Children.Add(UiFactory.IconButton("\uE897", "Справка",
                UiFactory.Blue, UiFactory.BlueHover, delegate
                {
                    UiFactory.ShowTextWindow(this, "Руководство пользователя",
                        HelpContent.FullText);
                }));
            DockPanel.SetDock(buttons, Dock.Left);
            dock.Children.Add(buttons);

            showAllCheck = new CheckBox
            {
                Content = "Показать все задачи планировщика Windows",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 4, 0)
            };
            showAllCheck.Checked += delegate { RefreshTasks(); };
            showAllCheck.Unchecked += delegate { RefreshTasks(); };
            DockPanel.SetDock(showAllCheck, Dock.Right);
            dock.Children.Add(showAllCheck);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(10, 9, 10, 9),
                Child = dock
            };
        }

        private DataGrid BuildGrid()
        {
            grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
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
                RowHeight = 38,
                FontSize = 14,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                EnableRowVirtualization = true
            };
            grid.ColumnHeaderStyle = ColumnHeaderStyle();
            grid.CellStyle = CellStyle();
            grid.RowStyle = RowStyle();

            AddLinkColumn("Задание", "Name", 200, "Открыть свойства именно этого задания",
                OpenTaskScheduler);
            AddLinkColumn("Служба", "ServiceName", 180, "Открыть свойства этой службы в Windows",
                OpenServiceConsole);
            AddColumn("Операция", "OperationText", 190);
            AddStarColumn("Расписание", "ScheduleText", 1.4);
            AddColumn("Последний запуск", "LastRunText", 150);
            AddColumn("Следующий запуск", "NextRunText", 150);
            AddLinkColumn("Результат", "LastResultText", 130, "Открыть журнал этого задания",
                ShowLog);
            AddColumn("Состояние", "StateText", 100);
            AddColumn("Тип", "KindText", 80);

            ContextMenu context = new ContextMenu { FontSize = 14 };
            context.Items.Add(MenuCommand("Добавить…", delegate { AddTask(); }));
            context.Items.Add(MenuCommand("Изменить…", delegate { EditTask(); }));
            context.Items.Add(MenuCommand("Удалить", delegate { DeleteTask(); }));
            context.Items.Add(new Separator());
            context.Items.Add(MenuCommand("Выполнить сейчас", delegate { RunNow(); }));
            context.Items.Add(MenuCommand("Включить / отключить", delegate { ToggleTask(); }));
            context.Items.Add(MenuCommand("Журнал выполнения", delegate { ShowLog(); }));
            grid.ContextMenu = context;
            return grid;
        }

        private void AddColumn(string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(width),
                ElementStyle = CellTextStyle()
            });
        }

        private void AddLinkColumn(string header, string binding, double width,
            string tooltip, Action<TaskInfo> open)
        {
            DataTemplate template = new DataTemplate();
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(binding) { Mode = BindingMode.OneWay });
            text.SetValue(TextBlock.ForegroundProperty, UiFactory.Brush(UiFactory.Blue));
            text.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.CursorProperty, Cursors.Hand);
            text.SetValue(TextBlock.ToolTipProperty, tooltip);
            text.AddHandler(TextBlock.MouseLeftButtonUpEvent, new MouseButtonEventHandler(
                delegate(object sender, MouseButtonEventArgs e)
                {
                    FrameworkElement element = sender as FrameworkElement;
                    TaskInfo task = element == null ? null : element.DataContext as TaskInfo;
                    if (task == null) return;
                    grid.SelectedItem = task;
                    e.Handled = true;
                    open(task);
                }));
            template.VisualTree = text;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = template,
                Width = new DataGridLength(width)
            });
        }

        private void AddStarColumn(string header, string binding, double weight)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
                ElementStyle = CellTextStyle()
            });
        }

        private static Style ColumnHeaderStyle()
        {
            Style style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty,
                UiFactory.Brush("#EEF2FF")));
            style.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty,
                UiFactory.Brush("#1E3A8A")));
            style.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty,
                FontWeights.SemiBold));
            style.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty,
                new Thickness(8, 9, 8, 9)));
            style.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty,
                UiFactory.DefaultBorderBrush));
            return style;
        }

        private static Style CellStyle()
        {
            Style style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(DataGridCell.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            style.Setters.Add(new Setter(DataGridCell.PaddingProperty,
                new Thickness(6, 2, 6, 2)));
            Trigger selected = new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(DataGridCell.BackgroundProperty,
                UiFactory.Brush("#DCFCE7")));
            selected.Setters.Add(new Setter(DataGridCell.ForegroundProperty,
                UiFactory.Text));
            style.Triggers.Add(selected);
            return style;
        }

        private static Style CellTextStyle()
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,
                TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,
                VerticalAlignment.Center));
            return style;
        }

        private static Style RowStyle()
        {
            Style style = new Style(typeof(DataGridRow));
            DataTrigger foreign = new DataTrigger
            {
                Binding = new Binding("IsOwn"),
                Value = false
            };
            foreign.Setters.Add(new Setter(DataGridRow.ForegroundProperty,
                UiFactory.MutedText));
            style.Triggers.Add(foreign);
            DataTrigger disabled = new DataTrigger
            {
                Binding = new Binding("IsEnabled"),
                Value = false
            };
            disabled.Setters.Add(new Setter(DataGridRow.ForegroundProperty,
                UiFactory.Brush("#B91C1C")));
            style.Triggers.Add(disabled);
            Trigger selected = new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                UiFactory.Brush("#DCFCE7")));
            selected.Setters.Add(new Setter(DataGridRow.ForegroundProperty,
                UiFactory.Text));
            style.Triggers.Add(selected);
            return style;
        }

        private Border BuildStatusBar()
        {
            DockPanel dock = new DockPanel();
            TextBlock admin = new TextBlock
            {
                Text = "●  Администратор",
                Foreground = UiFactory.Brush(UiFactory.Green),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(admin, Dock.Right);
            dock.Children.Add(admin);
            statusText = new TextBlock
            {
                Text = "Готово",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dock.Children.Add(statusText);
            return new Border
            {
                Background = UiFactory.Brush("#EEF2F7"),
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Child = dock
            };
        }

        private void RefreshServices()
        {
            statusText.Text = "Чтение списка служб…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<ServiceInfo> loaded = ServiceOperations.GetServices();
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        services = loaded;
                        statusText.Text = "Служб в системе: " + services.Count;
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        statusText.Text = "Ошибка чтения служб";
                        MessageBox.Show(this, UiFactory.ErrorText(ex), "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                }
            });
        }

        public void RefreshTasks()
        {
            int version = Interlocked.Increment(ref refreshVersion);
            bool ownOnly = showAllCheck == null || showAllCheck.IsChecked != true;
            statusText.Text = "Чтение списка заданий…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<TaskInfo> loaded = Scheduler.ListTasks(ownOnly);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != refreshVersion) return;
                        tasks = loaded;
                        grid.ItemsSource = null;
                        grid.ItemsSource = tasks;
                        int own = tasks.Count(t => t.IsOwn);
                        int migration = 0;
                        foreach (TaskInfo task in tasks)
                        {
                            try { if (ExecutorDeployer.NeedsMigration(task)) migration++; }
                            catch { }
                        }
                        statusText.Text = string.Format("Заданий: {0}  |  своих: {1}",
                            tasks.Count, own);
                        if (migration > 0)
                            statusText.Text += "  |  старый исполнитель: " + migration +
                                " (Инструменты → обновить)";
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != refreshVersion) return;
                        statusText.Text = "Ошибка чтения заданий";
                        MessageBox.Show(this, UiFactory.ErrorText(ex), "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                }
            });
        }

        private TaskInfo SelectedTask()
        {
            return grid.SelectedItem as TaskInfo;
        }

        private TaskInfo RequireOwnTask()
        {
            TaskInfo task = SelectedTask();
            if (task == null)
            {
                MessageBox.Show(this, "Выберите задание в таблице.", "Задания",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            if (!task.IsOwn)
            {
                MessageBox.Show(this,
                    "Это системное задание — оно доступно только для просмотра.",
                    "Задания", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return task;
        }

        private void AddTask()
        {
            if (services.Count == 0)
            {
                MessageBox.Show(this, "Список служб ещё загружается.", "Задания",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            TaskDialog dialog = new TaskDialog(this, services, null);
            if (dialog.ShowDialog() == true) RefreshTasks();
        }

        private void EditTask()
        {
            TaskInfo task = RequireOwnTask();
            if (task == null) return;
            TaskDialog dialog = new TaskDialog(this, services, task);
            if (dialog.ShowDialog() == true) RefreshTasks();
        }

        private void DeleteTask()
        {
            TaskInfo task = RequireOwnTask();
            if (task == null) return;
            if (MessageBox.Show(this, "Удалить задание?\n\n" + task.Path,
                "Удаление задания", MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes) return;
            try
            {
                Scheduler.DeleteTask(task.Path);
                Logger.Info("Задание удалено", task.Path, task.ServiceName);
                RefreshTasks();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void RunNow()
        {
            TaskInfo task = RequireOwnTask();
            if (task == null) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                Scheduler.RunNow(task.Path);
                Logger.Info("Задание запущено вручную", task.Path, task.ServiceName);
                statusText.Text = "Задание запущено: " + task.Name;
                DispatcherTimer timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += delegate
                {
                    timer.Stop();
                    RefreshTasks();
                };
                timer.Start();
            }
            catch (Exception ex) { ShowError(ex); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void ToggleTask()
        {
            TaskInfo task = RequireOwnTask();
            if (task == null) return;
            try
            {
                Scheduler.SetEnabled(task.Path, !task.IsEnabled);
                Logger.Info(task.IsEnabled ? "Задание отключено" : "Задание включено",
                    task.Path, task.ServiceName);
                RefreshTasks();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void UpdateExecutor()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                string path = ExecutorDeployer.EnsureProtectedExecutor();
                int updated = Scheduler.UpdateExecutorForOwnTasks(path);
                Logger.Info("Защищённый исполнитель подготовлен: " + path +
                    "; обновлено заданий: " + updated);
                MessageBox.Show(this,
                    "Исполнитель подготовлен:\n" + path +
                    "\n\nОбновлено заданий: " + updated,
                    "Исполнитель заданий", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshTasks();
            }
            catch (Exception ex) { ShowError(ex); }
            finally { Mouse.OverrideCursor = null; }
        }

        private void OpenTaskScheduler(TaskInfo task)
        {
            if (!task.IsOwn)
            {
                string text = "Системное задание Windows. Доступен только просмотр.\n\n" +
                    "Имя: " + task.Name + "\nПуть: " + task.Path +
                    "\nСлужба: " + task.ServiceName +
                    "\nОперация: " + task.OperationText +
                    "\nРасписание: " + task.ScheduleText +
                    "\nПоследний запуск: " + task.LastRunText +
                    "\nСледующий запуск: " + task.NextRunText +
                    "\nРезультат: " + task.LastResultText +
                    "\nСостояние: " + task.StateText +
                    "\n\nДействие: " + task.ActionExecutable +
                    "\nАргументы: " + task.ActionArguments +
                    "\n\nОписание:\n" + task.Description;
                statusText.Text = "Открыты свойства задания: " + task.Name;
                UiFactory.ShowTextWindow(this, task.Name, text);
                return;
            }
            if (services.Count == 0)
            {
                MessageBox.Show(this, "Список служб ещё загружается.", "Задания",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            TaskDialog dialog = new TaskDialog(this, services, task);
            statusText.Text = "Открыты свойства задания: " + task.Name;
            if (dialog.ShowDialog() == true) RefreshTasks();
        }

        private void OpenServiceConsole(TaskInfo task)
        {
            if (string.IsNullOrWhiteSpace(task.ServiceName))
            {
                MessageBox.Show(this, "Для этого системного задания служба не указана.",
                    "Служба", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ServiceInfo service = services.FirstOrDefault(s => string.Equals(s.Name,
                task.ServiceName, StringComparison.OrdinalIgnoreCase));
            if (service == null)
                service = new ServiceInfo { Name = task.ServiceName, DisplayName = task.ServiceName };
            statusText.Text = "Открываю свойства службы…";
            Mouse.OverrideCursor = Cursors.Wait;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    WindowsEditors.OpenService(service);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        statusText.Text = "Открыты свойства службы: " + service.Name;
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { ShowError(ex); }));
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Mouse.OverrideCursor = null;
                    }));
                }
            });
        }

        private void ShowLog(TaskInfo task = null)
        {
            LogWindow window = new LogWindow(this, task);
            window.ShowDialog();
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, UiFactory.ErrorText(ex), "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F5)
            {
                RefreshTasks();
                e.Handled = true;
            }
        }
    }
}
