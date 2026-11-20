using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DistanceMeassurementApp2
{
    internal sealed class StageController : IDisposable
    {
        private const string DllPath = @"C:\Taoyi\驱动电机\USBDAQ_DLL_V12X64.dll";
        private const float RunDuty = 50.0f;
        private const float StopDuty = 100.0f;

        private static readonly AxisConfig XAxis = new AxisConfig("X", 1, 2, false);
        private static readonly AxisConfig YAxis = new AxisConfig("Y", 2, 4, false);

        private readonly object syncRoot = new object();
        private bool isOpen;

        public bool IsOpen
        {
            get
            {
                lock (syncRoot)
                {
                    return isOpen;
                }
            }
        }

        public Task RunScheduleAsync(StageMotionSettings settings, Action<string> log, CancellationToken cancellationToken)
        {
            return Task.Run(delegate
            {
                RunSchedule(settings, log, cancellationToken);
            }, cancellationToken);
        }

        public void Stop()
        {
            lock (syncRoot)
            {
                if (!isOpen)
                {
                    return;
                }

                SafeStopNoThrow();
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void RunSchedule(StageMotionSettings settings, Action<string> log, CancellationToken cancellationToken)
        {
            Validate(settings);

            lock (syncRoot)
            {
                if (isOpen)
                {
                    throw new InvalidOperationException("位移台正在运行，请先停止当前运动。");
                }

                CheckReturn(OpenUsbV12(), "OpenUsbV12 打开失败");
                isOpen = true;
            }

            try
            {
                SetPWM(XAxis.PwmChan, 100, StopDuty);
                SetPWM(YAxis.PwmChan, 100, StopDuty);
                SleepWithCancel(200, cancellationToken);

                SetAxisDir(XAxis, settings.XDir);
                SetAxisDir(YAxis, settings.YDir);
                SleepWithCancel(100, cancellationToken);

                log("位移台方向设置完成，开始按时间表运动。");
                ExecuteEvents(settings, log, cancellationToken);

                SetPWM(XAxis.PwmChan, settings.XFreqHz, StopDuty);
                SetPWM(YAxis.PwmChan, settings.YFreqHz, StopDuty);
                log("位移台运动完成。");
            }
            catch (OperationCanceledException)
            {
                SafeStopNoThrow();
                log("位移台运动已停止。");
                throw;
            }
            finally
            {
                Close();
            }
        }

        private static void Validate(StageMotionSettings settings)
        {
            if (settings.XFreqHz < 1 || settings.YFreqHz < 1)
            {
                throw new ArgumentOutOfRangeException("PWM 频率不能小于 1 Hz。");
            }

            if (settings.XDurationSeconds < 0 || settings.YDurationSeconds < 0)
            {
                throw new ArgumentOutOfRangeException("运动持续时间不能为负数。");
            }

            if (settings.XStartTimeSeconds < 0 || settings.YStartTimeSeconds < 0)
            {
                throw new ArgumentOutOfRangeException("启动时间不能为负数。");
            }

            if ((settings.XDir != 1 && settings.XDir != -1) || (settings.YDir != 1 && settings.YDir != -1))
            {
                throw new ArgumentOutOfRangeException("方向只能填写 +1 或 -1。");
            }
        }

        private static void ExecuteEvents(StageMotionSettings settings, Action<string> log, CancellationToken cancellationToken)
        {
            List<MotionEvent> events = new List<MotionEvent>();

            if (settings.XDurationSeconds > 0)
            {
                events.Add(new MotionEvent(settings.XStartTimeSeconds, XAxis, MotionAction.Start));
                events.Add(new MotionEvent(settings.XStartTimeSeconds + settings.XDurationSeconds, XAxis, MotionAction.Stop));
            }

            if (settings.YDurationSeconds > 0)
            {
                events.Add(new MotionEvent(settings.YStartTimeSeconds, YAxis, MotionAction.Start));
                events.Add(new MotionEvent(settings.YStartTimeSeconds + settings.YDurationSeconds, YAxis, MotionAction.Stop));
            }

            events.Sort(delegate(MotionEvent left, MotionEvent right)
            {
                return left.TimeSeconds.CompareTo(right.TimeSeconds);
            });

            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (MotionEvent motionEvent in events)
            {
                while (stopwatch.Elapsed.TotalSeconds < motionEvent.TimeSeconds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(1);
                }

                int freqHz = motionEvent.Axis.Name == "X" ? settings.XFreqHz : settings.YFreqHz;
                int dir = motionEvent.Axis.Name == "X" ? settings.XDir : settings.YDir;

                if (motionEvent.Action == MotionAction.Start)
                {
                    log(string.Format("{0:F3} s：{1} 轴启动，频率 {2} Hz，方向 {3:+#;-#}",
                        stopwatch.Elapsed.TotalSeconds, motionEvent.Axis.Name, freqHz, dir));
                    SetPWM(motionEvent.Axis.PwmChan, freqHz, RunDuty);
                }
                else
                {
                    log(string.Format("{0:F3} s：{1} 轴停止",
                        stopwatch.Elapsed.TotalSeconds, motionEvent.Axis.Name));
                    SetPWM(motionEvent.Axis.PwmChan, freqHz, StopDuty);
                }
            }
        }

        private static void SetAxisDir(AxisConfig axis, int dirSign)
        {
            bool dirState = dirSign > 0;
            if (axis.DirInvert)
            {
                dirState = !dirState;
            }

            WriteDO(axis.DirDO, dirState);
        }

        private static void SetPWM(int pwmChan, int freq, float duty)
        {
            int ret = PWMOutSetV12(pwmChan, freq, duty);
            if (ret != 0)
            {
                throw new InvalidOperationException(string.Format(
                    "PWMOutSetV12 设置失败：PWM{0}, freq={1}, duty={2:F1}, return={3}",
                    pwmChan, freq, duty, ret));
            }
        }

        private static void WriteDO(int doChan, bool state)
        {
            int ret = DoSetV12((byte)doChan, state ? (byte)1 : (byte)0);
            if (ret != 0)
            {
                throw new InvalidOperationException(string.Format(
                    "DoSetV12 输出失败：DO{0}, state={1}, return={2}",
                    doChan, state ? 1 : 0, ret));
            }
        }

        private void Close()
        {
            lock (syncRoot)
            {
                if (!isOpen)
                {
                    return;
                }

                SafeStopNoThrow();
                CloseUsbV12();
                isOpen = false;
            }
        }

        private static void SafeStopNoThrow()
        {
            try
            {
                PWMOutSetV12(XAxis.PwmChan, 100, StopDuty);
                PWMOutSetV12(YAxis.PwmChan, 100, StopDuty);
            }
            catch
            {
            }
        }

        private static void SleepWithCancel(int milliseconds, CancellationToken cancellationToken)
        {
            int waited = 0;
            while (waited < milliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int step = Math.Min(20, milliseconds - waited);
                Thread.Sleep(step);
                waited += step;
            }
        }

        private static void CheckReturn(int ret, string message)
        {
            if (ret != 0)
            {
                throw new InvalidOperationException(string.Format("{0}，返回值={1}", message, ret));
            }
        }

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        private static extern int OpenUsbV12();

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        private static extern int CloseUsbV12();

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        private static extern int PWMOutSetV12(int chan, int freq, float dutyCycle);

        [DllImport(DllPath, CallingConvention = CallingConvention.StdCall)]
        private static extern int DoSetV12(byte chan, byte state);

        private sealed class AxisConfig
        {
            public AxisConfig(string name, int pwmChan, int dirDO, bool dirInvert)
            {
                Name = name;
                PwmChan = pwmChan;
                DirDO = dirDO;
                DirInvert = dirInvert;
            }

            public string Name { get; private set; }

            public int PwmChan { get; private set; }

            public int DirDO { get; private set; }

            public bool DirInvert { get; private set; }
        }

        private sealed class MotionEvent
        {
            public MotionEvent(double timeSeconds, AxisConfig axis, MotionAction action)
            {
                TimeSeconds = timeSeconds;
                Axis = axis;
                Action = action;
            }

            public double TimeSeconds { get; private set; }

            public AxisConfig Axis { get; private set; }

            public MotionAction Action { get; private set; }
        }

        private enum MotionAction
        {
            Start,
            Stop
        }
    }

    internal sealed class StageMotionSettings
    {
        public double XStartTimeSeconds { get; set; }

        public int XDir { get; set; }

        public int XFreqHz { get; set; }

        public double XDurationSeconds { get; set; }

        public double YStartTimeSeconds { get; set; }

        public int YDir { get; set; }

        public int YFreqHz { get; set; }

        public double YDurationSeconds { get; set; }
    }
}
