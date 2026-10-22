// The virtual controller games see for one physical pad, through ViGEmBus: a DualShock 4 (the DS4-layout report goes
// over as is) or an Xbox 360 (buttons, sticks and triggers mapped). Rumble from the game comes back through `rumble`.
using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Glowstick
{
    internal sealed class VirtualPad
    {
        readonly ViGEmClient client;
        readonly Action<byte, byte> rumble;   // (large, small)
        readonly string id;                   // for the log
        volatile IDualShock4Controller ds4;
        volatile IXbox360Controller x360;
        bool submitFailed;

        public VirtualPad(ViGEmClient client, string kind, Action<byte, byte> rumble, string id)
        {
            this.client = client; this.rumble = rumble; this.id = id;
            Connect(kind);
        }

        public bool Connected { get { return ds4 != null || x360 != null; } }

        // FeedbackReceived is marked obsolete in ViGEm.NET (AwaitRawOutputReport is the newer way); it works for rumble, and
        // switching needs a feel-check on real hardware — kept until then
#pragma warning disable 618
        void Connect(string kind)
        {
            try
            {
                if (kind == "Xbox")
                {
                    IXbox360Controller x = client.CreateXbox360Controller();
                    x.AutoSubmitReport = false;
                    x.FeedbackReceived += OnX360Feedback;
                    x.Connect();
                    x360 = x;
                }
                else
                {
                    IDualShock4Controller d = client.CreateDualShock4Controller();
                    d.FeedbackReceived += OnFeedback;
                    d.Connect();
                    ds4 = d;
                }
            }
            catch (Exception e) { ds4 = null; x360 = null; Log.Error("virtual pad", e); }
        }

        public void Disconnect()
        {
            IDualShock4Controller d = ds4; IXbox360Controller x = x360;
            ds4 = null; x360 = null;
            try
            {
                if (d != null) { d.FeedbackReceived -= OnFeedback; d.Disconnect(); }
                if (x != null) { x.FeedbackReceived -= OnX360Feedback; x.Disconnect(); }
            }
            catch { }
        }
#pragma warning restore 618

        // Change what games see, live (recreates the virtual pad)
        public void Switch(string kind) { Disconnect(); Connect(kind); }

        void OnFeedback(object sender, DualShock4FeedbackReceivedEventArgs e) { rumble(e.LargeMotor, e.SmallMotor); }
        void OnX360Feedback(object sender, Xbox360FeedbackReceivedEventArgs e) { rumble(e.LargeMotor, e.SmallMotor); }

        // One DS4-layout report to the game (read thread)
        public void Submit(byte[] report)
        {
            try
            {
                IDualShock4Controller d = ds4; IXbox360Controller x = x360;
                if (d != null) d.SubmitRawReport(report);
                else if (x != null) FeedX360(x, report);
                submitFailed = false;
            }
            catch (Exception e)
            {
                // The virtual pad stopped taking reports (ViGEmBus gone, pad being switched): games get no input — say so once
                if (!submitFailed) { submitFailed = true; Log.Error("virtual pad report " + id, e); }
            }
        }

        // The DS4 report → the virtual Xbox 360: buttons, sticks (Y inverted) and triggers
        static void FeedX360(IXbox360Controller x, byte[] raw)
        {
            Pad p = Pad.Parse(raw);
            Btn b = p.Buttons;
            x.SetButtonState(Xbox360Button.A, (b & Btn.Cross) != 0);
            x.SetButtonState(Xbox360Button.B, (b & Btn.Circle) != 0);
            x.SetButtonState(Xbox360Button.X, (b & Btn.Square) != 0);
            x.SetButtonState(Xbox360Button.Y, (b & Btn.Triangle) != 0);
            x.SetButtonState(Xbox360Button.LeftShoulder, (b & Btn.L1) != 0);
            x.SetButtonState(Xbox360Button.RightShoulder, (b & Btn.R1) != 0);
            x.SetButtonState(Xbox360Button.Back, (b & Btn.Share) != 0);
            x.SetButtonState(Xbox360Button.Start, (b & Btn.Options) != 0);
            x.SetButtonState(Xbox360Button.LeftThumb, (b & Btn.L3) != 0);
            x.SetButtonState(Xbox360Button.RightThumb, (b & Btn.R3) != 0);
            x.SetButtonState(Xbox360Button.Guide, (b & Btn.PS) != 0);
            x.SetButtonState(Xbox360Button.Up, (b & Btn.Up) != 0);
            x.SetButtonState(Xbox360Button.Down, (b & Btn.Down) != 0);
            x.SetButtonState(Xbox360Button.Left, (b & Btn.Left) != 0);
            x.SetButtonState(Xbox360Button.Right, (b & Btn.Right) != 0);
            x.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(p.LX, false));
            x.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(p.LY, true));
            x.SetAxisValue(Xbox360Axis.RightThumbX, Axis(p.RX, false));
            x.SetAxisValue(Xbox360Axis.RightThumbY, Axis(p.RY, true));
            x.SetSliderValue(Xbox360Slider.LeftTrigger, p.L2);
            x.SetSliderValue(Xbox360Slider.RightTrigger, p.R2);
            x.SubmitReport();
        }

        // Stick byte 0..255 (128 = center) → Xbox short; invert — the Y axes (DS4 up = 0, Xbox up = +).
        // The halves are scaled separately (128 steps down, 127 up), so both full deflections reach the Xbox maximum.
        internal static short Axis(byte v, bool invert)
        {
            int d = v - 128;
            if (invert) d = -d;
            int s = d >= 0 ? d * 32767 / (invert ? 128 : 127) : d * 32768 / (invert ? 127 : 128);
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }
    }
}
