// Presets page: controller tiles, category chips, a grid of live preset cards, and the bottom bar
// (colors A/B with quick colors, brightness, speed). Follows changes made elsewhere (hotkeys, tray, game profiles).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glowstick
{
    internal class PresetsPage : UserControl
    {
        const double CardMin = 170, Gap = 8;

        public event Action<string> TileClicked;               // controller id
        public event Action NewRequested, ImportRequested;
        public event Action<Preset> EditRequested, DeleteRequested, ExportRequested;   // the user's own presets (Pro)
        public event Action<Preset> CopyRequested;   // a copy of a built-in preset to edit (Pro)

        public readonly TileRow Tiles = new TileRow();
        readonly WrapPanel chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
        readonly UniformGrid grid = new UniformGrid { VerticalAlignment = VerticalAlignment.Top };
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly List<PresetCard> cards = new List<PresetCard>();
        readonly Slider bright = new Slider { Minimum = 0, Maximum = 100 }, speed = new Slider { Minimum = 10, Maximum = 400 };
        readonly TextBlock lblBright = new TextBlock(), lblSpeed = new TextBlock();
        readonly Slider music = new Slider { Minimum = 50, Maximum = 300 };   // music reaction (Pro, with a music preset)
        readonly TextBlock lblMusic = new TextBlock();
        readonly Grid musicRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        readonly ComboBox band = new ComboBox();   // which frequencies music presets follow (app-wide; a preset can have its own)
        static readonly string[] Bands = { "bass", "mid", "high", "all" };
        readonly TextBlock ownNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };   // the chosen preset has its own music settings
        readonly AnimClock clock = new AnimClock();
        readonly string group = "chips" + Guid.NewGuid().ToString("N");

        List<Preset> shownList;   // the Engine.Presets list the page was built from (replaced as a whole on changes)
        string category;
        int shownIndex = -1;

        public PresetsPage()
        {
            DockPanel root = new DockPanel { Margin = new Thickness(28, 20, 28, 18) };

            StackPanel top = new StackPanel();
            top.Children.Add(new TextBlock { Text = Lang.T("Presets"), Style = Theme.Style("PageTitle") });
            Tiles.TileClicked += id => { if (TileClicked != null) TileClicked(id); };
            top.Children.Add(Tiles);
            top.Children.Add(chips);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            FrameworkElement bottom = BuildBottom();
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            scroll.Content = grid;
            scroll.SizeChanged += (s, e) => LayoutGrid();
            root.Children.Add(scroll);
            Content = root;

            Rebuild();
        }

        // Bottom bar: brightness and speed side by side
        FrameworkElement BuildBottom()
        {
            Border card = new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(16, 10, 16, 10) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel left = new StackPanel(), right = new StackPanel();
            left.Children.Add(lblBright); left.Children.Add(bright);
            right.Children.Add(lblSpeed); right.Children.Add(speed);
            Grid.SetColumn(right, 2);
            g.Children.Add(left); g.Children.Add(right);
            // Music (Pro, with a music preset): the reaction strength and the frequencies, side by side like the row above
            musicRow.ColumnDefinitions.Add(new ColumnDefinition());
            musicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            musicRow.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel mLeft = new StackPanel(), mRight = new StackPanel();
            mLeft.Children.Add(lblMusic); mLeft.Children.Add(music);
            mRight.Children.Add(new TextBlock { Text = Lang.T("Music presets follow:") });
            foreach (string b in Bands) band.Items.Add(MusicBands.Title(b));
            band.SelectedIndex = Math.Max(0, Array.IndexOf(Bands, Live.MusicBand));
            band.SelectionChanged += (s, e) => { if (band.SelectedIndex >= 0) { Live.MusicBand = Bands[band.SelectedIndex]; Settings.Save(); } };
            band.Margin = new Thickness(0, 4, 0, 0);
            mRight.Children.Add(band);
            Grid.SetColumn(mRight, 2);
            musicRow.Children.Add(mLeft); musicRow.Children.Add(mRight);
            Grid.SetRow(musicRow, 1); Grid.SetColumnSpan(musicRow, 3);
            ownNote.Style = Theme.Style("MutedText");
            Grid.SetRow(ownNote, 2); Grid.SetColumnSpan(ownNote, 3);
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(musicRow); g.Children.Add(ownNote);

            bright.Value = Math.Round(Engine.Brightness * 100);
            speed.Value = Math.Round(Engine.Speed * 100);
            music.Value = Math.Round(Live.MusicReaction * 100);
            bright.ValueChanged += (s, e) => { Engine.Brightness = Math.Round(bright.Value) / 100.0; LabelSliders(); };
            speed.ValueChanged += (s, e) => { Engine.Speed = Math.Round(speed.Value) / 100.0; LabelSliders(); };
            music.ValueChanged += (s, e) => { Live.MusicReaction = Math.Round(music.Value) / 100.0; LabelSliders(); };
            // Save when the drag ends, not on every step
            bright.PreviewMouseUp += (s, e) => Settings.Save();
            speed.PreviewMouseUp += (s, e) => Settings.Save();
            music.PreviewMouseUp += (s, e) => Settings.Save();
            LabelSliders();
            ShowMusic();

            card.Child = g;
            return card;
        }

        void LabelSliders()
        {
            lblBright.Text = Lang.F("Brightness: {0}%", (int)Math.Round(bright.Value));
            lblSpeed.Text = Lang.F("Animation speed: {0}%", (int)Math.Round(speed.Value));
            lblMusic.Text = Lang.F("Music reaction: {0}% (more — only the hits light up)", (int)Math.Round(music.Value));
        }

        // The music settings: only with a music preset chosen, and only in Pro (music presets are Pro). The sliders here are
        // the app-wide values; an own preset's ones (set in its editor) win — the note under them says which.
        void ShowMusic()
        {
            List<Preset> ps = Engine.Presets;
            Preset cur = ps[Math.Max(0, Math.Min(Engine.PresetIndex, ps.Count - 1))];
            musicRow.Visibility = Pro.Unlocked && cur.Music ? Visibility.Visible : Visibility.Collapsed;
            PresetTune t = Pro.Unlocked ? PresetTunes.Get(cur.Name) : null;
            string note = "";
            if (t != null)
            {
                List<string> own = new List<string>();
                if (!double.IsNaN(t.Reaction)) own.Add(Lang.F("music reaction {0}%", (int)Math.Round(t.Reaction * 100)));
                if (t.Band != "") own.Add(t.Band == "custom" ? Lang.F("{0}–{1} Hz", (int)t.Low, (int)t.High) : MusicBands.Title(t.Band));
                note = Lang.F("“{0}” has its own music settings: {1}. They win over the sliders here; edit the preset to change them.", cur.Title, string.Join(", ", own));
            }
            if (ownNote.Text != note) ownNote.Text = note;
            ownNote.Visibility = note == "" ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- chips and grid ----------
        public void Rebuild()
        {
            shownList = Engine.Presets;
            if (Engine.PresetIndex >= shownList.Count) Engine.SetPreset(0);
            List<string> cats = UiFormat.Categories(shownList);
            if (category == null || !cats.Contains(category)) category = shownList[Engine.PresetIndex].Category;

            chips.Children.Clear();
            foreach (string cat in cats)
            {
                string c = cat;
                // A category with nothing usable without Pro gets a lock (My presets, Gradients, Live)
                bool locked = c == Pro.MyPresets ? !Pro.Unlocked : !shownList.Exists(p => p.Category == c && !p.Locked);
                string icon = locked ? "🔒 " : c == Pro.MyPresets ? "★ " : "";
                RadioButton rb = new RadioButton { Style = Theme.Style("Chip"), GroupName = group, Content = icon + Lang.T(c), Tag = c };
                rb.IsChecked = c == category;
                rb.Checked += (s, e) => ShowCategory(c);
                chips.Children.Add(rb);
            }
            ShowCategory(category);
        }

        void ShowCategory(string cat)
        {
            category = cat;
            foreach (RadioButton rb in chips.Children) if ((string)rb.Tag == cat && rb.IsChecked != true) rb.IsChecked = true;
            grid.Children.Clear(); cards.Clear();
            for (int k = 0; k < shownList.Count; k++)
            {
                if (shownList[k].Category != cat) continue;
                PresetCard card = new PresetCard(k, shownList[k]) { Margin = new Thickness(0, 0, Gap, Gap) };
                card.Picked += c => Pick(c.Index);
                card.EditRequested += c => { if (EditRequested != null) EditRequested(c.Preset); };
                card.DeleteRequested += c => { if (DeleteRequested != null) DeleteRequested(c.Preset); };
                card.ExportRequested += c => { if (ExportRequested != null) ExportRequested(c.Preset); };
                card.CopyRequested += c => { if (CopyRequested != null) CopyRequested(c.Preset); };
                cards.Add(card);
                grid.Children.Add(card);
            }
            if (cat == Pro.MyPresets) { grid.Children.Add(NewCard()); grid.Children.Add(ImportCard()); }   // locked without Pro
            shownIndex = -1;
            Highlight();
            LayoutGrid();
            scroll.ScrollToTop();
        }

        FrameworkElement NewCard()
        {
            Border b = new Border
            {
                Style = Theme.Style("CardBox"), Background = Brushes.Transparent, Margin = new Thickness(0, 0, Gap, Gap), Cursor = Cursors.Hand,
                ToolTip = Lang.T("Your own preset: effect, colors, speed. It will appear under “My presets”."),
                Child = new TextBlock { Text = Lang.T("+ New preset"), Foreground = Theme.Brush("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            b.Height = 56;   // same as the preset cards
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60)) { };
            b.MouseLeftButtonUp += (s, e) => { if (Pro.Unlocked && NewRequested != null) NewRequested(); };
            return LockIfNoPro(b);
        }

        // Without Pro: the same card, dimmed, with a lock; a click leads to Settings → Glowstick Pro
        static FrameworkElement LockIfNoPro(Border b)
        {
            if (Pro.Unlocked) return b;
            b.Opacity = 0.5;
            LockedPages.ToPro(b);
            b.ToolTip = Lang.T("Available in Glowstick Pro");
            TextBlock t = b.Child as TextBlock;
            if (t != null) t.Text = "🔒 " + t.Text;
            return b;
        }

        FrameworkElement ImportCard()
        {
            Border b = new Border
            {
                Style = Theme.Style("CardBox"), Background = Brushes.Transparent, Margin = new Thickness(0, 0, Gap, Gap), Cursor = Cursors.Hand,
                ToolTip = Lang.T("Load a preset shared as a .glowstick file."),
                Child = new TextBlock { Text = Lang.T("↓ Import…"), Foreground = Theme.Brush("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            b.Height = 56;
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60));
            b.MouseLeftButtonUp += (s, e) => { if (Pro.Unlocked && ImportRequested != null) ImportRequested(); };
            return LockIfNoPro(b);
        }

        void LayoutGrid()
        {
            double w = scroll.ActualWidth - 10;   // room for the scroll bar
            int cols = Math.Max(2, (int)((w + Gap) / (CardMin + Gap)));
            if (grid.Columns != cols) grid.Columns = cols;
        }

        void Pick(int index)
        {
            if (shownList[index].Locked) { UiApp.Main.ShowPro(); return; }   // a Pro preset without Pro → where Pro is bought
            PickUnlocked(index);
        }

        void PickUnlocked(int index)
        {
            Engine.SetPreset(index);
            Settings.Save();
            Highlight();
        }

        void Highlight()
        {
            if (shownIndex == Engine.PresetIndex) return;
            shownIndex = Engine.PresetIndex;
            foreach (PresetCard c in cards) c.Selected = c.Index == shownIndex;
        }

        // Follow changes made elsewhere: the preset list was replaced, the preset changed (hotkey, tray, profile), brightness
        public void SyncSelection()
        {
            if (!ReferenceEquals(shownList, Engine.Presets)) Rebuild();
            ShowMusic();
            if (shownIndex != Engine.PresetIndex)
            {
                string cat = Engine.Presets[Math.Min(Engine.PresetIndex, Engine.Presets.Count - 1)].Category;
                if (cat != category) ShowCategory(cat); else Highlight();
            }
            if (!bright.IsMouseCaptureWithin)
            {
                double b = Math.Round(Engine.Brightness * 100);
                if (Math.Abs(bright.Value - b) >= 1) bright.Value = b;
            }
        }

        public void OnTick(ControllerInfo[] snap)
        {
            SyncSelection();
            Tiles.Update(snap);
            double t = clock.Now(Engine.Speed);
            foreach (PresetCard c in cards) c.Animate(t);
        }
    }
}
