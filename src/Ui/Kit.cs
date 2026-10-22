// The small set of building blocks every settings-like page is made of (Settings, Profiles, Game profiles, the
// locked previews): cards with a title, label + control rows, toggles, sliders, combo boxes, table columns with a header,
// remove buttons. One place, so the pages look the same and a fix lands everywhere.
using System;
using System.Windows;
using System.Windows.Controls;

namespace Glowstick
{
    internal static class Kit
    {
        // A section title + a card; returns the card's inner panel
        public static StackPanel Card(Panel parent, string title)
        {
            if (title != null) parent.Children.Add(new TextBlock { Text = title, Style = Theme.Style("Section") });
            StackPanel inner = new StackPanel();
            parent.Children.Add(new Border { Style = Theme.Style("CardBox"), Padding = new Thickness(18, 14, 18, 16), Child = inner });
            return inner;
        }

        // Muted explanatory text
        public static TextBlock Note(Panel parent, string text)
        {
            TextBlock t = new TextBlock { Text = text, Style = Theme.Style("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, parent.Children.Count > 0 ? 8 : 0, 0, 0) };
            parent.Children.Add(t);
            return t;
        }

        // Fills the width up to max, aligned left (a plain MaxWidth would shrink every card to its content)
        public static Grid Wide(FrameworkElement content, double max)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MaxWidth = max });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(content);
            return g;
        }

        // label | control. The label takes up to labelWidth (less in a narrow column — it wraps), the control the rest.
        public static FrameworkElement Row(TextBlock label, FrameworkElement control, double labelWidth)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = labelWidth });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            label.Margin = new Thickness(0, 0, 10, 0);
            label.TextWrapping = TextWrapping.Wrap;
            g.Children.Add(label);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public static TextBlock Label(string text) { return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; }

        // A switch with a wrapping label; saved on click
        public static CheckBox Toggle(Panel parent, string text, bool value, Action<bool> set)
        {
            CheckBox c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, parent.Children.Count > 0 ? 10 : 0, 0, 0) };
            c.Click += (s, e) => { set(c.IsChecked == true); Settings.Save(); };
            parent.Children.Add(c);
            return c;
        }

        // A labelled slider: label(value) on the left, the slider on the right; saved when the mouse is released or a key is used
        public static Slider Slide(Panel parent, Func<double, string> label, double min, double max, double value, Action<double> set, double indent = 0, double step = 1)
        {
            TextBlock lbl = Label("");
            Slider sl = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), SmallChange = step, LargeChange = step * 5, IsSnapToTickEnabled = true, TickFrequency = step };
            lbl.Text = label(sl.Value);
            sl.ValueChanged += (s, e) => { set(sl.Value); lbl.Text = label(sl.Value); };
            sl.PreviewMouseUp += (s, e) => Settings.Save();
            sl.KeyUp += (s, e) => Settings.Save();
            FrameworkElement row = Row(lbl, sl, 240);
            row.Margin = new Thickness(indent, 10, 0, 0);
            parent.Children.Add(row);
            return sl;
        }

        // A combo box of labels; set(index) and save on change
        public static ComboBox Combo(string[] labels, int selected, Action<int> set)
        {
            ComboBox c = new ComboBox();
            foreach (string l in labels) c.Items.Add(l);
            c.SelectedIndex = selected;
            c.SelectionChanged += (s, e) => { if (c.SelectedIndex >= 0) { set(c.SelectedIndex); Settings.Save(); } };
            return c;
        }

        // label | combo box, as a row of the panel
        public static ComboBox Combo(Panel parent, string label, string[] labels, int selected, Action<int> set, double width = 280)
        {
            ComboBox c = Combo(labels, Math.Max(0, selected), set);
            c.MaxWidth = width; c.HorizontalAlignment = HorizontalAlignment.Stretch;   // narrower when the column is
            FrameworkElement row = Row(Label(label), c, 240);
            row.Margin = new Thickness(0, parent.Children.Count > 0 ? 10 : 0, 0, 0);
            parent.Children.Add(row);
            return c;
        }

        // ---------- tables: fixed columns, one that fills, then auto ones (buttons) ----------
        public static Grid Columns(double[] widths, int autoColumns)
        {
            Grid g = new Grid();
            foreach (double w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int k = 0; k < autoColumns; k++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return g;
        }

        // Puts el into column col, top-aligned (a row with a two-line cell keeps the rest at the top), 8 px gap after it
        public static void Put(Grid g, FrameworkElement el, int col, bool gap = true)
        {
            if (gap) el.Margin = new Thickness(0, 0, 8, 0);
            el.VerticalAlignment = VerticalAlignment.Top;
            if (el is Button) el.Margin = new Thickness(el.Margin.Left, 2, el.Margin.Right, 0);   // ghost buttons are a bit lower than the boxes
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        public static TextBlock Head(string text) { return new TextBlock { Text = text, Style = Theme.Style("MutedText"), FontSize = 12 }; }

        public static FrameworkElement Header(double[] widths, int autoColumns, params string[] titles)
        {
            Grid g = Columns(widths, autoColumns);
            g.Margin = new Thickness(2, 6, 0, 2);
            for (int i = 0; i < titles.Length; i++) Put(g, Head(titles[i]), i);
            return g;
        }

        public static TextBlock Empty(string text) { return new TextBlock { Text = text, Style = Theme.Style("MutedText"), Margin = new Thickness(2, 6, 0, 0) }; }

        public static Button RemoveButton(Action act)
        {
            Button rm = new Button { Content = "✕", Style = Theme.Style("GhostButton"), ToolTip = Lang.T("Remove"), VerticalAlignment = VerticalAlignment.Center };
            rm.Click += (s, e) => act();
            return rm;
        }
    }
}
