using Avalonia;
using NoteDraw.Services;
using System;
using System.Linq;

namespace NoteDraw
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                BuildAvaloniaApp().SetupWithoutStarting();
                return SelfTestRunner.RunAll();
            }

            CrashLoggerService.Initialize();
            CrashLoggerService.RegisterGlobalExceptionHandlers();

            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                CrashLoggerService.LogFatalCrash("Program.Main", ex, isTerminating: true);
                throw;
            }
            finally
            {
                CrashLoggerService.Shutdown();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
