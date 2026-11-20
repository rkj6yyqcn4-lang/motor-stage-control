using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZStage
{
    internal static class ControllerTests
    {
        private sealed class FakeBoard : IBoard
        {
            public readonly List<string> Calls = new List<string>();
            public readonly ManualResetEvent StartEntered = new ManualResetEvent(false);
            public readonly ManualResetEvent ReleaseStart = new ManualResetEvent(true);
            public volatile bool FailStop;
            public volatile bool FailDirection;
            private int Add(string text) { lock (Calls) Calls.Add(text); return 0; }
            public int Open() { return Add("open"); }
            public int Close() { return Add("close"); }
            public int Pwm(int ch, int freq, float duty)
            {
                Add("pwm:" + ch + ":" + freq + ":" + duty);
                if (duty == 50) { StartEntered.Set(); ReleaseStart.WaitOne(); }
                return duty == 100 && FailStop ? -7 : 0;
            }
            public int Direction(byte ch, byte state) { Add("dir:" + ch + ":" + state); return FailDirection ? -9 : 0; }
            public int Count(string prefix) { lock (Calls) return Calls.FindAll(delegate(string s) { return s.StartsWith(prefix); }).Count; }
            public string[] Snapshot() { lock (Calls) return Calls.ToArray(); }
        }
        private sealed class Rig : IDisposable
        {
            public readonly FakeBoard Board = new FakeBoard();
            public readonly StageController Controller;
            public volatile StageState State = StageState.Disconnected;
            public volatile string Message = "";
            public Rig()
            {
                Controller = new StageController(Board);
                Controller.Changed += delegate(StageState s, string m) { Message = m; State = s; };
                Controller.Connect(); Wait(delegate { return State == StageState.Idle; }, "connect");
            }
            public void Dispose()
            {
                Board.ReleaseStart.Set(); Board.FailStop = false; Board.FailDirection = false;
                Controller.Disconnect(); Wait(delegate { return State == StageState.Disconnected; }, "disconnect");
                Controller.Finish(1000);
            }
        }
        private static void Assert(bool ok, string text) { if (!ok) throw new Exception(text); }
        private static void Wait(Func<bool> predicate, string text)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!predicate()) { if (watch.ElapsedMilliseconds > 2500) throw new Exception("Timeout: " + text); Thread.Sleep(2); }
        }
        public static int Run(string[] args)
        {
            List<string> report = new List<string>();
            int result = 0;
            try
            {
                using (Rig r = new Rig())
                {
                    Assert(r.Board.Count("pwm:1:200:100") == 1, "connect must stop PWM1");
                    r.Controller.MoveAxis(true, false, 200, 1);
                    Wait(delegate { return r.State == StageState.Moving; }, "up start");
                    Assert(r.Board.Count("dir:2:1") == 1, "OUT3 must map to DO2 high for up");
                    Stopwatch sw = Stopwatch.StartNew(); r.Controller.Stop();
                    Wait(delegate { return r.State == StageState.Idle; }, "stop");
                    report.Add("PASS: connect high-level stop, PWM1/DO2 mapping, stop during motion (fake board " + sw.ElapsedMilliseconds + " ms; not hardware latency).");
                    r.Controller.MoveAxis(false, false, 300, 0.1);
                    Wait(delegate { return r.State == StageState.Moving; }, "down");
                    Assert(r.Board.Count("dir:2:0") == 1, "down level");
                    Wait(delegate { return r.State == StageState.Idle; }, "timed stop");
                    r.Controller.MoveAxis(true, true, 200, 0.1);
                    Wait(delegate { return r.State == StageState.Moving; }, "inverted up");
                    Assert(r.Board.Count("dir:2:0") == 2, "inverted level");
                    r.Controller.Stop(); Wait(delegate { return r.State == StageState.Idle; }, "stop inverted");
                    report.Add("PASS: down direction, invert direction, duration auto-stop.");
                }
                using (Rig r = new Rig())
                {
                    r.Controller.MoveAxis(true, false, 200, 1);
                    Wait(delegate { return r.Board.Count("dir:") > 0; }, "direction setup");
                    r.Controller.Stop(); Thread.Sleep(100);
                    Assert(r.Board.Count("pwm:1:200:50") == 0, "stop during setup must cancel start");
                    report.Add("PASS: stop cancels the 50 ms direction setup; no delayed restart.");
                }
                using (Rig r = new Rig())
                {
                    r.Board.ReleaseStart.Reset();
                    r.Controller.MoveAxis(true, false, 200, 1);
                    Assert(r.Board.StartEntered.WaitOne(2000), "start native call");
                    Stopwatch sw = Stopwatch.StartNew(); r.Controller.Stop();
                    Assert(sw.ElapsedMilliseconds < 100, "UI stop request blocked on native call");
                    r.Board.ReleaseStart.Set();
                    Wait(delegate { return r.State == StageState.Idle; }, "stop after blocked call");
                    string[] calls = r.Board.Snapshot();
                    Assert(calls[calls.Length - 1] == "pwm:1:200:100", "stop must follow blocked start");
                    report.Add("PASS: stop request remains nonblocking during a stalled native start; stop serviced when native call returns.");
                }
                using (Rig r = new Rig())
                {
                    r.Controller.MoveAxis(true, false, 200, 1);
                    Wait(delegate { return r.State == StageState.Moving; }, "fault test start");
                    r.Board.FailStop = true; r.Controller.Disconnect();
                    Wait(delegate { return r.State == StageState.Fault; }, "stop fault");
                    Thread.Sleep(100);
                    Assert(r.Board.Count("close") == 0, "must not close when stop unconfirmed");
                    int starts = r.Board.Count("pwm:1:200:50");
                    r.Controller.MoveAxis(true, false, 200, 1); Thread.Sleep(100);
                    Assert(r.Board.Count("pwm:1:200:50") == starts, "fault must reject motion");
                    r.Board.FailStop = false; r.Controller.Stop();
                    Wait(delegate { return r.Message.Contains("重新连接"); }, "stop retry");
                    Assert(r.State == StageState.Fault, "fault must remain latched");
                    r.Controller.Disconnect(); Wait(delegate { return r.State == StageState.Disconnected; }, "fault recovery");
                    string[] calls = r.Board.Snapshot();
                    Assert(calls[calls.Length - 2] == "pwm:1:200:100" && calls[calls.Length - 1] == "close", "stop before close");
                    report.Add("PASS: failed stop blocks close and motion; retry and stop-before-close order.");
                }
                using (Rig r = new Rig())
                {
                    r.Board.FailDirection = true;
                    r.Controller.MoveAxis(true, false, 200, 1);
                    Wait(delegate { return r.State == StageState.Fault; }, "direction fault");
                    Assert(r.Board.Count("pwm:1:200:50") == 0, "direction failure must prevent PWM start");
                    bool rejected = false;
                    try { r.Controller.MoveAxis(true, false, 0, 1); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Assert(rejected, "invalid frequency");
                    report.Add("PASS: direction API failure prevents motion; invalid parameters rejected.");
                }
                if (args.Length > 2)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-32000, -32000);
                        form.ShowInTaskbar = false;
                        form.Show(); Application.DoEvents(); form.PerformLayout();
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
                        form.Close();
                        Stopwatch close = Stopwatch.StartNew();
                        while (!form.IsDisposed && close.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(2); }
                    }
                    report.Add("PASS: WinForms layout rendered without opening the board.");
                }
                report.Add("No real board was opened. Physical direction, stopping latency, travel limits and driver dependencies require hardware validation.");
            }
            catch (Exception ex) { result = 1; report.Add("FAIL: " + ex); }
            if (args.Length > 1) File.WriteAllLines(args[1], report.ToArray(), System.Text.Encoding.UTF8);
            return result;
        }
    }
}
