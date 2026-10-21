// Drag and drop to change the controller order: cards on the Controllers page (vertical) and tiles on the Presets page.
// A drag starts only after the mouse moves a few pixels, and never from a text box, combo box or button inside the item,
// so clicks keep working. While dragging, an accent line shows where the item will land.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Glowstick
{
    internal class DragReorder
    {
        readonly Panel panel;
        readonly bool vertical;
        readonly Func<UIElement, string> idOf;
        readonly Action<string, int> drop;
        UIElement item;
        Point start;
        bool dragging;
        int target = -1;
        Marker marker;

        // idOf — the controller id of a child of the panel (null — not draggable); drop(id, new index among the children)
        public DragReorder(Panel panel, bool vertical, Func<UIElement, string> idOf, Action<string, int> drop)
        {
            this.panel = panel; this.vertical = vertical; this.idOf = idOf; this.drop = drop;
            panel.PreviewMouseLeftButtonDown += OnDown;
            panel.PreviewMouseMove += OnMove;
            panel.PreviewMouseLeftButtonUp += OnUp;
            panel.LostMouseCapture += (s, e) => { if (dragging) Stop(); };
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            item = null;
            if (panel.Children.Count < 2 || FromControl(e.OriginalSource as DependencyObject)) return;
            UIElement child = ChildOf(e.OriginalSource as DependencyObject);
            if (child == null || idOf(child) == null) return;
            item = child;
            start = e.GetPosition(panel);
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (item == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { if (dragging) Stop(); item = null; return; }
            Point p = e.GetPosition(panel);
            if (!dragging)
            {
                if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                    && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
                dragging = true;
                item.Opacity = 0.45;
                panel.CaptureMouse();
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(panel);
                if (layer != null) { marker = new Marker(panel); layer.Add(marker); }
            }
            target = Slot(p);
            if (marker != null) marker.Show(Line(target));
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) { item = null; return; }
            string id = idOf(item);
            int from = panel.Children.IndexOf(item), to = target > from ? target - 1 : target;
            Stop();
            e.Handled = true;   // no click on the tile under the mouse
            if (id != null && from >= 0 && to >= 0 && to != from) drop(id, to);
        }

        void Stop()
        {
            bool was = dragging;
            dragging = false;
            if (item != null) item.Opacity = 1;
            item = null;
            if (marker != null)
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(panel);
                if (layer != null) layer.Remove(marker);
                marker = null;
            }
            if (was && panel.IsMouseCaptured) panel.ReleaseMouseCapture();
        }

        // Insertion slot 0..Count: before the child nearest to the mouse, or after it when the mouse is past its middle
        int Slot(Point p)
        {
            int best = -1; double bestD = double.MaxValue;
            Rect bestR = Rect.Empty;
            for (int k = 0; k < panel.Children.Count; k++)
            {
                Rect r = Bounds(panel.Children[k]);
                double dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right)), dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = k; bestR = r; }
            }
            if (best < 0) return 0;
            bool after = vertical ? p.Y > bestR.Top + bestR.Height / 2 : p.X > bestR.Left + bestR.Width / 2;
            return after ? best + 1 : best;
        }

        // Where the marker line goes for a slot: the leading edge of that child, or the trailing edge of the last one
        Rect Line(int slot)
        {
            int n = panel.Children.Count;
            bool end = slot >= n;
            Rect r = Bounds(panel.Children[end ? n - 1 : slot]);
            FrameworkElement fe = panel.Children[end ? n - 1 : slot] as FrameworkElement;
            Thickness m = fe != null ? fe.Margin : new Thickness();
            if (vertical)
            {
                double y = end ? r.Bottom + m.Bottom / 2 : r.Top - (slot > 0 ? GapAbove(slot) / 2 : 0);
                return new Rect(r.Left, y - 1.5, r.Width, 3);
            }
            double x = end ? r.Right + m.Right / 2 : r.Left - m.Right / 2;
            return new Rect(x - 1.5, r.Top, 3, r.Height);
        }

        double GapAbove(int slot)
        {
            return Bounds(panel.Children[slot]).Top - Bounds(panel.Children[slot - 1]).Bottom;
        }

        Rect Bounds(UIElement child)
        {
            FrameworkElement fe = (FrameworkElement)child;
            Point o = child.TranslatePoint(new Point(0, 0), panel);
            return new Rect(o.X, o.Y, fe.ActualWidth, fe.ActualHeight);
        }

        UIElement ChildOf(DependencyObject d)
        {
            while (d != null)
            {
                UIElement u = d as UIElement;
                if (u != null && VisualTreeHelper.GetParent(u) == panel) return u;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        bool FromControl(DependencyObject d)
        {
            while (d != null && d != panel)
            {
                if (d is TextBoxBase || d is ComboBox || d is ButtonBase || d is Popup) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // The accent insertion line
        class Marker : Adorner
        {
            Rect line = Rect.Empty;

            public Marker(UIElement on) : base(on) { IsHitTestVisible = false; }

            public void Show(Rect r) { line = r; InvalidateVisual(); }

            protected override void OnRender(DrawingContext g)
            {
                if (!line.IsEmpty) g.DrawRoundedRectangle(Theme.Brush("Accent"), null, line, 1.5, 1.5);
            }
        }
    }
}
