// The "Glowstick Pro" section on the Settings page (both builds): enter the key bought on Gumroad, or free this PC's activation.
// The Pro build unlocks right away (the window is rebuilt, so every page shows its Pro parts); the free build then
// downloads the Pro exe from the Pro server and restarts as Pro — the same way as an update.
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Glowstick
{
    internal static class LicenseSettings
    {
        public static void Add(SettingsPage page)
        {
            StackPanel sec = page.Section("Glowstick Pro");
            page.ProSection = sec;
            TextBlock status = new TextBlock { Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.Wrap };
            sec.Children.Add(status);
            TextBlock result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Theme.Brush("Warn"), Visibility = Visibility.Collapsed };
            Action<string> fail = text => { result.Text = text; result.Visibility = Visibility.Visible; };

            if (License.Owned)
            {
                StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
                if (Pro.Present) status.Text = Lang.F("Pro is active on this PC (key {0}).", License.Masked(License.Key));
                else
                {
                    // The key is activated, but this is still the free exe (the download didn't happen or failed)
                    status.Text = Lang.F("The key {0} is activated. Download Glowstick Pro to unlock it.", License.Masked(License.Key));
                    Button get = new Button { Content = Lang.T("Download Glowstick Pro"), Style = Theme.Style("AccentButton"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
                    get.Click += (s, e) => { get.IsEnabled = false; DownloadPro(page, () => get.IsEnabled = true); };
                    buttons.Children.Add(get);
                }
                Button off = new Button { Content = Lang.T("Deactivate on this PC") };
                off.Click += (s, e) => Dialogs.Confirm(UiApp.Main, "Glowstick Pro",
                    Lang.T("Free this PC's activation? Pro gets locked here, and the key can be activated on another PC."), () =>
                    {
                        off.IsEnabled = false;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            License.Deactivate();
                            page.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                Settings.Save();
                                if (Pro.LicenseChanged != null) Pro.LicenseChanged();
                                if (Pro.Present) UiApp.DowngradeToFree();   // back to the free exe (or locked in place until it can)
                                else UiApp.ReloadWindow();
                            }));
                        });
                    });
                buttons.Children.Add(off);
                sec.Children.Add(buttons);
                sec.Children.Add(result);
                return;
            }

            status.Text = Lang.T("Paste your key to unlock Pro.");
            TextBox key = new TextBox { Margin = new Thickness(0, 12, 0, 0), MaxLength = 100, VerticalContentAlignment = VerticalAlignment.Center };
            page.ProKey = key;
            Button go = new Button { Content = Lang.T("Activate"), Style = Theme.Style("AccentButton"), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 12, 0, 0) };
            DockPanel row = new DockPanel();
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            row.Children.Add(key);
            sec.Children.Add(row);
            sec.Children.Add(result);

            Action activate = () =>
            {
                string k = key.Text;
                go.IsEnabled = key.IsEnabled = false;
                result.Visibility = Visibility.Collapsed;
                go.Content = Lang.T("Checking…");
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    LicenseResult r = License.Activate(k);
                    page.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (r == LicenseResult.Ok)
                        {
                            Settings.Save();
                            if (Pro.Present)
                            {
                                if (Pro.LicenseChanged != null) Pro.LicenseChanged();
                                UiApp.ReloadWindow();
                            }
                            else DownloadPro(page, () => UiApp.ReloadWindow());   // the free exe turns into Pro
                            return;
                        }
                        go.IsEnabled = key.IsEnabled = true;
                        go.Content = Lang.T("Activate");
                        fail(Message(r));
                    }));
                });
            };
            go.Click += (s, e) => activate();
            key.KeyDown += (s, e) => { if (e.Key == Key.Enter && go.IsEnabled) activate(); };
        }

        // A bright gradient "Get Glowstick Pro" badge (the light-bar colors) that opens the buy page. stretch — fill the width.
        public static FrameworkElement GetProBadge(bool stretch = false)
        {
            LinearGradientBrush grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0x1E, 0x6A), 0));
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x1B, 0x8F, 0xD6), 1));
            StackPanel inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            inner.Children.Add(new TextBlock { Text = "✦", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            inner.Children.Add(new TextBlock { Text = Lang.T("Get Glowstick Pro"), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Border badge = new Border
            {
                Background = grad, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 7, 16, 7), Cursor = Cursors.Hand,
                Child = inner, ToolTip = Lang.T("Opens the Glowstick Pro page in your browser"),
                HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0.35, Color = Color.FromRgb(0xE0, 0x1E, 0x6A) }
            };
            badge.MouseEnter += (s, e) => badge.Opacity = 0.9;
            badge.MouseLeave += (s, e) => badge.Opacity = 1;
            badge.MouseLeftButtonUp += (s, e) => UiApp.OpenUrl(Pro.BuyUrl);
            return badge;
        }

        // Free build with an activated key: ask the Pro server for the Pro exe and install it like an update.
        // On failure failed() runs first (it may rebuild the window), then the reason is shown in a dialog.
        static void DownloadPro(SettingsPage page, Action failed)
        {
            Action<string> fail = text => Dialogs.Info(UiApp.Main, "Glowstick Pro", text);
            if (!ProServer.Available)
            {
                failed();
                fail(Lang.T("The key is activated, but Glowstick Pro can't be downloaded yet. Try again in a day or two."));
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                UpdateInfo u = null; string err = null;
                try { u = ProServer.Latest(null); if (u == null) err = Lang.T("The Pro server didn't offer a download."); }
                catch (Exception e) { Log.Error("pro download", e); err = e.Message; }
                page.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (u != null) { UiApp.InstallPro(u); return; }   // the update card shows the progress, then Glowstick restarts
                    failed();
                    fail(Lang.F("Couldn't download Glowstick Pro: {0}", err));
                }));
            });
        }

        static string Message(LicenseResult r)
        {
            switch (r)
            {
                case LicenseResult.LimitReached:
                    return Lang.T("This key can't be activated here: it's already active on the maximum number of PCs, or it was revoked. Deactivate it on another PC (Settings → Glowstick Pro) and try again.");
                case LicenseResult.Offline:
                    return Lang.T("Couldn't reach the license server. Check the internet connection and try again.");
                default:
                    return Lang.T("This key wasn't found. Copy it from the purchase email exactly as it is.");
            }
        }
    }
}
