// Controllers page: one card per connected controller with what the classic window hid in the tile menu —
// name, own preset, find it, turn off, a drifting stick — plus "who is who" and "search again".
// Cards are dragged to change the order.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Glowstick
{
    internal class ControllersPage : UserControl
    {
        readonly StackPanel list = new StackPanel();
        readonly TextBlock none = new TextBlock(), hint = new TextBlock(), steam = new TextBlock();
        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        string shownIds = null;   // order + ids the rows were built for
        List<Preset> shownPresets;

        class Row
        {
            public Border Card;
            public SolidColorBrush Light = new SolidColorBrush(Colors.Black);
            public TextBox Name;
            public ComboBox Preset;
            public TextBlock Info, Warning, Hint;
            public StackPanel Drift;
            public string DriftShown = "";
            public ControllerExtra Extra;
            public BatteryCell Cell;
            public Button Off;
            public bool Updating;
        }

        public ControllersPage()
        {
            StackPanel page = new StackPanel { Margin = new Thickness(28, 20, 28, 20) };
            DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            Button scan = new Button { Content = Lang.T("Search again"), VerticalAlignment = VerticalAlignment.Center };
            scan.Click += (s, e) => Engine.Rescan();
            Button who = new Button { Content = Lang.T("Who is who"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = Lang.T("Every controller blinks its number: the 1st once, the 2nd twice…") };
            who.Click += (s, e) => Engine.Identify();
            StackPanel headButtons = new StackPanel { Orientation = Orientation.Horizontal };
            headButtons.Children.Add(who); headButtons.Children.Add(scan);
            DockPanel.SetDock(headButtons, Dock.Right);
            head.Children.Add(headButtons);
            head.Children.Add(new TextBlock { Text = Lang.T("Controllers"), Style = Theme.Style("PageTitle"), Margin = new Thickness(0) });
            page.Children.Add(head);
            hint.Style = Theme.Style("MutedText");
            hint.Text = Lang.T("Drag a card to change the controller order.");
            hint.Margin = new Thickness(0, -6, 0, 12);
            page.Children.Add(hint);
            steam.Foreground = Theme.Brush("Warn"); steam.TextWrapping = TextWrapping.Wrap; steam.Visibility = Visibility.Collapsed;
            steam.Margin = new Thickness(0, 0, 0, 12);
            page.Children.Add(steam);

            new DragReorder(list, true, el => (string)((FrameworkElement)el).Tag, (id, k) => PadMenu.IfPresent(id, () => Engine.MoveTo(id, k)));

            none.Style = Theme.Style("MutedText"); none.FontSize = 13;
            none.Text = Lang.T("No controllers found.\nConnect a DualShock 4 or DualSense via USB or Bluetooth — it will show up by itself.");
            page.Children.Add(none);
            page.Children.Add(list);
            Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        public void OnTick(ControllerInfo[] snap)
        {
            string sw = snap.Length > 0 ? SteamInput.Warning() : null;
            string swText = sw != null ? "⚠ " + sw : "";
            if (steam.Text != swText) { steam.Text = swText; steam.Visibility = sw == null ? Visibility.Collapsed : Visibility.Visible; }
            string ids = string.Join("|", Array.ConvertAll(snap, c => c.Id)) + "|+" + Engine.OverLimit;
            if (ids != shownIds || !ReferenceEquals(shownPresets, Engine.Presets)) Build(snap, ids);
            for (int k = 0; k < snap.Length; k++) Update(rows[snap[k].Id], snap[k], k, snap.Length);
        }

        void Build(ControllerInfo[] snap, string ids)
        {
            shownIds = ids; shownPresets = Engine.Presets;
            list.Children.Clear(); rows.Clear();
            int extra = Engine.OverLimit;   // connected, but over the free version's limit
            none.Visibility = snap.Length + extra == 0 ? Visibility.Visible : Visibility.Collapsed;
            hint.Visibility = snap.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (ControllerInfo c in snap)
            {
                Row r = NewRow(c.Id);
                rows[c.Id] = r;
                list.Children.Add(r.Card);
            }
            for (int k = 0; k < extra; k++) list.Children.Add(LockedCard(snap.Length + k));
        }

        // A controller the free version doesn't drive: the card's shape, dimmed, with a lock (no Tag — can't be dragged)
        static FrameworkElement LockedCard(int index)
        {
            StackPanel body = new StackPanel();
            body.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Brush("CardBorder"), Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "🔒 " + Lang.F("Controller {0}", index + 1), FontSize = 14, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Style = Theme.Style("MutedText"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = Lang.T("Connected, but the free version drives one controller. Any number of controllers comes with Glowstick Pro.") });
            return LockedPages.ToPro(new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(16, 12, 16, 14), Opacity = 0.6, Child = body });
        }

        Row NewRow(string id)
        {
            Row r = new Row();
            r.Card = new Border { Style = Theme.Style("CardBox"), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(16, 12, 16, 14), Tag = id };
            StackPanel body = new StackPanel();
            body.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = r.Light, Margin = new Thickness(0, 0, 0, 12) });

            // Line 1: name, own preset
            Grid line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 300 });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MaxWidth = 360 });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            r.Name = new TextBox { MaxLength = 24, ToolTip = Lang.T("Name (empty — “Controller N”):") };
            r.Hint = new TextBlock { Foreground = Theme.Brush("Muted"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            r.Name.LostKeyboardFocus += (s, e) => SaveName(r, id);
            r.Name.KeyDown += (s, e) => { if (e.Key == Key.Enter) { SaveName(r, id); Keyboard.ClearFocus(); } };
            r.Name.TextChanged += (s, e) => r.Hint.Visibility = r.Name.Text == "" ? Visibility.Visible : Visibility.Collapsed;
            Grid nameBox = new Grid();
            nameBox.Children.Add(r.Name);
            nameBox.Children.Add(r.Hint);   // "Controller 2" while the name is empty
            line.Children.Add(nameBox);
            r.Preset = PresetCombo(Lang.T("Same as everyone"));
            r.Preset.ToolTip = Lang.T("Own preset");
            r.Preset.SelectionChanged += (s, e) =>
            {
                if (r.Updating) return;
                ComboBoxItem it = r.Preset.SelectedItem as ComboBoxItem;
                if (it == null) return;
                string name = (string)it.Tag;
                PadMenu.IfPresent(id, () => Engine.SetPadPreset(id, name == "" ? null : name));
            };
            Grid.SetColumn(r.Preset, 2);
            line.Children.Add(r.Preset);
            body.Children.Add(line);

            // Line 2: battery and model, actions
            DockPanel line2 = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal };
            acts.Children.Add(Small(Lang.T("Find"), Lang.T("It rumbles and flashes white"), () => Engine.Find(id), id));
            r.Off = Small(Lang.T("Turn off (Bluetooth)"), null, () => Engine.Disconnect(id), id);
            acts.Children.Add(r.Off);
            DockPanel.SetDock(acts, Dock.Right);
            line2.Children.Add(acts);
            r.Cell = new BatteryCell { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            line2.Children.Add(r.Cell);
            r.Info = new TextBlock { Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            line2.Children.Add(r.Info);
            body.Children.Add(line2);

            r.Drift = new StackPanel { Visibility = Visibility.Collapsed };
            body.Children.Add(r.Drift);

            r.Warning = new TextBlock { Foreground = Theme.Brush("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            body.Children.Add(r.Warning);

            // Pro: battery history (and whatever else the module adds to a controller's card)
            if (Pro.Unlocked && Pro.ControllerExtra != null)
            {
                r.Extra = Pro.ControllerExtra(id);
                if (r.Extra != null) body.Children.Add(r.Extra.View);
            }
            else if (!Pro.Unlocked) body.Children.Add(LockedPages.BatteryHistory());   // the battery history chart — Pro
            r.Card.Child = body;
            return r;
        }

        static Button Small(string text, string tip, Action act, string id)
        {
            Button b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), FontSize = 12, ToolTip = tip };
            b.Click += (s, e) => PadMenu.IfPresent(id, act);
            return b;
        }

        // first ("Same as everyone" / "— don't change —", Tag "") + presets under their category headers (Tag = preset name)
        internal static ComboBox PresetCombo(string first)
        {
            ComboBox cb = new ComboBox { MaxDropDownHeight = 420 };
            cb.Items.Add(new ComboBoxItem { Content = first, Tag = "" });
            string cat = null;
            foreach (Preset p in Engine.Presets)
            {
                if (p.Category != cat)
                {
                    cat = p.Category;
                    cb.Items.Add(new ComboBoxItem { Content = p.CategoryTitle.ToUpper(), IsEnabled = false, FontSize = 11, Foreground = Theme.Brush("Accent"), Tag = null });
                }
                cb.Items.Add(new ComboBoxItem { Content = (p.Locked ? "🔒 " : "") + p.Title, Tag = p.Name, Padding = new Thickness(18, 6, 10, 6), IsEnabled = !p.Locked });
            }
            return cb;
        }

        void SaveName(Row r, string id)
        {
            string name = r.Name.Text.Trim();
            ControllerInfo c = Array.Find(Engine.Snapshot(), x => x.Id == id);
            if (c != null && (c.Name ?? "") != name) PadMenu.IfPresent(id, () => Engine.SetPadName(id, name));
        }

        void Update(Row r, ControllerInfo c, int index, int count)
        {
            Color col = Color.FromRgb(c.Color.R, c.Color.G, c.Color.B);
            if (r.Light.Color != col) r.Light.Color = col;
            if (!r.Name.IsKeyboardFocusWithin)
            {
                string shown = string.IsNullOrEmpty(c.Name) ? "" : c.Name;
                if (r.Name.Text != shown) r.Name.Text = shown;
            }
            r.Hint.Text = Lang.F("Controller {0}", index + 1);
            r.Updating = true;
            string own = c.OwnPreset ?? "";
            foreach (object o in r.Preset.Items)
            {
                ComboBoxItem it = (ComboBoxItem)o;
                if ((string)it.Tag == own && !ReferenceEquals(r.Preset.SelectedItem, it)) { r.Preset.SelectedItem = it; break; }
            }
            r.Updating = false;

            string info = (index + 1) + ". " + c.Model + " · " + UiFormat.PadLine(c);
            if (c.Virtual) info += " · " + c.Rate + " " + Lang.T("Hz");
            if (r.Info.Text != info) r.Info.Text = info;
            r.Cell.Visibility = c.Battery >= 0 ? Visibility.Visible : Visibility.Collapsed;
            r.Cell.Set(c.Battery, c.Charging);
            r.Off.IsEnabled = c.Kind == "Bluetooth";
            string warn = c.Warning != null ? "⚠ " + c.Warning : "";
            if (r.Warning.Text != warn) { r.Warning.Text = warn; r.Warning.Visibility = warn == "" ? Visibility.Collapsed : Visibility.Visible; }

            ShowDrift(r, c);
            if (r.Extra != null) r.Extra.Update(c);
        }

        // A stick that rests off center (and isn't hidden by its dead zone yet): what's wrong, and in Pro a one-click fix
        void ShowDrift(Row r, ControllerInfo c)
        {
            List<string> lines = new List<string>();
            List<KeyValuePair<bool, double>> fix = new List<KeyValuePair<bool, double>>();
            foreach (bool left in new[] { true, false })
            {
                double off = left ? c.DriftL : c.DriftR;
                double dz = Pro.StickDeadzone != null && Pro.Unlocked ? Pro.StickDeadzone(c.Id, left) : 0;
                if (off < DriftMeter.Shown || off < dz - 0.01) continue;
                int pct = (int)Math.Round(off * 100);
                lines.Add(left ? Lang.F("The left stick drifts: at rest it stays {0}% off center.", pct) : Lang.F("The right stick drifts: at rest it stays {0}% off center.", pct));
                fix.Add(new KeyValuePair<bool, double>(left, DriftMeter.DeadzoneFor(off)));
            }
            string key = string.Join("|", lines);
            if (key == r.DriftShown) return;
            r.DriftShown = key;
            r.Drift.Children.Clear();
            r.Drift.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            string id = c.Id;
            for (int k = 0; k < lines.Count; k++)
            {
                DockPanel row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                bool left = fix[k].Key; double dz = fix[k].Value;
                Button b = new Button { Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Content = (Pro.Unlocked ? "" : "🔒 ") + Lang.F("Set its dead zone to {0}%", (int)Math.Round(dz * 100)) };
                b.Click += (s, e) =>
                {
                    if (!Pro.Unlocked || Pro.SetStickDeadzone == null) { if (UiApp.Main != null) UiApp.Main.ShowPro(); return; }
                    PadMenu.IfPresent(id, () => Pro.SetStickDeadzone(id, left, dz));
                };
                DockPanel.SetDock(b, Dock.Right);
                row.Children.Add(b);
                row.Children.Add(new TextBlock { Text = "⚠ " + lines[k], Foreground = Theme.Brush("Warn"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                r.Drift.Children.Add(row);
            }
        }
    }
}
