// <copyright file="Debug.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Windows;

    /// <summary>
    /// Central logging facility.
    ///
    /// Writes to TWO places:
    /// 1. Per-session per-thread diagnostics files under
    ///    %LocalAppData%\FileConverter\Diagnostics-HHmMsS\DiagnosticsN.log
    ///    (preserved for 1 day; used by the in-app diagnostics UI).
    /// 2. A persistent rolling log under
    ///    %LocalAppData%\FileConverter\Logs\FileConverter.log (+ .1 .. .9 rotation)
    ///    so users/support can inspect activity across sessions even if the
    ///    UI was closed. Each log line is timestamped and tagged with severity,
    ///    thread id and category.
    /// </summary>
    public static class Debug
    {
        public const string CatGeneral = "General";
        public const string CatConversion = "Conversion";
        public const string CatFFmpeg = "FFmpeg";
        public const string CatShell = "Shell";
        public const string CatSettings = "Settings";
        public const string CatLifecycle = "Lifecycle";

        private const long MaxLogFileBytes = 5 * 1024 * 1024; // 5 MB before rotation.
        private const int MaxRotatedLogs = 10;

        private static readonly string diagnosticsFolderPath;
        private static readonly string persistentLogPath;
        private static readonly object persistentLogLock = new object();
        private static readonly Dictionary<int, DiagnosticsData> diagnosticsDataById = new Dictionary<int, DiagnosticsData>();
        private static int threadCount = 0;
        private static readonly int mainThreadId = 0;

        static Debug()
        {
            Debug.mainThreadId = Thread.CurrentThread.ManagedThreadId;

            string path = FileConverterExtension.PathHelpers.GetUserDataFolderPath;

            // Clean old session diagnostics folders (older than 1 day).
            try
            {
                DateTime expirationDate = DateTime.Now.Subtract(new TimeSpan(1, 0, 0, 0));
                string[] diagnosticsDirectories = Directory.GetDirectories(path, "Diagnostics-*");
                foreach (string directory in diagnosticsDirectories)
                {
                    DateTime creationTime = Directory.GetCreationTime(directory);
                    if (creationTime < expirationDate)
                    {
                        try { Directory.Delete(directory, true); } catch { }
                    }
                }
            }
            catch { }

            string diagnosticsFolderName = $"Diagnostics-{DateTime.Now.Hour}h{DateTime.Now.Minute}m{DateTime.Now.Second}s";
            Debug.diagnosticsFolderPath = Path.Combine(path, diagnosticsFolderName);
            Debug.diagnosticsFolderPath = PathHelpers.GenerateUniquePath(Debug.diagnosticsFolderPath);
            Directory.CreateDirectory(Debug.diagnosticsFolderPath);

            // Persistent rolling log.
            string logsDir = Path.Combine(path, "Logs");
            try
            {
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }
            }
            catch { }

            Debug.persistentLogPath = Path.Combine(logsDir, "FileConverter.log");
            Debug.RotateIfNeeded();

            // Session banner.
            string version;
            try
            {
                version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
            }
            catch { version = "?"; }

            Debug.LogPersistent(
                "INFO",
                CatLifecycle,
                $"=== Session start === FileConverter v{version} | " +
                $"OS: {Environment.OSVersion.VersionString} x{(Environment.Is64BitOperatingSystem ? 64 : 32)} | " +
                $"Culture: {System.Globalization.CultureInfo.CurrentUICulture.Name} | " +
                $"TZ: {TimeZoneInfo.Local.Id} (UTC{TimeZoneInfo.Local.BaseUtcOffset.TotalHours:+#;-#;+0})");

            Debug.Log($"Diagnostics stored at path '{Debug.diagnosticsFolderPath}'");
            Debug.Log($"Persistent log: '{Debug.persistentLogPath}'");
        }

        public static int FirstErrorCode
        {
            get;
            private set;
        }

        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

        public static DiagnosticsData[] Data => Debug.diagnosticsDataById.Values.ToArray();

        public static string PersistentLogPath => Debug.persistentLogPath;

        public static string DiagnosticsFolderPath => Debug.diagnosticsFolderPath;

        /// <summary>
        /// Informational log in the default "General" category.
        /// </summary>
        public static void Log(string message)
        {
            Debug.LogInternal(error: false, "INFO", CatGeneral, message, ConsoleColor.White);
        }

        /// <summary>
        /// Informational log with an explicit category (e.g. CatFFmpeg, CatConversion).
        /// </summary>
        public static void Log(string category, string message)
        {
            Debug.LogInternal(error: false, "INFO", category, message, ConsoleColor.White);
        }

        /// <summary>
        /// Warning log (non-fatal anomaly worth reporting).
        /// </summary>
        public static void LogWarning(string category, string message)
        {
            Debug.LogInternal(error: false, "WARN", category, message, ConsoleColor.Yellow);
        }

        public static void Assert(bool condition)
        {
            if (!condition)
            {
                LogError("Assertion failed");
            }
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                LogError(message);
            }
        }

        /// <summary>
        /// Error log with UI notification (shows a message box).
        /// </summary>
        public static void LogError(string message)
        {
            try
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }

            Debug.LogInternal(error: true, "ERROR", CatGeneral, message, ConsoleColor.Red);
        }

        public static void LogError(int errorCode, string message)
        {
            if (Debug.FirstErrorCode == 0)
            {
                Debug.FirstErrorCode = errorCode;
            }

            Debug.LogError($"{message} (code 0x{errorCode:X})");
        }

        /// <summary>
        /// Silent error log (no message box). Use when the caller will present
        /// the failure itself or when UI is unavailable.
        /// </summary>
        public static void LogErrorSilent(string category, string message)
        {
            Debug.LogInternal(error: true, "ERROR", category, message, ConsoleColor.Red);
        }

        /// <summary>
        /// Log an exception with full stack trace to the persistent log.
        /// </summary>
        public static void LogException(string category, string context, Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{context}: {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine($"  --> Inner: {inner.GetType().FullName}: {inner.Message}");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }

            Debug.LogInternal(error: true, "ERROR", category, sb.ToString(), ConsoleColor.Red);
        }

        public static void Release()
        {
            Debug.LogPersistent("INFO", CatLifecycle, "=== Session end ===");
            Debug.Log("Diagnostics manager released correctly.");

            foreach (KeyValuePair<int, DiagnosticsData> kvp in Debug.diagnosticsDataById)
            {
                kvp.Value.Release();
            }

            Debug.diagnosticsDataById.Clear();
        }

        private static void LogInternal(bool error, string severity, string category, string log, ConsoleColor color)
        {
            DiagnosticsData diagnosticsData;

            Thread currentThread = Thread.CurrentThread;
            int threadId = currentThread.ManagedThreadId;

            // Display main thread logs in standard output.
            if (threadId == Debug.mainThreadId)
            {
                try
                {
                    Console.ForegroundColor = color;
                    if (error)
                    {
                        Console.Error.WriteLine(log);
                    }
                    else
                    {
                        Console.WriteLine(log);
                    }

                    Console.ResetColor();
                }
                catch { }
            }

            lock (Debug.diagnosticsDataById)
            {
                if (!Debug.diagnosticsDataById.TryGetValue(threadId, out diagnosticsData))
                {
                    string threadName = Debug.threadCount > 0 ? $"{currentThread.Name} ({Debug.threadCount})" : "Application";
                    diagnosticsData = new DiagnosticsData(threadName);
                    diagnosticsData.Initialize(Debug.diagnosticsFolderPath, threadId);
                    Debug.diagnosticsDataById.Add(threadId, diagnosticsData);
                    Debug.threadCount++;

                    StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs("Data"));
                }
            }

            // Per-thread session log (kept for backward compatibility with diagnostics UI).
            diagnosticsData.Log(log);

            // Persistent rolling log with timestamp + metadata.
            Debug.LogPersistent(severity, category, log, threadId);
        }

        /// <summary>
        /// Write a structured entry to the persistent rolling log.
        /// </summary>
        internal static void LogPersistent(string severity, string category, string message, int? threadId = null)
        {
            if (string.IsNullOrEmpty(Debug.persistentLogPath))
            {
                return;
            }

            try
            {
                string tid = threadId.HasValue
                    ? threadId.Value.ToString()
                    : Thread.CurrentThread.ManagedThreadId.ToString();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{severity,-5}] [T{tid,-3}] [{category,-11}] {message}";

                lock (Debug.persistentLogLock)
                {
                    File.AppendAllText(Debug.persistentLogPath, line + Environment.NewLine, Encoding.UTF8);
                    // Opportunistic rotation check (cheap stat every write).
                    RotateIfNeeded();
                }
            }
            catch { /* never let logging break the app */ }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(Debug.persistentLogPath))
                {
                    return;
                }

                var fi = new FileInfo(Debug.persistentLogPath);
                if (fi.Length < MaxLogFileBytes)
                {
                    return;
                }

                // Shift .N -> .N+1, drop the oldest.
                for (int i = MaxRotatedLogs - 1; i >= 1; i--)
                {
                    string src = $"{Debug.persistentLogPath}.{i}";
                    string dst = $"{Debug.persistentLogPath}.{i + 1}";
                    if (File.Exists(src))
                    {
                        if (File.Exists(dst)) { try { File.Delete(dst); } catch { } }
                        try { File.Move(src, dst); } catch { }
                    }
                }

                string rotatedFirst = $"{Debug.persistentLogPath}.1";
                if (File.Exists(rotatedFirst)) { try { File.Delete(rotatedFirst); } catch { } }
                try { File.Move(Debug.persistentLogPath, rotatedFirst); } catch { }
            }
            catch { }
        }
    }
}
