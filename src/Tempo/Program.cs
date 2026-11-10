using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Tempo;

/// <summary>
/// Custom entry point that enforces a single running instance. A second launch redirects its
/// activation to the first instance (which surfaces its window) and exits, so alerts are never
/// scheduled by two processes at once.
/// </summary>
public static class Program
{
    private const string InstanceKey = "Tempo-{8E0F7A12-Main-Instance}";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!DecideRedirection())
            return 0; // another instance is primary; we redirected to it

        Application.Start(p =>
        {
            try
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            }
            catch (Exception ex)
            {
                Tempo.Services.Log.Error("Application.Start", ex);
                throw;
            }
        });

        return 0;
    }

    private static bool DecideRedirection()
    {
        try
        {
            var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (keyInstance.IsCurrent)
            {
                keyInstance.Activated += OnActivated;
                return true;
            }

            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance.RedirectActivationToAsync(activationArgs).AsTask().Wait();
            return false;
        }
        catch
        {
            // If single-instance registration fails for any reason, run normally rather than block the user.
            return true;
        }
    }

    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        App.Instance?.OnRedirectedActivation();
    }
}
