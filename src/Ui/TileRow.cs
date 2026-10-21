// The row of controller tiles (top of the Presets page): up to 4 in one row, more — compact in two rows. Drag a tile to reorder.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Glowstick
{
    internal class TileRow : UserControl
    {
        public event Action<string> TileClicked;

        readonly UniformGrid grid = new UniformGrid { HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock none = new TextBlock();
        readonly List<ControllerTile> tiles = new List<ControllerTile>();

        public TileRow()
        {
            none.Style = Theme.Style("MutedText");
            none.FontSize = 13;
            none.Text = Lang.T("No controllers found.\nConnect a DualShock 4 or DualSense via USB or Bluetooth — it will show up by itself.");
            none.Margin = new Thickness(0, 6, 0, 10);
            Grid root = new Grid();
            root.Children.Add(grid);
            root.Children.Add(none);
            Content = root;
            new DragReorder(grid, false, el => ((ControllerTile)el).Id, (id, k) => PadMenu.IfPresent(id, () => Engine.MoveTo(id, k)));
        }

        public void Update(ControllerInfo[] snap)
        {
            // The free version drives one controller; the other connected ones get locked tiles after it
            int live = snap.Length, extra = Engine.OverLimit, n = live + extra;
            none.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            while (tiles.Count < n)
            {
                ControllerTile t = new ControllerTile { Margin = new Thickness(0, 0, 10, 10) };
                t.Clicked += tile =>
                {
                    if (tile.Locked) UiApp.Main.ShowPro();
                    else if (TileClicked != null && tile.Id != null) TileClicked(tile.Id);
                };
                tiles.Add(t);
            }
            if (grid.Children.Count != n)
            {
                grid.Children.Clear();
                int cols = UiFormat.TileColumns(n);
                grid.Columns = cols;
                grid.MaxWidth = cols * (n > 4 ? 170 : 220);   // tiles up to 210/160 DIP, narrower when the window is narrow
                for (int k = 0; k < n; k++) { tiles[k].Compact = n > 4; grid.Children.Add(tiles[k]); }
            }
            for (int k = 0; k < live; k++) tiles[k].Update(snap[k], k);
            for (int k = live; k < n; k++) tiles[k].ShowLocked(k);
        }
    }
}
