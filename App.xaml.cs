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
            // Another instance of LyricalWidget is already running; cleanly exit to avoid duplicate overlays
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Elevate process priority safely to AboveNormal to prevent scheduling starvation while gaming
        try
        {
            using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
            currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to elevate process priority: {ex.Message}");
        }

        // Disable Windows 11 EcoQoS background power throttling so execution speed is not throttled
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
