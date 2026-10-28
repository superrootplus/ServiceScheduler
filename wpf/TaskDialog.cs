using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ServiceScheduler
{
    public sealed class TaskDialog : Window
    {
        private sealed class ServiceComboBox : ComboBox
        {
            protected override void OnDropDownOpened(EventArgs e)
            {
                Popup popup = GetTemplateChild("PART_Popup") as Popup;
                if (popup != null)
                {
                    popup.Width = ActualWidth;
                    popup.MaxHeight = MaxDropDownHeight;
                }
                base.OnDropDownOpened(e);
            }
        }

        private sealed class OperationChoice
        {
            public ServiceOperation Operation { get; set; }
            public string Title { get; set; }
        }

        private sealed class ScheduleChoice
        {
            public ScheduleType Type { get; set; }
            public string Title { get; set; }
        }

        private readonly List<ServiceInfo> services;
        private readonly TaskInfo originalTask;
        private ComboBox serviceCombo;
        private ICollectionView serviceView;
        private ComboBox operationCombo;
        private ComboBox scheduleCombo;
        private TextBox timeBox;
        private DatePicker datePicker;
        private TextBox monthDayBox;
        private TextBox intervalBox;
        private CheckBox disabledCheck;
        private TextBlock serviceHint;
        private TextBlock warningText;
        private readonly List<CheckBox> dayChecks = new List<CheckBox>();
        private readonly List<UIElement> dateControls = new List<UIElement>();
        private readonly List<UIElement> timeControls = new List<UIElement>();
        private readonly List<UIElement> weekControls = new List<UIElement>();
        private readonly List<UIElement> monthControls = new List<UIElement>();
        private readonly List<UIElement> intervalControls = new List<UIElement>();
        private bool changingServiceSelection;
        private bool openingFilter;

        public TaskDialog(Window owner, List<ServiceInfo> availableServices, TaskInfo task)
        {
            services = availableServices;
            originalTask = task;
            Owner = owner;
            Title = task == null ? "Новое задание" : "Изменение задания";
            Width = 720;
            Height = 760;
            MinWidth = 640;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = UiFactory.WindowBackground;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Foreground = UiFactory.Text;
            ShowInTaskbar = false;

            DockPanel root = new DockPanel();
            Border header = UiFactory.Header(
                task == null ? "Новое задание планировщика" : "Изменение задания",
                task == null ? "Выберите службу, операцию и расписание" : task.Path);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Border buttons = BuildButtons();
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14)
            };
            StackPanel content = new StackPanel();
            content.Children.Add(UiFactory.Card(BuildServiceSection(), new Thickness(18)));
            Border scheduleCard = UiFactory.Card(BuildScheduleSection(), new Thickness(18));
            scheduleCard.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(scheduleCard);

            warningText = new TextBlock
            {
                Text = "Жёсткий перезапуск сначала пробует штатную остановку. " +
                    "taskkill применяется только при её сбое.",
                Foreground = UiFactory.Brush("#92400E"),
                Background = UiFactory.Brush("#FFFBEB"),
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed
            };
            content.Children.Add(warningText);
            scroll.Content = content;
            root.Children.Add(scroll);
            Content = root;

            BuildOperationChoices();
            BuildScheduleChoices();
            LoadInitialValues();
            UpdateScheduleVisibility();
        }

        private UIElement BuildServiceSection()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(SectionTitle("Служба и действие"));

            panel.Children.Add(UiFactory.Label("Служба (можно выбрать стрелкой или начать ввод)", true));
            serviceView = CollectionViewSource.GetDefaultView(services);
            serviceCombo = new ServiceComboBox
            {
                Height = 42,
                Margin = new Thickness(0, 5, 0, 3),
                FontSize = 15,
                IsEditable = true,
                IsTextSearchEnabled = false,
                StaysOpenOnEdit = true,
                MaxDropDownHeight = 420,
                ItemsSource = serviceView,
                ItemTemplate = ServiceTemplate()
            };
            serviceCombo.AddHandler(TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(ServiceTextChanged));
            serviceCombo.SelectionChanged += ServiceSelectionChanged;
            serviceCombo.DropDownOpened += ServiceDropDownOpened;
            panel.Children.Add(serviceCombo);

            serviceHint = new TextBlock
            {
                Text = "Введите часть названия или точное имя службы, затем выберите её в списке.",
                FontSize = 13,
                Foreground = UiFactory.MutedText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 13)
            };
            panel.Children.Add(serviceHint);

            panel.Children.Add(UiFactory.Label("Операция", true));
            operationCombo = UiFactory.Combo(0);
            operationCombo.Width = double.NaN;
            operationCombo.Height = 40;
            operationCombo.Margin = new Thickness(0, 5, 0, 0);
            operationCombo.DisplayMemberPath = "Title";
            operationCombo.SelectionChanged += delegate
            {
                OperationChoice choice = operationCombo.SelectedItem as OperationChoice;
                bool kill = choice != null && OperationInfo.UsesKill(choice.Operation);
                warningText.Visibility = kill ? Visibility.Visible : Visibility.Collapsed;
                if (kill)
                    warningText.Text = choice.Operation == ServiceOperation.HardStop
                        ? "Жёсткая остановка сразу выполняет taskkill и не запускает службу повторно."
                        : "Жёсткий перезапуск сначала пробует штатную остановку. " +
                          "taskkill применяется только при её сбое.";
            };
            panel.Children.Add(operationCombo);
            return panel;
        }

        private static DataTemplate ServiceTemplate()
        {
            DataTemplate template = new DataTemplate();
            FrameworkElementFactory row = new FrameworkElementFactory(typeof(Grid));
            row.SetValue(Grid.HeightProperty, 32.0);
            FrameworkElementFactory dotColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            dotColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(24));
            FrameworkElementFactory textColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            textColumn.SetValue(ColumnDefinition.WidthProperty,
                new GridLength(1, GridUnitType.Star));
            row.AppendChild(dotColumn);
            row.AppendChild(textColumn);
            FrameworkElementFactory dot = new FrameworkElementFactory(typeof(Ellipse));
            dot.SetValue(Grid.ColumnProperty, 0);
            dot.SetValue(Ellipse.WidthProperty, 10.0);
            dot.SetValue(Ellipse.HeightProperty, 10.0);
            dot.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dot.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetBinding(Shape.FillProperty, new Binding("IsRunning")
            {
                Converter = new RunningStatusBrushConverter()
            });
            row.AppendChild(dot);

            FrameworkElementFactory display = new FrameworkElementFactory(typeof(TextBlock));
            display.SetValue(Grid.ColumnProperty, 1);
            display.SetBinding(TextBlock.TextProperty, new Binding("Display"));
            display.SetValue(TextBlock.FontSizeProperty, 14.0);
            display.SetValue(TextBlock.ForegroundProperty, UiFactory.Text);
            display.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            display.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            display.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            row.AppendChild(display);
            template.VisualTree = row;
            return template;
        }

        private UIElement BuildScheduleSection()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(SectionTitle("Расписание"));
            panel.Children.Add(UiFactory.Label("Периодичность", true));
            scheduleCombo = UiFactory.Combo(0);
            scheduleCombo.Width = double.NaN;
            scheduleCombo.Height = 40;
            scheduleCombo.Margin = new Thickness(0, 5, 0, 12);
            scheduleCombo.DisplayMemberPath = "Title";
            scheduleCombo.SelectionChanged += delegate { UpdateScheduleVisibility(); };
            panel.Children.Add(scheduleCombo);

            Grid fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,
                GridUnitType.Star) });

            datePicker = new DatePicker
            {
                Width = 180,
                Height = 38,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Left,
                SelectedDate = DateTime.Today
            };
            AddField(fields, "Дата", datePicker, dateControls);

            Grid timePanel = new Grid
            {
                Width = 182,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            timePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            timePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            timeBox = UiFactory.Input("09:00", 120);
            timeBox.HorizontalAlignment = HorizontalAlignment.Left;
            timeBox.ToolTip = "Можно ввести время вручную или выбрать на часах";
            Grid.SetColumn(timeBox, 0);
            timePanel.Children.Add(timeBox);
            Button clockButton = UiFactory.IconButton("\uE916", "Выбрать время на часах",
                UiFactory.Blue, UiFactory.BlueHover, delegate
                {
                    TimeSpan initial;
                    try { initial = ReadTime(timeBox.Text); }
                    catch { initial = new TimeSpan(9, 0, 0); }
                    TimePickerDialog picker = new TimePickerDialog(this, initial);
                    if (picker.ShowDialog() == true)
                        timeBox.Text = picker.SelectedTime.ToString(@"hh\:mm");
                });
            clockButton.Width = 42;
            clockButton.Height = 36;
            clockButton.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(clockButton, 1);
            timePanel.Children.Add(clockButton);
            AddField(fields, "Время (ЧЧ:мм)", timePanel, timeControls);

            WrapPanel days = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            AddDay(days, "Пн", 2);
            AddDay(days, "Вт", 4);
            AddDay(days, "Ср", 8);
            AddDay(days, "Чт", 16);
            AddDay(days, "Пт", 32);
            AddDay(days, "Сб", 64);
            AddDay(days, "Вс", 1);
            AddField(fields, "Дни недели", days, weekControls);

            monthDayBox = UiFactory.Input("1", 120);
            monthDayBox.HorizontalAlignment = HorizontalAlignment.Left;
            AddField(fields, "Число месяца (1–31)", monthDayBox, monthControls);

            intervalBox = UiFactory.Input("60", 120);
            intervalBox.HorizontalAlignment = HorizontalAlignment.Left;
            AddField(fields, "Повтор, минут", intervalBox, intervalControls);
            panel.Children.Add(fields);

            disabledCheck = new CheckBox
            {
                Content = "Сохранить задание отключённым",
                FontSize = 14,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(disabledCheck);
            return panel;
        }

        private static TextBlock SectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiFactory.Brush("#1E3A8A"),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static void AddField(Grid grid, string label, UIElement field,
            List<UIElement> group)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock caption = UiFactory.Label(label, true);
            caption.Margin = new Thickness(0, 4, 12, 10);
            Grid.SetRow(caption, row);
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);
            ((FrameworkElement)field).Margin = new Thickness(0, 4, 0, 10);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
            group.Add(caption);
            group.Add(field);
        }

        private void AddDay(Panel panel, string title, int mask)
        {
            CheckBox check = new CheckBox
            {
                Content = title,
                Tag = mask,
                MinWidth = 62,
                Margin = new Thickness(0, 0, 8, 4),
                FontSize = 14
            };
            dayChecks.Add(check);
            panel.Children.Add(check);
        }

        private Border BuildButtons()
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button cancel = UiFactory.TextButton("Отмена", UiFactory.Gray,
                UiFactory.GrayHover, delegate { DialogResult = false; }, true);
            Button save = UiFactory.TextButton(originalTask == null ? "Создать задание" :
                "Сохранить изменения", UiFactory.Blue, UiFactory.BlueHover,
                delegate { Save(); }, false);
            save.IsDefault = true;
            cancel.IsCancel = true;
            panel.Children.Add(cancel);
            panel.Children.Add(save);
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12),
                Child = panel
            };
        }

        private void BuildOperationChoices()
        {
            List<OperationChoice> choices = new List<OperationChoice>();
            foreach (ServiceOperation operation in OperationInfo.All)
                choices.Add(new OperationChoice
                {
                    Operation = operation,
                    Title = OperationInfo.ToRussian(operation)
                });
            operationCombo.ItemsSource = choices;
            operationCombo.SelectedIndex = 0;
        }

        private void BuildScheduleChoices()
        {
            scheduleCombo.ItemsSource = new List<ScheduleChoice>
            {
                new ScheduleChoice { Type = ScheduleType.Daily, Title = "Ежедневно" },
                new ScheduleChoice { Type = ScheduleType.Weekly, Title = "Еженедельно" },
                new ScheduleChoice { Type = ScheduleType.Monthly, Title = "Ежемесячно" },
                new ScheduleChoice { Type = ScheduleType.Once, Title = "Однократно" },
                new ScheduleChoice { Type = ScheduleType.Startup, Title = "При запуске системы" },
                new ScheduleChoice { Type = ScheduleType.Interval, Title = "Через интервал" }
            };
            scheduleCombo.SelectedIndex = 0;
        }

        private void LoadInitialValues()
        {
            ServiceInfo selectedService = null;
            ScheduleSpec schedule = new ScheduleSpec();
            if (originalTask != null)
            {
                selectedService = services.FirstOrDefault(s => string.Equals(s.Name,
                    originalTask.ServiceName, StringComparison.OrdinalIgnoreCase));
                if (originalTask.Operation.HasValue)
                {
                    foreach (OperationChoice choice in operationCombo.Items)
                        if (choice.Operation == originalTask.Operation.Value)
                            operationCombo.SelectedItem = choice;
                }
                if (originalTask.Schedule != null) schedule = originalTask.Schedule.Clone();
                disabledCheck.IsChecked = !originalTask.IsEnabled;
            }
            if (selectedService == null && services.Count > 0) selectedService = services[0];
            SetSelectedService(selectedService);

            foreach (ScheduleChoice choice in scheduleCombo.Items)
                if (choice.Type == schedule.Type) scheduleCombo.SelectedItem = choice;
            if (schedule.Type == ScheduleType.Other)
            {
                scheduleCombo.SelectedIndex = 0;
                MessageBox.Show(this,
                    "У этого задания нестандартный триггер. Выберите новое расписание: " +
                    "при сохранении оно заменит прежнее.",
                    "Изменение задания", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            timeBox.Text = schedule.Time.ToString(@"hh\:mm");
            datePicker.SelectedDate = schedule.Date == DateTime.MinValue ?
                DateTime.Today : schedule.Date;
            monthDayBox.Text = schedule.DayOfMonth.ToString(CultureInfo.InvariantCulture);
            intervalBox.Text = schedule.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
            foreach (CheckBox check in dayChecks)
                check.IsChecked = (schedule.DaysOfWeekMask & (int)check.Tag) != 0;
        }

        private void SetSelectedService(ServiceInfo service)
        {
            changingServiceSelection = true;
            try
            {
                ApplyServiceFilter(string.Empty);
                serviceCombo.SelectedItem = service;
                serviceCombo.Text = service == null ? string.Empty : service.Display;
                if (service != null)
                    serviceHint.Text = service.Status + "  •  " + service.Name;
            }
            finally { changingServiceSelection = false; }
        }

        private void ServiceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (changingServiceSelection) return;
            ServiceInfo selected = serviceCombo.SelectedItem as ServiceInfo;
            if (selected == null) return;
            changingServiceSelection = true;
            try
            {
                serviceCombo.Text = selected.Display;
                serviceHint.Text = selected.Status + "  •  " + selected.Name;
            }
            finally { changingServiceSelection = false; }
        }

        private void ServiceTextChanged(object sender, TextChangedEventArgs e)
        {
            if (changingServiceSelection || !IsLoaded) return;
            string query = serviceCombo.Text.Trim();
            changingServiceSelection = true;
            try
            {
                ApplyServiceFilter(query);
                // При скрытии выбранного пункта WPF очищает текст — восстанавливаем ввод.
                serviceCombo.Text = query;
            }
            finally { changingServiceSelection = false; }
            if (serviceCombo.IsKeyboardFocusWithin || serviceCombo.IsFocused)
            {
                openingFilter = true;
                serviceCombo.IsDropDownOpen = query.Length > 0;
            }
        }

        private void ServiceDropDownOpened(object sender, EventArgs e)
        {
            if (openingFilter)
            {
                openingFilter = false;
                return;
            }
            changingServiceSelection = true;
            try { ApplyServiceFilter(string.Empty); }
            finally { changingServiceSelection = false; }
        }

        private void ApplyServiceFilter(string query)
        {
            serviceView.Filter = null;
            if (!string.IsNullOrWhiteSpace(query))
            {
                serviceView.Filter = delegate(object item)
                {
                    ServiceInfo service = item as ServiceInfo;
                    return service != null && (service.SearchText ?? string.Empty).IndexOf(
                        query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                };
            }
            serviceView.Refresh();
        }

        private static void SetGroupVisible(IEnumerable<UIElement> controls, bool visible)
        {
            foreach (UIElement control in controls)
                control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateScheduleVisibility()
        {
            if (scheduleCombo == null || scheduleCombo.SelectedItem == null) return;
            ScheduleType type = ((ScheduleChoice)scheduleCombo.SelectedItem).Type;
            SetGroupVisible(dateControls, type == ScheduleType.Once);
            SetGroupVisible(timeControls, type != ScheduleType.Startup);
            SetGroupVisible(weekControls, type == ScheduleType.Weekly);
            SetGroupVisible(monthControls, type == ScheduleType.Monthly);
            SetGroupVisible(intervalControls, type == ScheduleType.Interval);
        }

        private ServiceInfo ResolveService()
        {
            string text = serviceCombo.Text.Trim();
            ServiceInfo selected = serviceCombo.SelectedItem as ServiceInfo;
            if (selected != null && (string.Equals(text, selected.Display,
                    StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(text, selected.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, selected.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase))) return selected;
            ServiceInfo exact = services.FirstOrDefault(s =>
                string.Equals(text, s.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, s.Display, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(text, s.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            if (exact == null)
                throw new ServiceSchedulerException(
                    "Выберите службу из раскрывающегося списка или введите её точное имя.");
            return exact;
        }

        private static TimeSpan ReadTime(string text)
        {
            DateTime parsed;
            string[] formats = { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };
            if (!DateTime.TryParseExact(text.Trim(), formats, CultureInfo.CurrentCulture,
                DateTimeStyles.NoCurrentDateDefault, out parsed) &&
                !DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out parsed))
                throw new ServiceSchedulerException(
                    "Время указано неверно. Используйте формат ЧЧ:мм, например 09:30.");
            return parsed.TimeOfDay;
        }

        private static int ReadInteger(TextBox box, int minimum, int maximum, string title)
        {
            int value;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer,
                CultureInfo.CurrentCulture, out value) || value < minimum || value > maximum)
                throw new ServiceSchedulerException(string.Format(
                    "Поле «{0}» должно содержать целое число от {1} до {2}.",
                    title, minimum, maximum));
            return value;
        }

        private ScheduleSpec ReadSchedule()
        {
            ScheduleType type = ((ScheduleChoice)scheduleCombo.SelectedItem).Type;
            ScheduleSpec schedule = new ScheduleSpec { Type = type };
            if (type != ScheduleType.Startup) schedule.Time = ReadTime(timeBox.Text);
            if (type == ScheduleType.Once)
            {
                if (!datePicker.SelectedDate.HasValue)
                    throw new ServiceSchedulerException("Выберите дату однократного запуска.");
                schedule.Date = datePicker.SelectedDate.Value.Date;
                if (schedule.Date.Add(schedule.Time) <= DateTime.Now)
                    throw new ServiceSchedulerException(
                        "Дата и время однократного запуска должны быть в будущем.");
            }
            if (type == ScheduleType.Weekly)
            {
                int mask = 0;
                foreach (CheckBox check in dayChecks)
                    if (check.IsChecked == true) mask |= (int)check.Tag;
                if (mask == 0)
                    throw new ServiceSchedulerException("Выберите хотя бы один день недели.");
                schedule.DaysOfWeekMask = mask;
            }
            if (type == ScheduleType.Monthly)
                schedule.DayOfMonth = ReadInteger(monthDayBox, 1, 31, "Число месяца");
            if (type == ScheduleType.Interval)
                schedule.IntervalMinutes = ReadInteger(intervalBox, 1, 525600, "Повтор, минут");
            return schedule;
        }

        private bool ConfirmSharedKill(ServiceInfo service, ServiceOperation operation)
        {
            if (!OperationInfo.UsesKill(operation) ||
                (!service.IsSharedProcess && service.ProcessTypeKnown)) return false;
            List<string> affected;
            try { affected = ServiceOperations.GetServicesOnSameProcess(service.Name); }
            catch { affected = new List<string>(); }
            if (affected.Count == 0) affected.Add(service.DisplayName + " (" + service.Name + ")");
            string list = string.Join("\n", affected.Take(18)
                .Select(name => "  •  " + name).ToArray());
            if (affected.Count > 18) list += "\n  … и другие";
            string behavior = operation == ServiceOperation.HardStop
                ? "taskkill сразу завершит весь процесс"
                : "если штатная остановка не поможет, taskkill завершит весь процесс";
            MessageBoxResult answer = MessageBox.Show(this,
                "Служба работает в общем процессе или тип процесса не удалось определить.\n\n" +
                "При автоматическом выполнении " + behavior + ". Это затронет перечисленные службы:\n\n" +
                list + "\n\nРазрешить принудительное завершение общего процесса?",
                "Подтверждение: " + OperationInfo.ToRussian(operation), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }

        private void Save()
        {
            try
            {
                ServiceInfo selectedService = ResolveService();
                OperationChoice selectedOperation = (OperationChoice)operationCombo.SelectedItem;
                ScheduleSpec schedule = ReadSchedule();
                bool allowSharedKill = ConfirmSharedKill(selectedService,
                    selectedOperation.Operation);
                if (OperationInfo.UsesKill(selectedOperation.Operation) &&
                    (selectedService.IsSharedProcess || !selectedService.ProcessTypeKnown) &&
                    !allowSharedKill) return;

                Mouse.OverrideCursor = Cursors.Wait;
                string executor = ExecutorDeployer.EnsureProtectedExecutor();
                string path = Scheduler.SaveTask(originalTask == null ? null : originalTask.Name,
                    selectedService.Name, selectedOperation.Operation, schedule,
                    disabledCheck.IsChecked == true, allowSharedKill, executor);
                string migrationWarning = null;
                try
                {
                    int updated = Scheduler.UpdateExecutorForOwnTasks(executor);
                    if (updated > 0)
                        Logger.Info("Обновлён исполнитель существующих заданий: " + updated);
                }
                catch (Exception migrationError)
                {
                    migrationWarning = UiFactory.ErrorText(migrationError);
                    Logger.Warning("Задание сохранено, но старые исполнители не обновлены: " +
                        migrationWarning);
                }
                Logger.Info((originalTask == null ? "Задание создано" : "Задание изменено") +
                    ". Операция: " + OperationInfo.ToRussian(selectedOperation.Operation),
                    path, selectedService.Name);
                Mouse.OverrideCursor = null;
                if (!string.IsNullOrEmpty(migrationWarning))
                    MessageBox.Show(this,
                        "Задание сохранено, но исполнитель старых заданий не обновлён:\n" +
                        migrationWarning, "Задание сохранено", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, UiFactory.ErrorText(ex), "Не удалось сохранить задание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { Mouse.OverrideCursor = null; }
        }
    }

    public sealed class RunningStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
            CultureInfo culture)
        {
            return value is bool && (bool)value ? UiFactory.Brush("#10B981") :
                UiFactory.Brush("#9CA3AF");
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
