using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZStage
{
    // Signatures copied from the supplied USBDAQ_DLL_V12.h; x64 process only.
    public interface IBoard
    {
        int Open();
        int Close();
        int Pwm(int channel, int frequency, float duty);
        int Direction(byte channel, byte level);
    }

    internal sealed class UsbBoard : IBoard
    {
        private const string Dll = "USBDAQ_DLL_V12X64.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int OpenUsbV12();
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int CloseUsbV12();
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int PWMOutSetV12(int chan, int freq, float dutyCycle);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int DoSetV12(byte chan, byte state);
        public int Open() { return OpenUsbV12(); }
        public int Close() { return CloseUsbV12(); }
        public int Pwm(int channel, int frequency, float duty) { return PWMOutSetV12(channel, frequency, duty); }
        public int Direction(byte channel, byte level) { return DoSetV12(channel, level); }
    }

    public enum StageState { Disconnected, Connecting, Idle, Preparing, Moving, Fault }

    // Exactly one worker owns all board calls. No USB call or motion wait runs on the UI thread.
    public sealed class StageController
    {
        private sealed class Move
        {
            public bool Up;
            public bool Invert;
            public int Frequency;
            public double Seconds;
            public long Revision;
        }

        private readonly IBoard board;
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Stopwatch duration = new Stopwatch();
        private readonly Thread worker;
        private Move pending;
        private bool connectPending, disconnectPending, stopPending;
        private bool opened, moving, fault;
        private volatile bool quit;
        private long revision;
        private int frequency = 200;
        private double deadline;
        public event Action<StageState, string> Changed;
        public StageController(IBoard board)
        {
            this.board = board;
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "Z stage USB worker";
            worker.Start();
        }
        public void Connect()
        {
            lock (gate) { connectPending = true; }
            wake.Set();
        }
        public void MoveAxis(bool up, bool invert, int hz, double seconds)
        {
            if (hz < 1 || hz > 10000 || seconds < 0.1 || seconds > 60 || Double.IsNaN(seconds))
                throw new ArgumentOutOfRangeException("运动参数超出允许范围");
            lock (gate)
            {
                pending = new Move { Up = up, Invert = invert, Frequency = hz, Seconds = seconds, Revision = ++revision };
            }
            wake.Set();
        }
        public void Stop()
        {
            lock (gate) { ++revision; pending = null; stopPending = true; }
            wake.Set();
        }
        public void Disconnect()
        {
            lock (gate)
            {
                ++revision; pending = null; connectPending = false;
                stopPending = true; disconnectPending = true;
            }
            wake.Set();
        }
        // Only call after a confirmed Disconnected state. Never dispose while PWM may be active.
        public bool Finish(int timeoutMs)
        {
            quit = true; wake.Set();
            return worker.Join(timeoutMs);
        }
        private void Publish(StageState state, string message)
        {
            Action<StageState, string> handler = Changed;
            if (handler != null) handler(state, message);
        }
        private bool Cancelled(long token)
        {
            lock (gate) { return token != revision || stopPending || disconnectPending; }
        }
        private static void Check(int result, string api)
        {
            if (result != 0) throw new InvalidOperationException(api + " 返回错误码 " + result);
        }
        private bool StopBoard(bool publishIdle = true)
        {
            if (!opened) return true;
            try
            {
                // Common anode: PUL- high, optocoupler off. Do not touch direction or other axes.
                Check(board.Pwm(1, frequency, 100f), "PWMOutSetV12(停止)");
                moving = false;
                duration.Reset();
                if (fault) Publish(StageState.Fault, "停止命令已成功发送；请断开后重新连接以清除故障。");
                else if (publishIdle) Publish(StageState.Idle, "已停止：PWM1 = 100%（高电平）");
                return true;
            }
            catch (Exception ex)
            {
                fault = true;
                Publish(StageState.Fault, "停止未确认！请使用硬件急停或切断驱动器动力。" + ex.Message + "；可再次点击停止重试。");
                return false;
            }
        }
        private void OpenBoard()
        {
            if (opened) return;
            Publish(StageState.Connecting, "正在连接板卡…");
            try
            {
                Check(board.Open(), "OpenUsbV12");
                opened = true; fault = false;
                StopBoard();
            }
            catch (Exception ex)
            {
                Publish(StageState.Disconnected, "连接失败：" + ex.Message);
            }
        }
        private void CloseBoard()
        {
            if (!opened) { Publish(StageState.Disconnected, "板卡已断开"); return; }
            // Keep the handle and window alive if stopping fails; never report a false safe close.
            if (!StopBoard(false)) return;
            try
            {
                Check(board.Close(), "CloseUsbV12");
                opened = false; fault = false;
                Publish(StageState.Disconnected, "已发送停止命令并断开板卡");
            }
            catch (Exception ex)
            {
                fault = true;
                Publish(StageState.Fault, "关闭板卡失败：" + ex.Message + "；请重试断开。");
            }
        }
        private void StartMove(Move move)
        {
            if (!opened || fault || Cancelled(move.Revision)) return;
            if (!StopBoard(false) || Cancelled(move.Revision)) return;
            try
            {
                Publish(StageState.Preparing, "正在建立方向信号…");
                byte level = (byte)((move.Up ^ move.Invert) ? 1 : 0);
                Check(board.Direction(2, level), "DoSetV12(OUT3/DO2)");
                Stopwatch setup = Stopwatch.StartNew();
                while (setup.ElapsedMilliseconds < 50)
                {
                    if (Cancelled(move.Revision)) return;
                    wake.WaitOne(2);
                }
                if (Cancelled(move.Revision)) return;
                frequency = move.Frequency;
                deadline = move.Seconds;
                duration.Restart();
                // A stop that arrives during this native call is serviced immediately after it returns.
                moving = true;
                Check(board.Pwm(1, frequency, 50f), "PWMOutSetV12(运动)");
                if (Cancelled(move.Revision)) { StopBoard(); return; }
                Publish(StageState.Moving, (move.Up ? "上移" : "下移") + "中：" + frequency + " Hz；最长 " + move.Seconds.ToString("0.0") + " 秒");
            }
            catch (Exception ex)
            {
                fault = true;
                string error = "运动故障：" + ex.Message;
                bool stopped = StopBoard();
                Publish(StageState.Fault, error + (stopped ? "；停止命令已发送，请重新连接。" : "；停止未确认，请立即硬件急停！"));
            }
        }
        private void Loop()
        {
            while (!quit)
            {
                int action = 0;
                Move move = null;
                lock (gate)
                {
                    if (stopPending) { stopPending = false; action = 1; }
                    else if (disconnectPending) { disconnectPending = false; action = 2; }
                    else if (connectPending) { connectPending = false; action = 3; }
                    else if (pending != null) { move = pending; pending = null; action = 4; }
                }
                if (action == 1) StopBoard();
                else if (action == 2) CloseBoard();
                else if (action == 3) OpenBoard();
                else if (action == 4) StartMove(move);
                if (moving && !fault && duration.Elapsed.TotalSeconds >= deadline) StopBoard();
                if (action == 0) wake.WaitOne(5);
            }
        }
    }
}
