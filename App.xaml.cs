using System;
using System.Threading;
using System.Windows;

namespace LyricalWidget;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Global\LyricalWidget_SingleInstance_Mutex";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Above normal so the overlay is not starved while a game has the CPU
        try
        {
            using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
            currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to elevate process priority: {ex.Message}");
        }

        // Opt out of Windows 11 EcoQoS, which would throttle a background overlay
        LyricalWidget.Interop.NativeMethods.DisableProcessPowerThrottling();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }
}
