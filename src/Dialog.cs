using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NoodlePet
{
    // 自建對話框（不用系統訊息框）：Enter = 主要按鈕、Esc = 取消
    public static class Dialog
    {
        public static bool Confirm(Window owner, string title, string message, UIElement extra, string ok, string cancel, bool danger)
        {
            var w = new Window
            {
                Owner = owner,
                Title = Program.AppName,
                Width = 640,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Background = Theme.Brush("canvas"),
                ShowInTaskbar = owner == null,
                UseLayoutRounding = true,
            };
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = title, Style = Theme.Style("t-card-title") });
            if (!string.IsNullOrEmpty(message))
                panel.Children.Add(new TextBlock { Text = message, Style = Theme.Style("t-body-sm-muted"), Margin = new Thickness(0, 8, 0, 0) });
            if (extra != null)
            {
                var fe = extra as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(0, 16, 0, 0);
                panel.Children.Add(extra);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            bool result = false;
            if (cancel != null)
            {
                var no = new Button { Content = cancel, Style = Theme.Style("button-secondary"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
                no.Click += (s, e) => w.Close();
                buttons.Children.Add(no);
            }
            var yes = new Button { Content = ok, Style = Theme.Style(danger ? "button-danger" : "button-primary"), IsDefault = true, IsCancel = cancel == null };
            yes.Click += (s, e) => { result = true; w.Close(); };
            buttons.Children.Add(yes);
            panel.Children.Add(buttons);
            w.Content = panel;
            w.SourceInitialized += (s, e) => Native.DarkTitleBar(new WindowInteropHelper(w).Handle);
            w.Loaded += (s, e) => yes.Focus();
            w.ShowDialog();
            return result;
        }

        public static void Info(Window owner, string title, string message)
        {
            Confirm(owner, title, message, null, "知道了", null, false);
        }

        // 差異檢視：只顯示改動和前後 3 行，其餘收成「⋯」
        public static UIElement DiffView(List<KeyValuePair<char, string>> diff, string path)
        {
            var lines = new StackPanel();
            var show = new bool[diff.Count];
            for (int i = 0; i < diff.Count; i++)
                if (diff[i].Key != ' ')
                    for (int k = Math.Max(0, i - 3); k <= Math.Min(diff.Count - 1, i + 3); k++) show[k] = true;
            bool gap = false;
            for (int i = 0; i < diff.Count; i++)
            {
                if (!show[i]) { gap = true; continue; }
                if (gap) { lines.Children.Add(Line(' ', "⋯")); gap = false; }
                lines.Children.Add(Line(diff[i].Key, diff[i].Value));
            }
            if (gap) lines.Children.Add(Line(' ', "⋯"));
            var scroll = new ScrollViewer { Content = lines, MaxHeight = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 8, 0, 8) };
            AutomationProperties.SetName(scroll, "設定檔差異");
            var box = new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Child = scroll };
            var wrap = new StackPanel();
            wrap.Children.Add(new TextBlock { Text = path, Style = Theme.Style("t-caption"), Margin = new Thickness(0, 0, 0, 8) });
            wrap.Children.Add(box);
            return wrap;
        }

        static FrameworkElement Line(char kind, string text)
        {
            var g = new Grid { Background = kind == '+' ? Theme.Brush("success-tint") : kind == '-' ? Theme.Brush("danger-tint") : Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = new TextBlock { Text = kind == '+' ? "+" : kind == '-' ? "−" : "", Style = Theme.Style("t-mono"), TextAlignment = TextAlignment.Center,
                Foreground = Theme.Brush(kind == '+' ? "semantic-success" : kind == '-' ? "semantic-danger" : "ink-subtle") };
            var t = new TextBlock { Text = text, Style = Theme.Style("t-mono"), Margin = new Thickness(0, 0, 12, 0),
                Foreground = Theme.Brush(kind == ' ' ? "ink-subtle" : "ink") };
            Grid.SetColumn(t, 1);
            g.Children.Add(mark);
            g.Children.Add(t);
            return g;
        }
    }
}
