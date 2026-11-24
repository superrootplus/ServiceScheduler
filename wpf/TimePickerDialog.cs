using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ServiceScheduler
{
    public sealed class TimePickerDialog : Window
    {
        private const double Center = 140;
        private int hour;
        private int minute;
        private bool selectingMinutes;
        private Canvas face;
        private Line hand;
        private TextBlock digital;
        private TextBlock modeText;
        private Button phaseButton;

        public TimeSpan SelectedTime { get; private set; }

        public TimePickerDialog(Window owner, TimeSpan initial)
        {
            Owner = owner;
            Title = "Выбор времени";
            Width = 390;
            Height = 570;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = UiFactory.WindowBackground;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            ShowInTaskbar = false;
            hour = initial.Hours;
            minute = initial.Minutes;

            DockPanel root = new DockPanel();
            Border header = UiFactory.Header("Время запуска",
                "Выберите часы и минуты на циферблате");
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Border buttons = new Border
            {
                Background = Brushes.White,
                BorderBrush = UiFactory.DefaultBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12)
            };
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button cancel = UiFactory.TextButton("Отмена", UiFactory.Gray,
                UiFactory.GrayHover, delegate { DialogResult = false; }, true);
            Button ok = UiFactory.TextButton("Применить", UiFactory.Blue,
                UiFactory.BlueHover, delegate
                {
                    SelectedTime = new TimeSpan(hour, minute, 0);
                    DialogResult = true;
                }, false);
            ok.IsDefault = true;
            cancel.IsCancel = true;
            buttonPanel.Children.Add(cancel);
            buttonPanel.Children.Add(ok);
            buttons.Child = buttonPanel;
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            StackPanel content = new StackPanel();
            digital = new TextBlock
            {
                FontSize = 34,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiFactory.Brush("#1E3A8A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 6)
            };
            content.Children.Add(digital);

            StackPanel modes = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Button hours = new Button { Content = "Часы", Margin = new Thickness(3) };
            hours.Click += delegate { selectingMinutes = false; DrawFace(); };
            Button minutes = new Button { Content = "Минуты", Margin = new Thickness(3) };
            minutes.Click += delegate { selectingMinutes = true; DrawFace(); };
            phaseButton = new Button { Margin = new Thickness(3), ToolTip =
                "Переключить первую и вторую половину суток" };
            phaseButton.Click += delegate
            {
                hour = (hour % 12) + (hour >= 12 ? 0 : 12);
                UpdateDisplay();
            };
            modes.Children.Add(hours);
            modes.Children.Add(minutes);
            modes.Children.Add(phaseButton);
            content.Children.Add(modes);

            modeText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = UiFactory.MutedText,
                Margin = new Thickness(0, 0, 0, 3)
            };
            content.Children.Add(modeText);

            face = new Canvas
            {
                Width = 280,
                Height = 280,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            face.MouseLeftButtonUp += FaceClicked;
            content.Children.Add(face);
            root.Children.Add(content);
            Content = root;
            DrawFace();
        }

        private void FaceClicked(object sender, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(face);
            double dx = point.X - Center;
            double dy = point.Y - Center;
            if (Math.Sqrt(dx * dx + dy * dy) > 137) return;
            double angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
            if (angle < 0) angle += 360;
            if (selectingMinutes)
            {
                minute = ((int)Math.Round(angle / 6)) % 60;
            }
            else
            {
                int hourOnDial = ((int)Math.Round(angle / 30)) % 12;
                hour = HourFromDial(hour, hourOnDial);
                selectingMinutes = true;
            }
            DrawFace();
        }

        internal static int HourFromDial(int currentHour, int hourOnDial)
        {
            // На циферблате верхнее деление означает 12 часов, а не 00.
            // Полночь по-прежнему доступна переключателем половины суток.
            return hourOnDial == 0 ? 12 : hourOnDial + (currentHour >= 12 ? 12 : 0);
        }

        private void DrawFace()
        {
            face.Children.Clear();
            face.Children.Add(new Ellipse
            {
                Width = 272,
                Height = 272,
                Fill = Brushes.White,
                Stroke = UiFactory.Brush("#CBD5E1"),
                StrokeThickness = 2
            });
            Canvas.SetLeft(face.Children[0], 4);
            Canvas.SetTop(face.Children[0], 4);

            for (int tick = 0; tick < 60; tick++)
            {
                double angle = tick * Math.PI / 30;
                bool major = tick % 5 == 0;
                double inner = major ? 119 : 125;
                Line line = new Line
                {
                    X1 = Center + Math.Sin(angle) * inner,
                    Y1 = Center - Math.Cos(angle) * inner,
                    X2 = Center + Math.Sin(angle) * 131,
                    Y2 = Center - Math.Cos(angle) * 131,
                    Stroke = major ? UiFactory.Brush("#64748B") : UiFactory.Brush("#CBD5E1"),
                    StrokeThickness = major ? 2 : 1
                };
                face.Children.Add(line);
            }

            for (int index = 0; index < 12; index++)
            {
                int value = selectingMinutes ? index * 5 : (index == 0 ? 12 : index);
                double angle = index * Math.PI / 6;
                TextBlock label = new TextBlock
                {
                    Text = selectingMinutes ? value.ToString("00") : value.ToString(),
                    Width = 36,
                    Height = 30,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = UiFactory.Text
                };
                Canvas.SetLeft(label, Center + Math.Sin(angle) * 96 - 18);
                Canvas.SetTop(label, Center - Math.Cos(angle) * 96 - 15);
                face.Children.Add(label);
            }

            double handAngle = selectingMinutes ? minute * Math.PI / 30 :
                (hour % 12) * Math.PI / 6 + minute * Math.PI / 360;
            double length = selectingMinutes ? 88 : 65;
            hand = new Line
            {
                X1 = Center,
                Y1 = Center,
                X2 = Center + Math.Sin(handAngle) * length,
                Y2 = Center - Math.Cos(handAngle) * length,
                Stroke = UiFactory.Brush(UiFactory.Blue),
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            face.Children.Add(hand);
            Ellipse centerDot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = UiFactory.Brush(UiFactory.Blue),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(centerDot, Center - 6);
            Canvas.SetTop(centerDot, Center - 6);
            face.Children.Add(centerDot);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            digital.Text = string.Format("{0:00}:{1:00}", hour, minute);
            modeText.Text = selectingMinutes ?
                "Минуты: щёлкните по нужному делению" :
                "Часы: щёлкните по числу, затем выберите минуты";
            phaseButton.Content = hour >= 12 ? "12–23" : "00–11";
        }
    }
}
