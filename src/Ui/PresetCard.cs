// Preset card: the card is lit by the preset itself (a soft glow of what 4 controllers show right now, every frame)
// with the title over it. Right click: the preset's own settings, a copy to edit (built-in) or edit / export / delete (own).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Glowstick
{
    internal class PresetCard : Border
    {
        const int Stripes = 4;
        const double Radius = 8;
        public int Index { get; private set; }
        public Preset Preset { get; private set; }
        public event Action<PresetCard> Picked, EditRequested, DeleteRequested, ExportRequested, TuneRequested, CopyRequested;

        readonly GradientStop[] stops = new GradientStop[Stripes];
        readonly Rectangle glow = new Rectangle { IsHitTestVisible = false };
        bool selected, hover;

        public PresetCard(int index, Preset p)
        {
            Index = index; Preset = p;
            Style = Theme.Style("CardBox");
            Padding = new Thickness(0);
            Height = 56;
            Cursor = Cursors.Hand;
            ToolTip = p.Title;

            // Glow: the 4 "controllers" left to right as a gradient, strongest at the top, blurred into a light
            LinearGradientBrush fill = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int k = 0; k < Stripes; k++) { stops[k] = new GradientStop(Colors.Black, k / (double)(Stripes - 1)); fill.GradientStops.Add(stops[k]); }
            glow.Fill = fill;
            glow.OpacityMask = new LinearGradientBrush(Color.FromArgb(0xFF, 0, 0, 0), Color.FromArgb(0x55, 0, 0, 0), 90);
            glow.Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance };
            glow.Margin = new Thickness(-14, -16, -14, -6);

            TextBlock title = new TextBlock
            {
                Text = p.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(11, 0, 11, 8),
                Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 6, Opacity = 0.9, Color = Colors.Black }   // readable over white/yellow light
            };
            Grid body = new Grid();
            body.Children.Add(glow);
            body.Children.Add(title);
            // A Pro preset without Pro: dimmer, a lock in the corner; a click leads to Settings → Glowstick Pro
            if (p.Locked)
            {
                ToolTip = p.Title + "\n" + Lang.T("Available in Glowstick Pro");
                title.Opacity = 0.75;
                body.Children.Add(new TextBlock
                {
                    Text = "🔒", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 8, 0), Opacity = 0.9, IsHitTestVisible = false
                });
            }
            Child = body;
            // The glow must not spill outside the rounded card
            SizeChanged += (s, e) => body.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width - 2, e.NewSize.Height - 2), Radius - 1, Radius - 1);
            Paint();
            Animate(0);

            MouseEnter += (s, e) => { hover = true; Paint(); };
            MouseLeave += (s, e) => { hover = false; Paint(); };
            MouseLeftButtonUp += (s, e) => { if (Picked != null) Picked(this); };
            if (p.Locked) return;
            // Right click: the preset's own settings (any preset) and a copy to edit (built-in) or edit / export / delete (own).
            // Without Pro the items carry a lock and lead to Settings → Glowstick Pro.
            ContextMenu menu = new ContextMenu();
            bool pro = Glowstick.Pro.Unlocked;
            Func<string, Action<PresetCard>, MenuItem> item = (text, act) =>
            {
                MenuItem mi = new MenuItem { Header = (pro ? "" : "🔒 ") + text };
                mi.Click += (s, e) => { if (!pro) { if (UiApp.Main != null) UiApp.Main.ShowPro(); } else if (act != null) act(this); };
                menu.Items.Add(mi);
                return mi;
            };
            if (p.Custom != null)
            {
                MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2 && EditRequested != null) { EditRequested(this); e.Handled = true; } };
                item(Lang.T("Edit"), c => { if (EditRequested != null) EditRequested(c); });
                item(Lang.T("Preset settings…"), c => { if (TuneRequested != null) TuneRequested(c); });
                item(Lang.T("Export…"), c => { if (ExportRequested != null) ExportRequested(c); });
                item(Lang.T("Delete"), c => { if (DeleteRequested != null) DeleteRequested(c); });
            }
            else
            {
                item(Lang.T("Preset settings…"), c => { if (TuneRequested != null) TuneRequested(c); });
                item(Lang.T("Edit a copy…"), c => { if (CopyRequested != null) CopyRequested(c); });
            }
            ContextMenu = menu;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Paint(); }
        }

        void Paint()
        {
            bool locked = Preset.Locked;
            Background = Theme.Brush(selected ? "SelCard" : hover && !locked ? "CardHover" : "Card");
            BorderBrush = Theme.Brush(selected ? "Accent" : "CardBorder");
            glow.Opacity = locked ? 0.45 : selected || hover ? 1 : 0.8;
        }

        // t — seconds; the glow shows what 4 controllers would show at that moment
        public void Animate(double t)
        {
            Rgb[] c = UiFormat.SwatchColors(Preset.Fn, t, Stripes);
            for (int k = 0; k < Stripes; k++)
            {
                Color col = Color.FromRgb((byte)c[k].R, (byte)c[k].G, (byte)c[k].B);
                if (stops[k].Color != col) stops[k].Color = col;
            }
        }
    }
}
