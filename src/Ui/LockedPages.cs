// What Pro adds, shown in place but locked — instead of an ad card: the Game profiles page with example rows,
// the battery history chart on a controller's card, the Local API section in Settings. Only the look: the logic is
// in the Pro module. Buying / entering a key happens in one place — Settings → Glowstick Pro: a click on anything
// locked goes there (ToPro).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Glowstick
{
    internal static class LockedPages
    {
        // A click on e opens Settings → Glowstick Pro
        public static T ToPro<T>(T e) where T : FrameworkElement
        {
            e.Cursor = Cursors.Hand;
            e.MouseLeftButtonUp += (s, a) => { if (UiApp.Main != null) UiApp.Main.ShowPro(); };
            return e;
        }

        // Disabled controls don't get clicks: a transparent layer over them does
        static FrameworkElement Covered(FrameworkElement locked)
        {
            Grid g = new Grid();
            g.Children.Add(locked);
            g.Children.Add(ToPro(new Border { Background = Brushes.Transparent, ToolTip = Lang.T("Available in Glowstick Pro") }));
            return g;
        }

        // The "🔒 Glowstick Pro" badge next to a title
        static FrameworkElement Badge()
        {
            return ToPro(new Border
            {
                Background = Theme.Brush("SelCard"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 4),
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("Available in Glowstick Pro"),
                Child = new TextBlock { Text = "🔒 Glowstick Pro", FontSize = 12, Foreground = Theme.Brush("Accent") }
            });
        }

        // Game profiles: the real page's layout with two example rows, everything disabled
        public static FrameworkElement Profiles(string title)
        {
            StackPanel page = new StackPanel { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = title, Style = Theme.Style("PageTitle") });
            FrameworkElement badge = Badge();
            badge.Margin = new Thickness(12, 0, 0, 10);
            head.Children.Add(badge);
            page.Children.Add(head);
            page.Children.Add(new TextBlock { Text = Lang.T("While the game runs — its own preset. Quit the game — everything goes back."),
                Style = Theme.Style("MutedText"), FontSize = 13, Margin = new Thickness(0, -6, 0, 14), TextWrapping = TextWrapping.Wrap });

            StackPanel locked = new StackPanel { IsEnabled = false, Opacity = 0.55 };
            StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            bar.Children.Add(new Button { Content = Lang.T("Add a running game ▾"), Margin = new Thickness(0, 0, 8, 0) });
            bar.Children.Add(new Button { Content = Lang.T("Choose exe…") });
            locked.Children.Add(bar);

            StackPanel table = new StackPanel();
            table.Children.Add(Columns(Head(Lang.T("Game (exe)")), Head(Lang.T("Preset"))));
            string[][] examples = { new[] { "RocketLeague.exe", "Fire" }, new[] { "eldenring.exe", "Thunderstorm" } };
            foreach (string[] ex in examples)
            {
                ComboBox preset = new ComboBox();
                preset.Items.Add(Lang.T(ex[1]));
                preset.SelectedIndex = 0;
                Grid row = Columns(new TextBox { Text = ex[0] }, preset);
                row.Margin = new Thickness(0, 0, 0, 6);
                table.Children.Add(row);
            }
            locked.Children.Add(new Border { Style = Theme.Style("CardBox"), Padding = new Thickness(14, 10, 14, 12), Child = table });
            page.Children.Add(Covered(locked));
            return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        static TextBlock Head(string t) { return new TextBlock { Text = t, Style = Theme.Style("MutedText"), Margin = new Thickness(2, 0, 0, 6) }; }

        static Grid Columns(FrameworkElement a, FrameworkElement b)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            a.Margin = new Thickness(0, a.Margin.Top, 8, a.Margin.Bottom);
            Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
            return g;
        }

        // A controller's card: where the battery history chart is in Pro — an empty chart frame with a lock
        public static FrameworkElement BatteryHistory()
        {
            Grid g = ToPro(new Grid { Height = 96, Margin = new Thickness(0, 12, 0, 0), ToolTip = Lang.T("Available in Glowstick Pro"), Background = Brushes.Transparent });
            g.Children.Add(new Border { Background = Theme.Brush("CardPressed"), CornerRadius = new CornerRadius(8) });
            for (int k = 1; k <= 2; k++)
                g.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(36, 12 + k * 26, 10, 0),
                    Background = Theme.Brush("CardBorder"), Opacity = 0.7 });
            g.Children.Add(new TextBlock { Text = "🔒 " + Lang.T("Battery history — Glowstick Pro"), Style = Theme.Style("MutedText"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        // Settings: the Local API section, disabled
        public static void LocalApi(SettingsPage page)
        {
            StackPanel sec = page.Section("🔒 " + Lang.T("Local API"));
            sec.ToolTip = Lang.T("Available in Glowstick Pro");
            CheckBox c = new CheckBox { Content = new TextBlock { Text = Lang.T("Local API (HTTP) for Stream Deck, Home Assistant, OBS and your own scripts"), TextWrapping = TextWrapping.Wrap },
                IsEnabled = false, Opacity = 0.55 };
            sec.Children.Add(Covered(c));
        }
    }
}
