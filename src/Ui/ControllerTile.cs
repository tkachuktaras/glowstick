// Controller tile: the live light-bar color as a strip on top, the name, a drawn battery cell and "82% · USB".
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glowstick
{
    internal class ControllerTile : Border
    {
        public string Id { get; private set; }
        public event Action<ControllerTile> Clicked;

        readonly SolidColorBrush light = new SolidColorBrush(Colors.Black);
        readonly Border strip = new Border { CornerRadius = new CornerRadius(3) };
        readonly TextBlock name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock warning = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        readonly BatteryCell cell = new BatteryCell();
        bool compact;
        string shown;   // what the texts were built from — rebuilt only when it changes

        public ControllerTile()
        {
            Style = Theme.Style("CardBox");
            Cursor = Cursors.Hand;
            strip.Background = light;
            line.Style = Theme.Style("MutedText"); line.TextWrapping = TextWrapping.NoWrap;
            warning.Foreground = Theme.Brush("Warn"); warning.FontSize = 12;

            Grid bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            cell.Margin = new Thickness(0, 0, 7, 0);
            bottom.Children.Add(cell);
            Grid.SetColumn(line, 1);
            bottom.Children.Add(line);

            StackPanel body = new StackPanel();
            body.Children.Add(strip);
            body.Children.Add(name);
            body.Children.Add(bottom);
            body.Children.Add(warning);
            Child = body;
            Compact = false;

            MouseLeftButtonUp += (s, e) => { if (Clicked != null) Clicked(this); };
        }

        // More than 4 controllers — smaller tiles in two rows
        public bool Compact
        {
            get { return compact; }
            set
            {
                compact = value;
                Padding = value ? new Thickness(10, 8, 10, 7) : new Thickness(12, 10, 12, 9);
                strip.Height = value ? 4 : 6;
                name.FontSize = value ? 13 : 14;
                name.Margin = new Thickness(0, value ? 6 : 8, 0, value ? 3 : 5);
            }
        }

        public bool Locked { get; private set; }

        // A controller over the free version's limit: connected, but not driven — shown locked; a click leads to Settings → Glowstick Pro
        public void ShowLocked(int index)
        {
            if (Id == null && shown == "locked" + index) return;
            Id = null; shown = "locked" + index; Locked = true;
            Opacity = 0.55;
            light.Color = Color.FromRgb(0x3A, 0x3A, 0x42);
            name.Text = Lang.F("Controller {0}", index + 1);
            line.Text = "🔒 Glowstick Pro";
            cell.Visibility = Visibility.Collapsed;
            warning.Visibility = Visibility.Collapsed;
            ToolTip = Lang.T("Connected, but the free version drives one controller. Any number of controllers comes with Glowstick Pro.");
        }

        public void Update(ControllerInfo c, int index)
        {
            if (Id == null) Opacity = 1;   // was a locked tile
            Id = c.Id; Locked = false;
            System.Windows.Media.Color col = System.Windows.Media.Color.FromRgb(c.Color.R, c.Color.G, c.Color.B);
            if (light.Color != col) light.Color = col;

            string title = (c.OwnPreset != null ? "★ " : "") + Engine.DisplayName(c, index);
            string key = title + "|" + UiFormat.PadLine(c) + "|" + c.Battery + "|" + c.Warning + "|" + c.Model;
            if (key == shown) return;
            shown = key;

            name.Text = title;
            line.Text = UiFormat.PadLine(c);
            cell.Visibility = c.Battery >= 0 ? Visibility.Visible : Visibility.Collapsed;
            cell.Set(c.Battery, c.Charging);
            warning.Text = c.Warning != null ? "⚠ " + c.Warning : "";
            warning.Visibility = c.Warning != null ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = (index + 1) + ". " + Engine.DisplayName(c, index) + " — " + c.Model + ", " + c.Kind +
                (c.OwnPreset != null ? "\n" + Lang.F("Own preset: {0}", UiFormat.PresetTitle(c.OwnPreset)) : "") +
                (c.Warning != null ? "\n⚠ " + c.Warning : "") + "\n" + Lang.T("Click — menu for this controller, drag — change the order");
        }
    }

    // A small battery drawn like the system one: outline, nub, fill by charge in the level's color
    internal class BatteryCell : Grid
    {
        readonly Rectangle fill = new Rectangle { RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Left };

        public BatteryCell()
        {
            Width = 20; Height = 10;
            Brush outline = Theme.Brush("Text");
            Border body = new Border { Width = 17, Height = 10, BorderBrush = outline, BorderThickness = new Thickness(1.3), CornerRadius = new CornerRadius(2.5),
                Padding = new Thickness(1.2), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.85, Child = fill };
            Rectangle nub = new Rectangle { Width = 2, Height = 4, Fill = outline, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.85,
                RadiusX = 0.8, RadiusY = 0.8 };
            Children.Add(body);
            Children.Add(nub);
        }

        public void Set(int percent, bool charging)
        {
            BatteryLevel lv = UiFormat.Level(percent);
            fill.Fill = Theme.Brush(lv == BatteryLevel.Critical ? "BatCritical" : lv == BatteryLevel.Low ? "BatLow" : "BatOk");
            fill.Width = Math.Max(1.5, 12.0 * Math.Max(0, Math.Min(100, percent)) / 100);
        }
    }
}
