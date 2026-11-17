using System.Threading;

namespace GpuMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\GpuMonitorNativeWidget", out var isFirstInstance);
        if (!isFirstInstance) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new GpuWidget());
    }
}
