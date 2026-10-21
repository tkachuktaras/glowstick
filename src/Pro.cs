// Hooks for Glowstick Pro — a closed module (repo glowstick_pro) compiled in only for the Pro build.
// The free build has none of its code: the hooks below stay empty and Pro features show a "Get Pro" card instead.
// The module implements Init() (a partial method — without the module the compiler drops the call) and fills the hooks.
using System;
using System.Collections.Generic;
using System.Windows;

namespace Glowstick
{
    internal static partial class Pro
    {
        public static bool Present { get; private set; }     // the Pro module is compiled in
        public static Func<bool> Owned = () => false;        // Pro is paid for (the module checks the license key)
        public static bool Unlocked { get { return Present && Owned(); } }

        // The free version drives one controller (with game mode); Pro — any number
        public static int MaxControllers { get { return Unlocked ? int.MaxValue : 1; } }

        public const string Price = "$5";
        public static readonly string BuyUrl = "https://tkachuktaras.gumroad.com/l/glowstick-pro";   // the Gumroad product page
        public const string MyPresets = "My presets";         // category of the user's own presets (Pro)

        // ---------- filled by the module ----------
        public static Action<Dictionary<string, object>> Load, Save;       // its part of settings.json
        public static Func<string> SavedPreset;                             // what to store as the preset (a game profile may be playing)
        public static Action AppStarted, AppExiting, EverySecond;
        public static Action LicenseChanged;                                 // a key was activated or deactivated (Pro build)
        public static Action<ControllerInfo[]> BatteryTick;                 // every 2 s from the tray
        public static Func<MainWindow.Page, MainWindow, FrameworkElement> BuildPage;
        public static Action<PresetsPage, MainWindow> PresetsPageCreated;
        public static Action<SettingsPage> SettingsPageCreated;
        public static Func<string, ControllerExtra> ControllerExtra;         // an extra part of a controller's card (by id)

        // Per input report (Pro): gyro → mouse, button remap, shift layers, macros. Runs on the controller's read thread,
        // before the report is forwarded to the virtual pad. `raw` is the physical report, `send` the one about to be sent
        // (already button-masked for held hotkeys); returns the report to forward (the same array when nothing was remapped).
        public static Func<Controller, byte[], byte[], long, byte[]> MapReport;

        public static void Startup()
        {
            Init();
            if (Present) Log.Info("Pro module present, " + (Unlocked ? "unlocked" : "not purchased"));
        }

        static partial void Init();
    }

    // A piece the Pro module adds to a controller's card on the Controllers page
    internal abstract class ControllerExtra
    {
        public abstract FrameworkElement View { get; }
        public abstract void Update(ControllerInfo c);
    }
}
