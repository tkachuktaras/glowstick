// In-window dialogs on MainWindow's overlay (no separate windows, no MessageBox): text input, yes/no, information.
// Enter — OK, Esc — cancel (MainWindow closes the overlay on Esc).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Glowstick
{
    internal static class Dialogs
    {
        static StackPanel Frame(string title, string text)
        {
            StackPanel p = new StackPanel();
            p.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            if (!string.IsNullOrEmpty(text)) p.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            return p;
        }

        static StackPanel Buttons(params Button[] buttons)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            foreach (Button b in buttons) { b.MinWidth = 100; b.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(b); }
            return row;
        }

        public static void Prompt(MainWindow w, string title, string label, string value, int maxLength, Action<string> ok)
        {
            StackPanel p = Frame(title, label);
            TextBox box = new TextBox { Text = value ?? "", MaxLength = maxLength };
            p.Children.Add(box);
            Button bOk = new Button { Content = "OK", Style = Theme.Style("AccentButton"), IsDefault = true };
            Button bCancel = new Button { Content = Lang.T("Cancel"), IsCancel = true };
            bOk.Click += (s, e) => { w.CloseOverlay(); ok(box.Text); };
            bCancel.Click += (s, e) => w.CloseOverlay();
            p.Children.Add(Buttons(bOk, bCancel));
            w.ShowOverlay(p);
            Focus(box, true);
        }

        public static void Confirm(MainWindow w, string title, string text, Action yes)
        {
            StackPanel p = Frame(title, text);
            Button bYes = new Button { Content = Lang.T("Yes"), Style = Theme.Style("AccentButton"), IsDefault = true };
            Button bNo = new Button { Content = Lang.T("No"), IsCancel = true };
            bYes.Click += (s, e) => { w.CloseOverlay(); yes(); };
            bNo.Click += (s, e) => w.CloseOverlay();
            p.Children.Add(Buttons(bYes, bNo));
            w.ShowOverlay(p);
            Focus(bNo, false);
        }

        // closed — after it goes away (OK or Esc)
        public static void Info(MainWindow w, string title, string text, Action closed = null)
        {
            StackPanel p = Frame(title, text);
            Button bOk = new Button { Content = "OK", Style = Theme.Style("AccentButton"), IsDefault = true, IsCancel = true };
            bOk.Click += (s, e) => w.CloseOverlay();
            p.Children.Add(Buttons(bOk));
            w.ShowOverlay(p, closed);
            Focus(bOk, false);
        }

        // After the overlay is laid out
        static void Focus(Control c, bool selectAll)
        {
            c.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                c.Focus(); Keyboard.Focus(c);
                TextBox t = c as TextBox;
                if (t != null && selectAll) t.SelectAll();
            }));
        }
    }
}
