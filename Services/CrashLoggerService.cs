using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace NoteDraw.Services
{
    /// <summary>
    /// Configures log4net file-based crash and error logging into `<exe>/Logs/`.
    /// Captures unhandled AppDomain, UI dispatcher, task scheduler, and startup crashes.
    /// </summary>
    public static class CrashLoggerService
    {
        private static readonly object SyncRoot = new();
        private static bool _isInitialized;
        private static bool _handlersRegistered;
        private static ILog? _logger;

        public static string LogsDirectoryPath =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Logs"));

        public static string ActiveLogsDirectory { get; private set; } = LogsDirectoryPath;
        public static string CurrentLogFilePath { get; private set; } = string.Empty;

        public static void Initialize(string? logsDirectoryOverride = null)
        {
            lock (SyncRoot)
            {
                string targetDir = !string.IsNullOrWhiteSpace(logsDirectoryOverride)
                    ? Path.GetFullPath(logsDirectoryOverride)
                    : LogsDirectoryPath;

                Directory.CreateDirectory(targetDir);

                var repository = LogManager.GetRepository(typeof(CrashLoggerService).Assembly);
                if (repository is Hierarchy hierarchy)
                {
                    hierarchy.Root.RemoveAllAppenders();

                    var patternLayout = new PatternLayout
                    {
                        ConversionPattern = "%date{yyyy-MM-dd HH:mm:ss.fff} [%-5level] [Thread:%thread] %logger - %message%newline%exception"
                    };
                    patternLayout.ActivateOptions();

                    var rollingAppender = new RollingFileAppender
                    {
                        Name = "NoteDrawCrashFileAppender",
                        File = Path.Combine(targetDir, "notedraw-"),
                        DatePattern = "yyyy-MM-dd'.log'",
                        StaticLogFileName = false,
                        AppendToFile = true,
                        RollingStyle = RollingFileAppender.RollingMode.Composite,
                        MaxSizeRollBackups = 14,
                        MaximumFileSize = "10MB",
                        ImmediateFlush = true,
                        LockingModel = new FileAppender.MinimalLock(),
                        Layout = patternLayout
                    };
                    rollingAppender.ActivateOptions();

                    hierarchy.Root.AddAppender(rollingAppender);
                    hierarchy.Root.Level = Level.Error;
                    hierarchy.Configured = true;
                }

                _logger = LogManager.GetLogger(typeof(CrashLoggerService));
                ActiveLogsDirectory = targetDir;
                CurrentLogFilePath = Path.Combine(targetDir, $"notedraw-{DateTime.Now:yyyy-MM-dd}.log");
                _isInitialized = true;
            }
        }

        public static void RegisterGlobalExceptionHandlers()
        {
            lock (SyncRoot)
            {
                if (_handlersRegistered)
                    return;

                _handlersRegistered = true;

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    var ex = e.ExceptionObject as Exception
                             ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled AppDomain exception");
                    LogFatalCrash("AppDomain.UnhandledException", ex, e.IsTerminating);
                };

                TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    LogError("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved();
                };

                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    LogFatalCrash("Avalonia.Dispatcher.UIThread.UnhandledException", e.Exception, isTerminating: !e.Handled);
                };
            }
        }

        public static void LogError(string context, Exception? ex)
        {
            lock (SyncRoot)
            {
                if (!_isInitialized || _logger is null)
                    Initialize();

                if (ex is not null)
                    _logger?.Error($"Application error in {context}: {ex.Message}", ex);
                else
                    _logger?.Error($"Application error in {context}.");

                LogManager.Flush(2000);
            }
        }

        public static void LogFatalCrash(string source, Exception? ex, bool isTerminating = true)
        {
            lock (SyncRoot)
            {
                if (!_isInitialized || _logger is null)
                    Initialize();

                string crashHeader =
                    $"FATAL CRASH in {source} | IsTerminating={isTerminating} | OS={RuntimeInformation.OSDescription} | Runtime={RuntimeInformation.FrameworkDescription} | BaseDir={AppContext.BaseDirectory}";

                if (ex is not null)
                    _logger?.Fatal(crashHeader, ex);
                else
                    _logger?.Fatal(crashHeader);

                LogManager.Flush(2000);
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                try
                {
                    LogManager.Flush(2000);
                    LogManager.Shutdown();
                }
                catch
                {
                    // Best-effort log4net teardown
                }
                finally
                {
                    _logger = null;
                    _isInitialized = false;
                }
            }
        }
    }
}
