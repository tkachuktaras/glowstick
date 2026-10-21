// Main window: sidebar with pages, the game mode card at its bottom, a page host and an overlay layer for in-window dialogs.
// A 40 ms timer drives everything live (light bar colors, battery, status) while the window is visible.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Glowstick
{
    internal class MainWindow : Window
    {
        public enum Page { Presets, Controllers, Profiles, Test, Settings }

        // Fires every 40 ms while the window is visible (not minimized, not hidden in the tray)
        public event Action Tick;

        readonly Dictionary<Page, RadioButton> nav = new Dictionary<Page, RadioButton>();
        readonly Dictionary<Page, FrameworkElement> pages = new Dictionary<Page, FrameworkElement>();
        readonly ContentControl host = new ContentControl();
        readonly Grid overlay = new Grid();
        readonly Border overlayCard = new Border();
        readonly ScrollViewer overlayScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Ellipse gameDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0) };
        readonly TextBlock gameStatus = new TextBlock();
        readonly Border gameCard = new Border();
        readonly Button install = new Button();
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        public Page Current { get; private set; }

        public MainWindow()
        {
            Title = "Glowstick";
            Icon = UiApp.IconSource();
            Width = 1040; Height = 720; MinWidth = 900; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = (FontFamily)Theme.Dict["UiFont"]; FontSize = 13; Foreground = Theme.Brush("Text");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Theme.Chrome(this);

            Grid layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.Children.Add(BuildSidebar());

            Border content = new Border { Background = Theme.Layer, CornerRadius = new CornerRadius(8, 0, 0, 0), Child = host };
            Grid.SetColumn(content, 1);
            layout.Children.Add(content);

            // Overlay: dims the window and shows one dialog card in the middle
            overlay.Background = Theme.Brush("Overlay");
            overlay.Visibility = Visibility.Collapsed;
            overlayCard.Style = Theme.Style("CardBox");
            overlayCard.Background = Theme.Brush("Bg");
            overlayCard.Padding = new Thickness(24, 20, 24, 20);
            overlayCard.MaxWidth = 640; overlayCard.MinWidth = 380;
            overlayCard.HorizontalAlignment = HorizontalAlignment.Center; overlayCard.VerticalAlignment = VerticalAlignment.Center;
            overlayCard.Child = overlayScroll;   // a tall dialog (preset editor) scrolls in a small window
            overlay.SizeChanged += (s, e) => overlayCard.MaxHeight = Math.Max(200, e.NewSize.Height - 40);
            overlay.Children.Add(overlayCard);

            Grid root = new Grid();
            root.Children.Add(layout);
            root.Children.Add(overlay);
            Content = root;

            // KeyDown, not PreviewKeyDown: an open drop-down or menu handles Esc itself (closes) and it never reaches the dialog
            KeyDown += (s, e) => { if (e.Key == Key.Escape && overlay.Visibility == Visibility.Visible && OnEscape()) e.Handled = true; };
            // The window goes away with dialogs open (✕, language reload, exit): close them so their cleanup runs —
            // e.g. a color picker puts back the color it was previewing on the controllers
            Closed += (s, e) => { while (dialogs.Count > 0) CloseOverlay(); };
            timer.Tick += (s, e) => OnTick();
            IsVisibleChanged += (s, e) => { if (IsVisible) timer.Start(); else timer.Stop(); };
            Navigate(Page.Presets);
            UpdateGameCard();
        }

        FrameworkElement BuildSidebar()
        {
            DockPanel side = new DockPanel { Background = Theme.Mica ? Brushes.Transparent : Theme.Brush("Side"), Margin = new Thickness(0) };

            // Game mode is always on: the card shows up only when it doesn't work (no admin rights, drivers missing or not loaded)
            gameCard.Style = Theme.Style("CardBox");
            gameCard.Margin = new Thickness(10, 8, 10, 12); gameCard.Padding = new Thickness(12, 10, 12, 12);
            gameCard.BorderBrush = Theme.Brush("StatusBad");
            gameCard.Visibility = Visibility.Collapsed;
            StackPanel cardBody = new StackPanel();
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(gameDot);
            head.Children.Add(new TextBlock { Text = Lang.T("Game mode"), FontWeight = FontWeights.SemiBold });
            cardBody.Children.Add(head);
            gameStatus.Style = Theme.Style("MutedText");
            gameStatus.Margin = new Thickness(0, 4, 0, 0);
            gameStatus.TextWrapping = TextWrapping.Wrap;
            cardBody.Children.Add(gameStatus);
            install.Margin = new Thickness(0, 10, 0, 0);
            install.HorizontalAlignment = HorizontalAlignment.Left;
            install.Style = Theme.Style("AccentButton");
            install.Padding = new Thickness(12, 4, 12, 4);
            install.Click += (s, e) => UiApp.InstallDrivers();
            cardBody.Children.Add(install);
            gameCard.Child = cardBody;
            DockPanel.SetDock(gameCard, Dock.Bottom);
            side.Children.Add(gameCard);

            // Free build: a small "Glowstick Pro" card at the bottom of the sidebar — its perks and a Get Pro button on every page
            FrameworkElement pro = BuildProCard();
            if (pro != null) { DockPanel.SetDock(pro, Dock.Bottom); side.Children.Add(pro); }

            FrameworkElement upd = BuildUpdateCard();
            DockPanel.SetDock(upd, Dock.Top);
            side.Children.Add(upd);

            StackPanel items = new StackPanel { Margin = new Thickness(8, 8, 8, 0) };
            AddNav(items, Page.Presets, "", Lang.T("Presets"));
            AddNav(items, Page.Controllers, "", Lang.T("Controllers"));
            AddNav(items, Page.Profiles, "", Lang.T("Game profiles"));
            AddNav(items, Page.Test, "", Lang.T("Controller test"));
            AddNav(items, Page.Settings, "", Lang.T("Settings"));
            side.Children.Add(items);
            return side;
        }

        // The upgrade card at the bottom of the sidebar (free build only): a few Pro perks + a Get Pro button, seen on every page
        FrameworkElement BuildProCard()
        {
            if (Pro.Unlocked) return null;
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "✦ Glowstick Pro", FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush("Accent"), Margin = new Thickness(0, 0, 0, 8) });
            string[] perks = { Lang.T("All animations & live presets"), Lang.T("Any number of controllers"), Lang.T("Custom presets, hotkeys & API") };
            foreach (string p in perks)
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                row.Children.Add(new TextBlock { Text = "✓", Foreground = Theme.Brush("BatOk"), FontSize = 12, Margin = new Thickness(0, 0, 7, 0) });
                row.Children.Add(new TextBlock { Text = p, Style = Theme.Style("MutedText"), FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 150 });
                body.Children.Add(row);
            }
            FrameworkElement badge = LicenseSettings.GetProBadge(true);
            badge.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(badge);
            TextBlock haveKey = new TextBlock { Text = Lang.T("Have a key? →"), Style = Theme.Style("MutedText"), FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
            haveKey.MouseLeftButtonUp += (s, e) => ShowPro();
            body.Children.Add(haveKey);

            Border card = new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(10, 8, 10, 12), Padding = new Thickness(12, 12, 12, 12), Child = body };
            return card;
        }

        // "Version X is available" with "What's new" and "Update" — shown while a newer release is known
        FrameworkElement BuildUpdateCard()
        {
            Border card = new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(10, 10, 10, 0), Padding = new Thickness(12, 10, 12, 12),
                BorderBrush = Theme.Brush("Accent"), Visibility = Visibility.Collapsed };
            StackPanel p = new StackPanel();
            TextBlock title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            TextBlock note = new TextBlock { Style = Theme.Style("MutedText"), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed,
                Text = Lang.T("Downloading the update…") };
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            Button go = new Button { Content = Lang.T("Update"), Style = Theme.Style("AccentButton"), Padding = new Thickness(12, 4, 12, 4) };
            Button news = new Button { Content = Lang.T("What's new"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
            go.Click += (s, e) => UiApp.InstallUpdate();
            news.Click += (s, e) => { if (UiApp.Update != null && UiApp.Update.PageUrl != null) UiApp.OpenUrl(UiApp.Update.PageUrl); };
            buttons.Children.Add(go); buttons.Children.Add(news);
            p.Children.Add(title); p.Children.Add(note); p.Children.Add(buttons);
            card.Child = p;

            Action refresh = () =>
            {
                UpdateInfo u = UiApp.Update;
                card.Visibility = u != null ? Visibility.Visible : Visibility.Collapsed;
                if (u == null) return;
                title.Text = Lang.F("Version {0} is available", u.Version);
                note.Visibility = UiApp.Updating ? Visibility.Visible : Visibility.Collapsed;
                go.IsEnabled = !UiApp.Updating;
                news.Visibility = u.PageUrl != null ? Visibility.Visible : Visibility.Collapsed;   // the Pro server has no release page
            };
            UiApp.UpdateChanged += refresh;
            Closed += (s, e) => UiApp.UpdateChanged -= refresh;
            refresh();
            return card;
        }

        void AddNav(Panel parent, Page page, string glyph, string text)
        {
            StackPanel c = new StackPanel { Orientation = Orientation.Horizontal };
            c.Children.Add(new TextBlock { Text = glyph, Style = Theme.Style("Icon"), Margin = new Thickness(0, 0, 14, 0) });
            c.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            RadioButton rb = new RadioButton { Style = Theme.Style("NavItem"), Content = c, GroupName = "nav" };
            rb.Checked += (s, e) => Show(page);
            nav[page] = rb;
            parent.Children.Add(rb);
        }

        // Settings were changed from outside the Settings page (first-run setup): rebuild it on the next visit
        public void RefreshSettings()
        {
            if (Current != Page.Settings) pages.Remove(Page.Settings);
        }

        public void Navigate(Page page)
        {
            if (nav[page].IsChecked == true) Show(page);
            else nav[page].IsChecked = true;   // → Checked → Show
        }

        // A click on anything locked: Settings, scrolled to the Glowstick Pro section (key box and the buy button)
        public void ShowPro()
        {
            Navigate(Page.Settings);
            SettingsPage sp = host.Content as SettingsPage;
            if (sp != null) sp.ScrollToPro();
        }

        void Show(Page page)
        {
            FrameworkElement p;
            if (!pages.TryGetValue(page, out p)) { p = BuildPage(page); pages[page] = p; }
            Current = page;
            host.Content = p;
        }

        // Pages are built on first visit
        FrameworkElement BuildPage(Page page)
        {
            string title = page == Page.Presets ? Lang.T("Presets") : page == Page.Controllers ? Lang.T("Controllers")
                : page == Page.Profiles ? Lang.T("Game profiles") : page == Page.Test ? Lang.T("Controller test") : Lang.T("Settings");
            if (page == Page.Presets)
            {
                PresetsPage pp = new PresetsPage();
                pp.TileClicked += OpenPadMenu;
                if (Pro.PresetsPageCreated != null) Pro.PresetsPageCreated(pp, this);   // own presets, export / import
                Tick += () => { if (Current == Page.Presets) pp.OnTick(Engine.Snapshot()); };
                pp.OnTick(Engine.Snapshot());
                return pp;
            }
            if (page == Page.Test)
            {
                TestPage tp = new TestPage();
                Tick += () => { if (Current == Page.Test) tp.OnTick(); };
                tp.OnTick();
                return tp;
            }
            if (page == Page.Settings)
            {
                SettingsPage sp = new SettingsPage();
                Tick += () => { if (Current == Page.Settings) sp.OnTick(); };
                return sp;
            }
            // Pro pages (game profiles): built by the module once it's unlocked, otherwise shown locked
            if (Pro.Unlocked && Pro.BuildPage != null)
            {
                FrameworkElement extra = Pro.BuildPage(page, this);
                if (extra != null) return extra;
            }
            if (page == Page.Profiles) return LockedPages.Profiles(title);
            if (page == Page.Controllers)
            {
                ControllersPage cp = new ControllersPage();
                Tick += () => { if (Current == Page.Controllers) cp.OnTick(Engine.Snapshot()); };
                cp.OnTick(Engine.Snapshot());
                return cp;
            }
            StackPanel s = new StackPanel { Margin = new Thickness(28, 20, 28, 20) };
            s.Children.Add(new TextBlock { Text = title, Style = Theme.Style("PageTitle") });
            return new ScrollViewer { Content = s, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        public void OpenPadMenu(string id)
        {
            ContextMenu m = PadMenu.Build(this, id);
            if (m.Items.Count == 0) return;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        // Dialogs stack: the preset editor can open the color picker on top of itself
        class Dialog { public UIElement Content; public Action OnClose; public bool Modal; }
        readonly List<Dialog> dialogs = new List<Dialog>();

        public UIElement OverlayTop { get { return dialogs.Count > 0 ? dialogs[dialogs.Count - 1].Content : null; } }

        // onClose — called when this dialog goes away in any way (OK, Cancel, Esc).
        // modal — Esc doesn't close it (progress that must not be interrupted).
        public void ShowOverlay(UIElement content, Action onClose = null, bool modal = false)
        {
            dialogs.Add(new Dialog { Content = content, OnClose = onClose, Modal = modal });
            overlayScroll.Content = content;
            overlay.Visibility = Visibility.Visible;
        }

        // Closes the topmost dialog
        public void CloseOverlay() { if (dialogs.Count > 0) Close(dialogs.Count - 1); }

        // Closes this particular dialog, wherever it is in the stack
        public void CloseOverlay(UIElement content)
        {
            int i = dialogs.FindIndex(d => d.Content == content);
            if (i >= 0) Close(i);
        }

        // Esc: closes the topmost dialog unless it's modal. Returns whether something was closed.
        public bool OnEscape()
        {
            if (dialogs.Count == 0 || dialogs[dialogs.Count - 1].Modal) return false;
            CloseOverlay();
            return true;
        }

        void Close(int i)
        {
            Dialog d = dialogs[i];
            dialogs.RemoveAt(i);
            overlayScroll.Content = OverlayTop;
            overlay.Visibility = dialogs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (d.OnClose != null) d.OnClose();
        }

        void OnTick()
        {
            if (WindowState == WindowState.Minimized) return;
            UpdateGameCard();
            if (Tick != null) Tick();
        }

        void UpdateGameCard()
        {
            bool show = !Engine.GameModeWorking && Engine.Status != "";   // no status yet — the engine is still starting
            Visibility v = show ? Visibility.Visible : Visibility.Collapsed;
            if (gameCard.Visibility != v) gameCard.Visibility = v;
            if (!show) return;
            string st = Engine.Status;
            if (gameStatus.Text != st) gameStatus.Text = st;
            gameDot.Fill = Theme.Brush("StatusBad");
            install.Visibility = UiApp.DriversMissing || UiApp.Installing ? Visibility.Visible : Visibility.Collapsed;
            install.IsEnabled = !UiApp.Installing;
            string label = UiApp.Installing ? Lang.T("Installing…") : Lang.T("Install drivers");
            if (!label.Equals(install.Content)) install.Content = label;
        }
    }
}
