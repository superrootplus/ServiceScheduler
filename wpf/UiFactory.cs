using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceScheduler
{
    public static class UiFactory
    {
        public static readonly Brush WindowBackground = Brush("#F3F4F6");
        public static readonly Brush CardBackground = Brushes.White;
        public static readonly Brush HeaderBackground = Brush("#172554");
        public static readonly Brush HeaderSubtext = Brush("#BFDBFE");
        public static readonly Brush Text = Brush("#111827");
        public static readonly Brush MutedText = Brush("#6B7280");
        public static readonly Brush DefaultBorderBrush = Brush("#D1D5DB");

        public const string Blue = "#2563EB";
        public const string BlueHover = "#1D4ED8";
        public const string Green = "#059669";
        public const string GreenHover = "#047857";
        public const string Red = "#DC2626";
        public const string RedHover = "#B91C1C";
        public const string Orange = "#D97706";
        public const string OrangeHover = "#B45309";
        public const string Gray = "#4B5563";
        public const string GrayHover = "#374151";

        public static Brush Brush(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static Border Header(string title, string subtitle)
        {
            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = HeaderSubtext,
                FontSize = 13,
                Margin = new Thickness(0, 3, 0, 0)
            });
            return new Border
            {
                Background = HeaderBackground,
                Padding = new Thickness(18, 12, 18, 12),
                Child = content
            };
        }

        public static Button IconButton(string glyph, string tooltip, string color,
            string hoverColor, RoutedEventHandler click)
        {
            Button button = new Button
            {
                Width = 48,
                Height = 44,
                Margin = new Thickness(4, 0, 4, 0),
                Padding = new Thickness(0),
                Background = Brush(color),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = new TextBlock { Text = tooltip, FontSize = 14, Padding = new Thickness(4) },
                Content = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 23,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            button.Template = RoundedTemplate(Brush(color), Brush(hoverColor));
            if (click != null) button.Click += click;
            return button;
        }

        public static Button TextButton(string text, string color, string hoverColor,
            RoutedEventHandler click, bool secondary)
        {
            Button button = new Button
            {
                Content = text,
                MinWidth = 116,
                Height = 40,
                Margin = new Thickness(5, 0, 5, 0),
                Padding = new Thickness(18, 0, 18, 0),
                Background = secondary ? Brush("#E5E7EB") : Brush(color),
                Foreground = secondary ? Text : Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            button.Template = RoundedTemplate(
                secondary ? Brush("#E5E7EB") : Brush(color),
                secondary ? Brush("#D1D5DB") : Brush(hoverColor));
            if (click != null) button.Click += click;
            return button;
        }

        private static ControlTemplate RoundedTemplate(Brush normal, Brush hover)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), "bd");
            border.SetValue(Border.BackgroundProperty, normal);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(4));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(content);
            template.VisualTree = border;

            Trigger mouseOver = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };
            mouseOver.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bd"));
            template.Triggers.Add(mouseOver);

            Trigger pressed = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            pressed.Setters.Add(new Setter(Border.OpacityProperty, 0.75, "bd"));
            template.Triggers.Add(pressed);

            Trigger disabled = new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false
            };
            disabled.Setters.Add(new Setter(Border.OpacityProperty, 0.4, "bd"));
            template.Triggers.Add(disabled);
            return template;
        }

        public static TextBlock Label(string text, bool muted)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                Foreground = muted ? MutedText : Text,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBox Input(string text, int width)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                Width = width,
                Height = 34,
                FontSize = 14,
                Padding = new Thickness(7, 4, 7, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static ComboBox Combo(int width)
        {
            return new ComboBox
            {
                Width = width,
                Height = 36,
                FontSize = 14,
                Padding = new Thickness(6, 3, 6, 3)
            };
        }

        public static Border Card(UIElement child, Thickness padding)
        {
            return new Border
            {
                Background = CardBackground,
                BorderBrush = Brush("#E5E7EB"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = padding,
                Child = child
            };
        }

        public static void ShowTextWindow(Window owner, string title, string text)
        {
            Window window = new Window
            {
                Title = title,
                Owner = owner,
                Width = 900,
                Height = 680,
                MinWidth = 650,
                MinHeight = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = WindowBackground,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14
            };
            DockPanel dock = new DockPanel();
            Button close = TextButton("Закрыть", Gray, GrayHover,
                delegate { window.Close(); }, true);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(12);
            DockPanel.SetDock(close, Dock.Bottom);
            dock.Children.Add(close);
            TextBox box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                Foreground = Text,
                FontSize = 14,
                Padding = new Thickness(14),
                Margin = new Thickness(12, 12, 12, 0),
                BorderThickness = new Thickness(0)
            };
            dock.Children.Add(box);
            window.Content = dock;
            window.ShowDialog();
        }

        public static string ErrorText(Exception ex)
        {
            Exception current = ex;
            while (current.InnerException != null &&
                !(current is ServiceSchedulerException)) current = current.InnerException;
            return current.Message;
        }
    }
}
