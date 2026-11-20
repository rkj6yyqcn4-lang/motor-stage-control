using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Program
{
    // Keep the program running as x64 because this DLL is USBDAQ_DLL_V12X64.dll.
    private const string DllPath = @"C:\Taoyi\驱动电机\USBDAQ_DLL_V12X64.dll";

    // ===== 1. X/Y axis motion parameters =====
    private const double XStartTimeSeconds = 10.0;
    private const int XDir = +1;
    private const int XFreqHz = 1500;
    private const double XDurationSeconds = 8.0;

    private const double YStartTimeSeconds = 0.0;
    private const int YDir = +1;
    private const int YFreqHz = 1000;
    private const double YDurationSeconds = 10.0;

    // ===== 2. Channel definitions =====
    private static readonly AxisConfig X = new("X", PwmChan: 1, DirDO: 2, DirInvert: false);
    private static readonly AxisConfig Y = new("Y", PwmChan: 2, DirDO: 4, DirInvert: false);

    // Common-anode wiring:
    // Run: 50% duty cycle.
    // Stop: 100% duty cycle, keeping PUL- high.
    private const float RunDuty = 50.0f;
    private const float StopDuty = 100.0f;

    private static int Main()
    {
        try
        {
            ValidateParameters();

            CheckReturn(OpenUsbV12(), "OpenUsbV12 failed");
            Console.WriteLine("DAQ card opened successfully.");

            try
            {
                InitializeStopped();
                Thread.Sleep(200);

                SetAxisDir(X, XDir);
                SetAxisDir(Y, YDir);
                Thread.Sleep(100);

                Console.WriteLine("Direction set. Starting scheduled motion.");
                ExecuteMotionSchedule();

                SetPWM(X.PwmChan, XFreqHz, StopDuty);
                SetPWM(Y.PwmChan, YFreqHz, StopDuty);

                Console.WriteLine("Dual-axis motion finished.");
                return 0;
            }
            finally
            {
                SafeClose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateParameters()
    {
        if (XFreqHz < 1 || YFreqHz < 1)
            throw new ArgumentOutOfRangeException(nameof(XFreqHz), "PWM frequency cannot be less than 1 Hz.");

        if (XDurationSeconds < 0 || YDurationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(XDurationSeconds), "Motion duration cannot be negative.");

        if (XStartTimeSeconds < 0 || YStartTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(XStartTimeSeconds), "Start time cannot be negative.");

        if (XDir != -1 && XDir != 1)
            throw new ArgumentOutOfRangeException(nameof(XDir), "XDir can only be +1 or -1.");

        if (YDir != -1 && YDir != 1)
            throw new ArgumentOutOfRangeException(nameof(YDir), "YDir can only be +1 or -1.");
    }

    private static void InitializeStopped()
    {
        SetPWM(X.PwmChan, 100, StopDuty);
        SetPWM(Y.PwmChan, 100, StopDuty);
    }

    private static void ExecuteMotionSchedule()
    {
        List<MotionEvent> events = new();

        if (XDurationSeconds > 0)
        {
            events.Add(new MotionEvent(XStartTimeSeconds, X, MotionAction.Start));
            events.Add(new MotionEvent(XStartTimeSeconds + XDurationSeconds, X, MotionAction.Stop));
        }

        if (YDurationSeconds > 0)
        {
            events.Add(new MotionEvent(YStartTimeSeconds, Y, MotionAction.Start));
            events.Add(new MotionEvent(YStartTimeSeconds + YDurationSeconds, Y, MotionAction.Stop));
        }

        events.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));

        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (MotionEvent motionEvent in events)
        {
            while (stopwatch.Elapsed.TotalSeconds < motionEvent.TimeSeconds)
            {
                Thread.Sleep(1);
            }

            if (motionEvent.Axis.Name == "X")
            {
                HandleAxisEvent(motionEvent, XFreqHz, XDir, stopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                HandleAxisEvent(motionEvent, YFreqHz, YDir, stopwatch.Elapsed.TotalSeconds);
            }
        }
    }

    private static void HandleAxisEvent(MotionEvent motionEvent, int freqHz, int dir, double elapsedSeconds)
    {
        if (motionEvent.Action == MotionAction.Start)
        {
            Console.WriteLine($"{elapsedSeconds:F3} s: {motionEvent.Axis.Name} axis start, freq {freqHz} Hz, dir {dir:+#;-#}");
            SetPWM(motionEvent.Axis.PwmChan, freqHz, RunDuty);
        }
        else
        {
            Console.WriteLine($"{elapsedSeconds:F3} s: {motionEvent.Axis.Name} axis stop");
            SetPWM(motionEvent.Axis.PwmChan, freqHz, StopDuty);
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
            throw new InvalidOperationException(
                $"PWMOutSetV12 failed: PWM{pwmChan}, freq={freq}, duty={duty:F1}, return={ret}");
        }
    }

    private static void WriteDO(int doChan, bool state)
    {
        int ret = DoSetV12((byte)doChan, state ? (byte)1 : (byte)0);
        if (ret != 0)
        {
            throw new InvalidOperationException(
                $"DoSetV12 failed: DO{doChan}, state={(state ? 1 : 0)}, return={ret}");
        }
    }

    private static void SafeClose()
    {
        try
        {
            PWMOutSetV12(X.PwmChan, 100, StopDuty);
            PWMOutSetV12(Y.PwmChan, 100, StopDuty);
            CloseUsbV12();
        }
        catch
        {
            // Best-effort shutdown only.
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

    private static void CheckReturn(int ret, string message)
    {
        if (ret != 0)
        {
            throw new InvalidOperationException($"{message}, return={ret}");
        }
    }

    private sealed record AxisConfig(string Name, int PwmChan, int DirDO, bool DirInvert);

    private sealed record MotionEvent(double TimeSeconds, AxisConfig Axis, MotionAction Action);

    private enum MotionAction
    {
        Start,
        Stop
    }
}
