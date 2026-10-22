// The touchpad-as-mouse options (TouchConfig): on the Settings page for the global ones, and in a Pro input profile for its own.
using System;
using System.Windows;
using System.Windows.Controls;

namespace Glowstick
{
    internal static class TouchSettings
    {
        public static void Build(Panel p, TouchConfig c)
        {
            ComboBox mode = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            mode.Items.Add(Lang.T("Move the cursor (like a laptop)"));
            mode.Items.Add(Lang.T("Absolute (the pad is the screen)"));
            mode.SelectedIndex = c.Absolute ? 1 : 0;
            mode.SelectionChanged += (s, e) => { c.Absolute = mode.SelectedIndex == 1; Settings.Save(); };
            FrameworkElement modeRow = SettingsPage.Row(new TextBlock { Text = Lang.T("Mode:"), VerticalAlignment = VerticalAlignment.Center }, mode, 240);
            modeRow.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(modeRow);

            SettingsPage.Slide(p, v => Lang.F("Sensitivity: {0}%", (int)v), 20, 400, Math.Round(c.Sens * 100), v => c.Sens = v / 100.0, 0, 5);
            SettingsPage.Toggle(p, Lang.T("Acceleration: slow movements are precise, fast ones go far"), c.Accel, v => c.Accel = v);
            SettingsPage.Toggle(p, Lang.T("Jitter compensation"), c.Jitter, v => c.Jitter = v);
            SettingsPage.Toggle(p, Lang.T("Tap to click (two-finger tap — right click)"), c.TapClick, v => c.TapClick = v);
            SettingsPage.Toggle(p, Lang.T("Double tap and slide to drag"), c.TapDrag, v => c.TapDrag = v);
            SettingsPage.Toggle(p, Lang.T("Clicking the right half — right button"), c.RightHalfClick, v => c.RightHalfClick = v);
            SettingsPage.Toggle(p, Lang.T("Two fingers scroll"), c.Scroll, v => c.Scroll = v);
            SettingsPage.Slide(p, v => Lang.F("Scroll speed: {0}%", (int)v), 20, 400, Math.Round(c.ScrollSens * 100), v => c.ScrollSens = v / 100.0, 0, 5);
            SettingsPage.Toggle(p, Lang.T("Natural scrolling (content follows the fingers)"), c.NaturalScroll, v => c.NaturalScroll = v);
            SettingsPage.Toggle(p, Lang.T("Trackball: the cursor keeps gliding after a flick"), c.Trackball, v => c.Trackball = v);
            SettingsPage.Slide(p, v => Lang.F("Glide friction: {0}%", (int)v), 0, 100, Math.Round(c.Friction * 100), v => c.Friction = v / 100.0, 0, 5);
            SettingsPage.Toggle(p, Lang.T("Invert horizontal"), c.InvertX, v => c.InvertX = v);
            SettingsPage.Toggle(p, Lang.T("Invert vertical"), c.InvertY, v => c.InvertY = v);
        }
    }
}
