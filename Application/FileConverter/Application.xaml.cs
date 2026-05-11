// <copyright file="Application.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*  File Converter - This program allow you to convert file format to another.
    Copyright (C) 2026 Adrien Allard
    email: adrien.allard.pro@gmail.com

    This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or any later version.

    This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Threading;
    using System.Windows;

    using System.Globalization;
    using System.Reflection;

    using CommunityToolkit.Mvvm.DependencyInjection;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;
    using FileConverter.ViewModels;
    using FileConverter.Views;
    using Microsoft.Extensions.DependencyInjection;
    using Debug = FileConverter.Diagnostics.Debug;

    public partial class Application : System.Windows.Application
    {
        private static readonly Version Version = new Version()
                                                      {
                                                          Major = 2,
                                                          Minor = 2,
                                                          Patch = 9,
                                                      };

        private bool needToRunConversionThread;
        private volatile bool cancelAutoExit;
        private bool isSessionEnding;
        private bool verbose;
        private bool silent;
        private bool showSettings;
        private bool showHelp;
        private volatile System.Windows.Forms.NotifyIcon trayIcon;

        // ── new CLI flags ──────────────────────────────────────────────────
        /// <summary>--output-dir: write all output files into this directory.</summary>
        private string outputDirectory;
        /// <summary>--wait: block process until all conversions finish; exit code = 0/1.</summary>
        private bool waitMode;
        /// <summary>--progress: write machine-readable progress key=value to stdout.</summary>
        private bool progressMode;
        /// <summary>--list-presets: print available presets then exit.</summary>
        private bool listPresetsMode;
        /// <summary>--probe: print media info for the given input files then exit.</summary>
        private bool probeMode;
        /// <summary>--output-format: "text" (default) or "json" for --list-presets/--probe.</summary>
        private string outputFormat = "text";

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleOutputCP(uint wCodePageID);

        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;
        const int STD_OUTPUT_HANDLE = -11;
        const int STD_ERROR_HANDLE  = -12;

        public event EventHandler<ApplicationTerminateArgs> OnApplicationTerminate;

        public static Version ApplicationVersion => Application.Version;

        public static bool IsInAdmininstratorPrivileges
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
        }

        public void CancelAutoExit()
        {
            this.cancelAutoExit = true;

            if (this.OnApplicationTerminate != null)
            {
                this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(float.NaN));
            }
        }

        public static void AskForShutdown()
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown(Debug.FirstErrorCode)));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Redirect standard output to the parent process in case the application is launch from command line.
            AttachConsole(ATTACH_PARENT_PROCESS);

            // Register a handler so that ResourceManager can locate satellite assemblies
            // placed under the legacy "Languages\<culture>\" folder used by the WiX installer.
            // Without this, ResourceManager only probes "<exe>\<culture>\" which does not exist
            // in the installed layout, causing all UI strings to fall back to English.
            AppDomain.CurrentDomain.AssemblyResolve += this.OnAssemblyResolve;

            // Handle non-UI commands early, BEFORE any WPF initialization (theme, DI, etc).
            // This is critical because --register-shell-extension runs as SYSTEM via MSI
            // deferred CA, where WPF/HKCU operations may fail silently.
            if (this.HandleEarlyCommandLineArgs())
            {
                return;
            }

            // Single-instance gate. If another instance is already running,
            // forward our payload (preset + file list) to it over a named pipe
            // and exit silently — the primary instance will append the new jobs
            // to its existing conversion queue, so the user sees ONE window
            // instead of multiple cluttered ones.
            if (!SingleInstanceManager.TryAcquirePrimary())
            {
                this.ForwardArgumentsToPrimaryAndExit();
                return;
            }

            // Apply dark theme if Windows is in dark mode.
            this.ApplySystemTheme();

            this.RegisterServices();

            this.Initialize();

            // Listen for subsequent instances forwarding their payload to us.
            SingleInstanceManager.PayloadReceived += this.OnSingleInstancePayloadReceived;
            SingleInstanceManager.StartPipeListener();

            // Navigate to the wanted view.
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            if (this.showHelp)
            {
                navigationService.Show(Pages.Help);
                return;
            }

            if (this.needToRunConversionThread)
            {
                // ── --wait mode: headless blocking, no window or tray ─────────────────
                // Blocks the main thread until all conversions finish, then exits
                // with code 0 (all succeeded) or 1 (any failure).
                if (this.waitMode)
                {
                    var done = new System.Threading.ManualResetEventSlim(false);
                    bool allOk = true;

                    IConversionService waitConvService = Ioc.Default.GetRequiredService<IConversionService>();
                    waitConvService.ConversionJobsTerminated += (sender, waitArgs) =>
                    {
                        allOk = waitArgs.AllConversionsSucceed;
                        done.Set();
                    };
                    waitConvService.ConvertFilesAsync();

                    // Block without pumping WPF messages — this is a CLI-style invocation.
                    done.Wait();
                    Environment.Exit(allOk ? 0 : 1);
                    return;
                }

                ISettingsService traySettingsService = Ioc.Default.GetRequiredService<ISettingsService>();
                bool minimizeToTray = traySettingsService.Settings?.MinimizeToTray == true;

                if (!this.silent && !minimizeToTray)
                {
                    navigationService.Show(Pages.Main);
                }
                else if (minimizeToTray)
                {
                    this.InitializeTrayIcon();
                }

                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
                conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
                conversionService.ConvertFilesAsync();

                // In silent mode, force auto-exit when all conversions finish.
                if (this.silent)
                {
                    this.cancelAutoExit = false;
                }

                // Start tray progress update timer if in tray mode.
                if (minimizeToTray)
                {
                    this.StartTrayProgressUpdater(conversionService);
                }
            }

            if (this.showSettings)
            {
                navigationService.Show(Pages.Settings);
            }

            if (this.verbose)
            {
                navigationService.Show(Pages.Diagnostics);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            this.DisposeTrayIcon();

            try { SingleInstanceManager.Shutdown(); } catch { }

            Debug.Log("Exit application.");

            try
            {
                IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();

                if (!this.isSessionEnding && upgradeService.UpgradeVersionDescription != null && upgradeService.UpgradeVersionDescription.NeedToUpgrade)
                {
                    Debug.Log($"A new version of file converter has been found: {upgradeService.UpgradeVersionDescription.LatestVersion}.");

                    if (string.IsNullOrEmpty(upgradeService.UpgradeVersionDescription.InstallerPath))
                    {
                        Debug.LogError("Invalid installer path.");
                    }
                    else
                    {
                        Debug.Log("Wait for the end of the installer download.");
                        int maxWaitSeconds = 60;
                        while (upgradeService.UpgradeVersionDescription.InstallerDownloadInProgress && maxWaitSeconds > 0)
                        {
                            Thread.Sleep(1000);
                            maxWaitSeconds--;
                        }

                        if (maxWaitSeconds <= 0)
                        {
                            Debug.LogError("Timed out waiting for installer download.");
                        }
                        else
                        {
                            string installerPath = upgradeService.UpgradeVersionDescription.InstallerPath;
                            if (!System.IO.File.Exists(installerPath))
                            {
                                Debug.LogError($"Can't find upgrade installer ({installerPath}). Try to restart the application.");
                            }
                            else
                            {
                                Debug.Log($"Start file converter upgrade from version {ApplicationVersion} to {upgradeService.UpgradeVersionDescription.LatestVersion}.");

                                ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true, };

                                Debug.Log($"Start upgrade process: {System.IO.Path.GetFileName(startInfo.FileName)}{startInfo.Arguments}.");
                                Process process = new System.Diagnostics.Process { StartInfo = startInfo };

                                process.Start();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Error during exit: {ex.Message}");
            }

            Debug.Release();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);

            this.isSessionEnding = true;
            this.Shutdown();
        }

        private void RegisterServices()
        {
            var services = new ServiceCollection();

            if (this.TryFindResource("Locator") is ViewModelLocator viewModelLocator)
            {
                viewModelLocator.RegisterViewModels(services);
            }
            else
            {
                Debug.LogError("Can't retrieve view model locator.");
                Application.AskForShutdown();
                return;
            }

            if (this.TryFindResource("Upgrade") is UpgradeService upgradeService)
            {
                services.AddSingleton<IUpgradeService>(upgradeService);
            }
            else
            {
                Debug.LogError("Can't retrieve Upgrade service.");
                Application.AskForShutdown();
                return;
            }

            services
              .AddSingleton<INavigationService, NavigationService>()
              .AddSingleton<IConversionService, ConversionService>()
              .AddSingleton<ISettingsService, SettingsService>();

            Ioc.Default.ConfigureServices(services.BuildServiceProvider());

            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            navigationService.RegisterPage<HelpWindow>(Pages.Help, false, true);
            navigationService.RegisterPage<MainWindow>(Pages.Main, false, true);
            navigationService.RegisterPage<SettingsWindow>(Pages.Settings, true, true);
            navigationService.RegisterPage<DiagnosticsWindow>(Pages.Diagnostics, true, false);
            navigationService.RegisterPage<UpgradeWindow>(Pages.Upgrade, true, false);
        }

        private void Initialize()
        {
#if BUILD32
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (32 bits)");
#else
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (64 bits)");
#endif

            // Retrieve arguments.
            Debug.Log("Retrieve arguments...");
            string[] args = Environment.GetCommandLineArgs();

            // Log arguments.
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                Debug.Log($"Arg{index}: {argument}");
            }

            Debug.Log(string.Empty);

            if (args.Length == 1)
            {
                // Display help windows to explain that this application is a context menu extension.
                this.showHelp = true;
                return;
            }

            // Parse arguments.
            List<string> filePaths = new List<string>();
            string conversionPresetName = null;
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith("--"))
                {
                    // This is an optional parameter.
                    string parameterTitle = argument.Substring(2).ToLowerInvariant();

                    switch (parameterTitle)
                    {
                        case "post-install-init":
                            try
                            {
                                ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
                                if (!settingsService.PostInstallationInitialization())
                                {
                                    Diagnostics.Debug.Log("PostInstallInit returned false (non-fatal, install continues).");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Diagnostics.Debug.Log($"PostInstallInit error (non-fatal): {ex.Message}");
                            }

                            Application.AskForShutdown();
                            return;

                        case "remove-user-data":
                            // Called by the MSI uninstaller to delete user settings.
                            try
                            {
                                string userDataPath = FileConverterExtension.PathHelpers.GetUserDataFolderPath;
                                if (System.IO.Directory.Exists(userDataPath))
                                {
                                    System.IO.Directory.Delete(userDataPath, true);
                                    Debug.Log($"User data folder deleted: {userDataPath}");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.Log($"Failed to delete user data: {ex.Message}");
                            }

                            Application.AskForShutdown();
                            return;

                        case "register-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0B, $"Invalid format.");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.RegisterShellExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0C, $"Failed to register shell extension {shellExtensionPath}.");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "unregister-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0D, $"Invalid format.");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.UnregisterExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0E, $"Failed to unregister shell extension {shellExtensionPath}.");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "version":
                            Console.WriteLine(ApplicationVersion.ToString());
                            Application.AskForShutdown();
                            return;

                        case "settings":
                            this.showSettings = true;
                            break;

                        case "conversion-preset":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x01, $"Invalid format.");
                                Application.AskForShutdown();
                                return;
                            }

                            conversionPresetName = args[index + 1];
                            index++;
                            break;

                        case "input-files":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x02, $"Invalid format.");
                                Application.AskForShutdown();
                                return;
                            }

                            string fileListPath = args[index + 1];
                            try
                            {
                                using (FileStream file = File.OpenRead(fileListPath))
                                using (StreamReader reader = new StreamReader(file))
                                {
                                    while (!reader.EndOfStream)
                                    {
                                        filePaths.Add(reader.ReadLine());
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogError(errorCode: 0x03, $"Can't read input files list: {exception}");
                                Application.AskForShutdown();
                                return;
                            }

                            index++;
                            break;

                        case "verbose":
                            {
                                this.verbose = true;
                            }

                            break;

                        case "silent":
                            // Hidden mode: run conversions without showing the main window (#117).
                            this.silent = true;
                            break;

                        case "output-dir":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x06, "--output-dir requires a directory path argument.");
                                Application.AskForShutdown();
                                return;
                            }

                            this.outputDirectory = args[index + 1];
                            index++;
                            break;

                        case "wait":
                            // Block the process until all conversions finish; exit code = 0 (success) or 1 (any failure).
                            this.waitMode = true;
                            break;

                        case "progress":
                            // Write machine-readable key=value progress lines to stdout.
                            this.progressMode = true;
                            break;

                        case "list-presets":
                            // Print all available presets to stdout then exit.
                            this.listPresetsMode = true;
                            break;

                        case "probe":
                            // Print media file info to stdout then exit.
                            this.probeMode = true;
                            break;

                        case "output-format":
                            // "text" (default) or "json" — affects --list-presets and --probe output.
                            if (index < args.Length - 1)
                            {
                                this.outputFormat = args[index + 1].ToLowerInvariant();
                                index++;
                            }

                            break;

                        default:
                            Debug.LogError($"Unknown application argument: '--{parameterTitle}'.");
                            return;
                    }
                }
                else
                {
                    filePaths.Add(argument);
                }
            }

            this.RunConversions(filePaths, conversionPresetName, this.outputDirectory);
        }

        private void RunConversions(List<string> filePaths, string conversionPresetName, string outputDirectory = null)
        {
            // ── silent console for CLI output modes ───────────────────────────────────
            // When outputting structured data to stdout (--probe, --list-presets,
            // --progress), any stray Debug.Log() output would corrupt the caller's
            // parser. Suppress all console output; everything still goes to the log file.
            if (this.probeMode || this.listPresetsMode || this.progressMode)
            {
                Diagnostics.Debug.SilentConsoleMode = true;

                // WPF applications use /SUBSYSTEM:WINDOWS so their Console.Out is not
                // wired to the process's stdout file descriptor.  AttachConsole() fixes
                // interactive TTYs, but not PowerShell pipes.  Explicitly redirect
                // Console.Out / Console.Error to the real standard handles so that our
                // structured output reaches the caller even through a pipe.
                try
                {
                    SetConsoleOutputCP(65001); // UTF-8
                    var stdoutHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(
                        GetStdHandle(STD_OUTPUT_HANDLE), ownsHandle: false);
                    if (!stdoutHandle.IsInvalid)
                    {
                        var writer = new System.IO.StreamWriter(
                            new System.IO.FileStream(stdoutHandle, System.IO.FileAccess.Write),
                            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                        { AutoFlush = true };
                        Console.SetOut(writer);
                    }

                    var stderrHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(
                        GetStdHandle(STD_ERROR_HANDLE), ownsHandle: false);
                    if (!stderrHandle.IsInvalid)
                    {
                        var errWriter = new System.IO.StreamWriter(
                            new System.IO.FileStream(stderrHandle, System.IO.FileAccess.Write),
                            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                        { AutoFlush = true };
                        Console.SetError(errWriter);
                    }
                }
                catch { /* best effort — Console output may still not work in all hosts */ }
            }
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            if (settingsService.Settings == null)
            {
                Debug.LogError(errorCode: 0x04, "Can't load File Converter settings. The application will now shutdown, if you want to fix the problem yourself please edit or delete the file: C:\\Users\\UserName\\AppData\\Local\\FileConverter\\Settings.user.xml.");
                Application.AskForShutdown();
                return;
            }

            // ── --list-presets: print available presets to stdout then exit ────────────
            if (this.listPresetsMode)
            {
                var presets = settingsService.Settings?.ConversionPresets;
                if (presets == null || presets.Count == 0)
                {
                    Console.Error.WriteLine("No presets found.");
                    Environment.Exit(1);
                    return;
                }

                if (this.outputFormat == "json")
                {
                    Console.Write("[");
                    bool first = true;
                    foreach (var p in presets)
                    {
                        if (!first) { Console.Write(","); }

                        first = false;
                        string inputs = string.Join(",", p.InputTypes ?? new System.Collections.Generic.List<string>());
                        Console.Write(
                            $"{{\"name\":{EscapeJson(p.FullName)}," +
                            $"\"outputType\":\"{p.OutputType}\"," +
                            $"\"inputTypes\":\"{inputs}\"}}");
                    }

                    Console.WriteLine("]");
                }
                else
                {
                    foreach (var p in presets)
                    {
                        string inputs = string.Join(",", p.InputTypes ?? new System.Collections.Generic.List<string>());
                        Console.WriteLine($"{p.FullName,-55} -> {p.OutputType,-8}  [{inputs}]");
                    }
                }

                Console.Out.Flush();
                Environment.Exit(0);
                return;
            }

            // ── --probe: print media info for each input file then exit ──────────────
            if (this.probeMode)
            {
                foreach (string file in filePaths)
                {
                    var r = Diagnostics.MediaProber.Probe(file);
                    if (this.outputFormat == "json")
                    {
                        Console.WriteLine(
                            $"{{\"file\":{EscapeJson(file)}," +
                            $"\"duration_sec\":{r.DurationSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
                            $"\"width\":{r.Width}," +
                            $"\"height\":{r.Height}," +
                            $"\"video_codec\":{EscapeJson(r.VideoCodec)}," +
                            $"\"audio_codec\":{EscapeJson(r.AudioCodec)}," +
                            $"\"bitrate_kbps\":{r.BitrateKbps}," +
                            $"\"size_bytes\":{r.SizeBytes}}}");
                    }
                    else
                    {
                        var dur = TimeSpan.FromSeconds(r.DurationSec);
                        Console.WriteLine($"File       : {file}");
                        Console.WriteLine($"Duration   : {dur.Hours:D2}:{dur.Minutes:D2}:{dur.Seconds:D2}");
                        Console.WriteLine($"Video      : {r.VideoCodec}  {r.Width}x{r.Height}");
                        Console.WriteLine($"Audio      : {r.AudioCodec}");
                        Console.WriteLine($"Bitrate    : {r.BitrateKbps} kb/s");
                        Console.WriteLine($"Size       : {r.SizeBytes / 1024.0 / 1024.0:F2} MB");
                        Console.WriteLine();
                    }
                }

                Console.Out.Flush();
                Environment.Exit(0);
                return;
            }

            // ── --progress: enable ProgressWriter ────────────────────────────────────
            if (this.progressMode)
            {
                Diagnostics.ProgressWriter.IsEnabled = true;
            }

            Debug.Assert(Debug.FirstErrorCode == 0, "An error happened during the initialization.");

            // Check for upgrade.
            if (settingsService.Settings.CheckUpgradeAtStartup)
            {
                IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
                upgradeService.NewVersionAvailable += this.UpgradeService_NewVersionAvailable;
                upgradeService.CheckForUpgrade();
            }

            ConversionPreset conversionPreset = null;
            if (!string.IsNullOrEmpty(conversionPresetName))
            {
                conversionPreset = settingsService.Settings.GetPresetFromName(conversionPresetName);
                if (conversionPreset == null)
                {
                    Debug.LogError(errorCode: 0x02, $"Invalid conversion preset '{conversionPresetName}'.");
                    Application.AskForShutdown();
                    return;
                }
            }

            if (conversionPreset != null)
            {
                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

                // Create output directory if --output-dir was specified.
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(outputDirectory);
                        Debug.Log(Debug.CatConversion, $"--output-dir: using directory '{outputDirectory}'");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"--output-dir: failed to create directory '{outputDirectory}': {ex.Message}");
                        Application.AskForShutdown();
                        return;
                    }
                }

                // Create conversion jobs.
                Debug.Log($"Create jobs for conversion preset: '{conversionPreset.FullName}'");
                try
                {
                    for (int index = 0; index < filePaths.Count; index++)
                    {
                        string inputFilePath = filePaths[index];
                        ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, inputFilePath);

                        // Apply --output-dir override so PrepareConversion places
                        // the output file in the requested directory.
                        if (!string.IsNullOrEmpty(outputDirectory))
                        {
                            conversionJob.OutputDirectoryOverride = outputDirectory;
                        }

                        conversionService.RegisterConversionJob(conversionJob);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                    throw;
                }

                this.needToRunConversionThread = true;
            }
        }

        private static string EscapeJson(string s) =>
            "\"" + (s ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r") + "\"";

        private void UpgradeService_NewVersionAvailable(object sender, UpgradeVersionDescription e)
        {
            Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Upgrade);

            IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
            upgradeService.NewVersionAvailable -= this.UpgradeService_NewVersionAvailable;
        }

        private void ConversionService_ConversionJobsTerminated(object sender, ConversionJobsTerminatedEventArgs e)
        {
            // NOTE: Do NOT unsubscribe here — in single-instance mode, additional
            // right-click launches enqueue new jobs via OnSingleInstancePayloadReceived,
            // which re-starts the conversion loop. That loop fires this event each
            // time its current queue completes, so we must remain subscribed.
            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            // Show tray notification if enabled.
            if (settingsService.Settings.NotifyOnComplete && this.trayIcon != null)
            {
                string title = e.AllConversionsSucceed ? "✅ Conversion Complete / 转换完成" : "⚠️ Conversion Finished with Errors / 转换完成（有错误）";
                string body = $"{conversionService.ConversionJobs.Count} file(s) processed.";
                this.trayIcon.ShowBalloonTip(5000, title, body, 
                    e.AllConversionsSucceed ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning);
            }
            else if (settingsService.Settings.NotifyOnComplete && settingsService.Settings.MinimizeToTray)
            {
                // Even without tray icon active, show notification if setting is on.
                this.Dispatcher.BeginInvoke((System.Action)(() =>
                {
                    string msg = e.AllConversionsSucceed
                        ? "✅ All conversions completed successfully!\n所有转换已成功完成！"
                        : "⚠️ Conversions finished with errors.\n转换完成，但有错误。";
                    System.Windows.MessageBox.Show(msg, "File Converter",
                        System.Windows.MessageBoxButton.OK,
                        e.AllConversionsSucceed ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
                }));
            }

            // Play sound if enabled.
            if (settingsService.Settings.PlaySoundOnComplete)
            {
                try
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
                catch (System.Exception ex)
                {
                    Debug.Log($"Failed to play completion sound: {ex.Message}");
                }
            }

            // NOTE: We intentionally do NOT dispose the tray icon here — if the
            // user manually minimized to tray, they expect it to remain available
            // to restore the window, independently of whether their jobs finished.

            // If there is no main window visible AND auto-exit is enabled, honor
            // the legacy shutdown-on-idle behaviour. Otherwise keep the process
            // alive so follow-up right-click launches can enqueue more files.
            if (!settingsService.Settings.ExitApplicationWhenConversionsFinished)
            {
                return;
            }
            
            if (this.cancelAutoExit)
            {
                return;
            }

            if (e.AllConversionsSucceed)
            {
                float remainingTime = settingsService.Settings.DurationBetweenEndOfConversionsAndApplicationExit;
                while (remainingTime > 0f)
                {
                    if (this.OnApplicationTerminate != null)
                    {
                        this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                    }

                    Thread.Sleep(1000);
                    remainingTime--;

                    if (this.cancelAutoExit)
                    {
                        return;
                    }
                }

                if (this.OnApplicationTerminate != null)
                {
                    this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                }

                Application.AskForShutdown();
            }
        }

        /// <summary>
        /// Exit the process cleanly without going through WPF's OnExit path.
        ///
        /// OnExit calls Ioc.Default.GetRequiredService&lt;IUpgradeService&gt;(), which
        /// throws InvalidOperationException when RegisterServices() was never
        /// called (as is the case for --register-shell-extension and friends).
        /// That unhandled exception surfaces as CLR exit code 0xE0434352
        /// (-532462766) in the MSI log and is confusing to operators.
        ///
        /// Using Environment.Exit bypasses WPF shutdown entirely, producing a
        /// clean exit code 0.
        /// </summary>
        private static void ExitEarlyProcess()
        {
            Environment.Exit(0);
        }

        /// <summary>
        /// Parse command-line args early (before WPF theme/DI init) and handle
        /// non-UI commands that should run without full application setup.
        /// Returns true if the app should exit immediately.
        /// </summary>
        private bool HandleEarlyCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument) || !argument.StartsWith("--"))
                {
                    continue;
                }

                string parameterTitle = argument.Substring(2).ToLowerInvariant();

                switch (parameterTitle)
                {
                    case "register-shell-extension":
                        if (index < args.Length - 1)
                        {
                            string shellExtensionPath = args[index + 1];
                            if (!Helpers.RegisterShellExtension(shellExtensionPath))
                            {
                                Debug.LogError(errorCode: 0x0C, $"Failed to register shell extension {shellExtensionPath}.");
                            }
                        }

                        ExitEarlyProcess();
                        return true;

                    case "unregister-shell-extension":
                        if (index < args.Length - 1)
                        {
                            string shellExtensionPath = args[index + 1];
                            if (!Helpers.UnregisterExtension(shellExtensionPath))
                            {
                                Debug.LogError(errorCode: 0x0E, $"Failed to unregister shell extension {shellExtensionPath}.");
                            }
                        }

                        ExitEarlyProcess();
                        return true;

                    case "remove-user-data":
                        try
                        {
                            string userDataPath = FileConverterExtension.PathHelpers.GetUserDataFolderPath;
                            if (System.IO.Directory.Exists(userDataPath))
                            {
                                System.IO.Directory.Delete(userDataPath, true);
                            }
                        }
                        catch
                        {
                        }

                        ExitEarlyProcess();
                        return true;

                    case "version":
                        Console.WriteLine(ApplicationVersion.ToString());
                        ExitEarlyProcess();
                        return true;
                }
            }

            return false;
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Use the application icon for the tray.
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Drawing.Icon appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                this.trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = appIcon,
                    Text = "File Converter",
                    Visible = true,
                };

                // Right-click menu: Show window / Exit.
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Show Window / 显示窗口", null, (s, e) =>
                {
                    this.Dispatcher.BeginInvoke((System.Action)(() => this.RestoreFromTray()));
                });
                contextMenu.Items.Add("Exit / 退出", null, (s, e) =>
                {
                    this.DisposeTrayIcon();
                    Application.AskForShutdown();
                });
                this.trayIcon.ContextMenuStrip = contextMenu;

                // Single left click to show window (preferred UX per #tray-button).
                this.trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        this.Dispatcher.BeginInvoke((System.Action)(() => this.RestoreFromTray()));
                    }
                };

                // Keep legacy double-click behaviour as a no-op-safe fallback.
                this.trayIcon.DoubleClick += (s, e) =>
                {
                    this.Dispatcher.BeginInvoke((System.Action)(() => this.RestoreFromTray()));
                };

                Debug.Log("Tray icon initialized (minimize to tray mode).");
            }
            catch (System.Exception ex)
            {
                Debug.Log($"Failed to initialize tray icon: {ex.Message}");
            }
        }

        private void StartTrayProgressUpdater(IConversionService conversionService)
        {
            Thread progressThread = Helpers.InstantiateThread("TrayProgressThread", () =>
            {
                while (true)
                {
                    try
                    {
                        var jobs = conversionService.ConversionJobs;
                        if (jobs == null || jobs.Count == 0)
                        {
                            break;
                        }

                        int totalJobs = jobs.Count;
                        int doneJobs = 0;
                        int failedJobs = 0;
                        string currentJobName = string.Empty;
                        float currentJobProgress = 0f;
                        float totalProgress = 0f;

                        for (int i = 0; i < totalJobs; i++)
                        {
                            var job = jobs[i];
                            if (job.State == ConversionJobs.ConversionState.Done)
                            {
                                doneJobs++;
                                totalProgress += 1f;
                            }
                            else if (job.State == ConversionJobs.ConversionState.Failed)
                            {
                                failedJobs++;
                                totalProgress += 1f;
                            }
                            else if (job.State == ConversionJobs.ConversionState.InProgress)
                            {
                                currentJobName = System.IO.Path.GetFileName(job.InputFilePath);
                                currentJobProgress = job.Progress;
                                totalProgress += job.Progress;
                            }
                        }

                        bool allFinished = (doneJobs + failedJobs) >= totalJobs;

                        int overallPercent = totalJobs > 0 ? (int)(totalProgress / totalJobs * 100) : 0;
                        int currentPercent = (int)(currentJobProgress * 100);

                        // Update tray tooltip (max 63 chars for NotifyIcon.Text).
                        string tooltip = $"File Converter: {overallPercent}% ({doneJobs}/{totalJobs})";
                        if (!string.IsNullOrEmpty(currentJobName))
                        {
                            string shortName = currentJobName.Length > 20 ? currentJobName.Substring(0, 17) + "..." : currentJobName;
                            tooltip += $"\n{shortName}: {currentPercent}%";
                        }

                        if (tooltip.Length > 63)
                        {
                            tooltip = tooltip.Substring(0, 63);
                        }

                        if (this.trayIcon != null)
                        {
                            this.trayIcon.Text = tooltip;
                        }

                        if (allFinished)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }

                    Thread.Sleep(500);
                }
            });
            progressThread.Start();
        }

        private void DisposeTrayIcon()
        {
            var icon = this.trayIcon;
            this.trayIcon = null;
            if (icon != null)
            {
                try
                {
                    icon.Visible = false;
                    icon.Dispose();
                }
                catch
                {
                    // Ignore disposal errors (icon may already be disposed by another thread).
                }
            }
        }

        /// <summary>
        /// Hide the main window and show the tray icon. Used by the new
        /// "minimize to tray" title-bar button in MainWindow. Safe to call
        /// multiple times.
        /// </summary>
        public void MinimizeToTray()
        {
            this.Dispatcher.VerifyAccess();

            if (this.trayIcon == null)
            {
                this.InitializeTrayIcon();
            }

            foreach (Window w in this.Windows)
            {
                if (w is Views.MainWindow)
                {
                    w.Hide();
                }
            }

            // Cancel any pending auto-exit countdown triggered by ConversionJobsTerminated.
            this.cancelAutoExit = true;
        }

        /// <summary>
        /// Show the main window and hide the tray icon. Used by tray
        /// single/double-click and the context-menu "Show Window" item.
        /// </summary>
        public void RestoreFromTray()
        {
            this.Dispatcher.VerifyAccess();

            INavigationService nav = Ioc.Default.GetRequiredService<INavigationService>();
            nav.Show(Pages.Main);

            foreach (Window w in this.Windows)
            {
                if (w is Views.MainWindow mw)
                {
                    if (mw.WindowState == WindowState.Minimized)
                    {
                        mw.WindowState = WindowState.Normal;
                    }

                    mw.Show();
                    mw.Activate();
                    mw.Topmost = true;
                    mw.Topmost = false;
                    mw.Focus();
                }
            }

            this.DisposeTrayIcon();
        }

        /// <summary>
        /// Secondary-instance code path: a first instance is already running.
        /// Parse our own command line looking for --conversion-preset, the
        /// positional file arguments and --input-files, then push them through
        /// the named pipe to the primary. Exit silently afterwards.
        /// </summary>
        private void ForwardArgumentsToPrimaryAndExit()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string preset = null;
                var filePaths = new List<string>();

                for (int i = 1; i < args.Length; i++)
                {
                    string a = args[i];
                    if (string.IsNullOrEmpty(a))
                    {
                        continue;
                    }

                    if (a.StartsWith("--"))
                    {
                        string key = a.Substring(2).ToLowerInvariant();
                        if (key == "conversion-preset" && i < args.Length - 1)
                        {
                            preset = args[i + 1];
                            i++;
                        }
                        else if (key == "input-files" && i < args.Length - 1)
                        {
                            string listPath = args[i + 1];
                            i++;
                            try
                            {
                                foreach (string line in File.ReadAllLines(listPath))
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        filePaths.Add(line);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.Log($"Secondary instance: failed to read input list '{listPath}': {ex.Message}");
                            }
                        }
                        // Silently ignore other flags (--settings, --verbose, etc).
                    }
                    else
                    {
                        filePaths.Add(a);
                    }
                }

                if (filePaths.Count == 0)
                {
                    // No files to forward — just tell the primary to surface its window.
                    // Sending an empty payload with preset="__FC_SHOW_WINDOW__" signals focus.
                    SingleInstanceManager.SendPayloadToPrimary("__FC_SHOW_WINDOW__", new List<string> { "__focus__" });
                }
                else
                {
                    SingleInstanceManager.SendPayloadToPrimary(preset, filePaths);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"ForwardArgumentsToPrimaryAndExit failed: {ex.Message}");
            }

            // Bypass WPF OnExit (no DI registered) to get a clean exit code.
            Environment.Exit(0);
        }

        /// <summary>
        /// Primary instance: another copy was launched and pushed its payload
        /// to us. Create conversion jobs on the UI thread, append them to the
        /// existing queue, and resume the conversion loop. Also surfaces the
        /// window so the user sees the updated queue.
        /// </summary>
        private void OnSingleInstancePayloadReceived(object sender, SingleInstancePayload payload)
        {
            this.Dispatcher.BeginInvoke((System.Action)(() =>
            {
                try
                {
                    // Special "bring to front" signal from a secondary instance
                    // launched with no files (e.g. user double-clicked the exe).
                    if (payload.ConversionPresetName == "__FC_SHOW_WINDOW__")
                    {
                        this.RestoreFromTray();
                        return;
                    }

                    ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
                    if (settingsService.Settings == null)
                    {
                        Debug.LogError("Secondary-instance payload received but settings are unavailable.");
                        return;
                    }

                    ConversionPreset preset = null;
                    if (!string.IsNullOrEmpty(payload.ConversionPresetName))
                    {
                        preset = settingsService.Settings.GetPresetFromName(payload.ConversionPresetName);
                    }

                    if (preset == null)
                    {
                        Debug.LogWarning(Debug.CatLifecycle, $"Secondary-instance payload: unknown preset '{payload.ConversionPresetName}', dropping {payload.FilePaths.Count} file(s).");
                        return;
                    }

                    IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
                    ViewModels.MainViewModel mainVm = this.TryFindResource("Locator") is ViewModelLocator loc ? loc.Main : null;

                    int added = 0;
                    foreach (string path in payload.FilePaths)
                    {
                        try
                        {
                            ConversionJobs.ConversionJob job = ConversionJobFactory.Create(preset, path);
                            conversionService.RegisterConversionJob(job);
                            mainVm?.AddExternalJob(job);
                            added++;
                        }
                        catch (Exception ex)
                        {
                            Debug.Log($"Failed to create job for '{path}': {ex.Message}");
                        }
                    }

                    if (added > 0)
                    {
                        // Kick the conversion service again — if the previous run finished
                        // it will start processing the newly added jobs.
                        conversionService.ConvertFilesAsync();
                        this.cancelAutoExit = true; // More work just arrived.
                    }

                    // Bring the main window up (or show it if hidden to tray).
                    this.RestoreFromTray();
                }
                catch (Exception ex)
                {
                    Debug.LogException(Debug.CatLifecycle, "OnSingleInstancePayloadReceived dispatcher body", ex);
                }
            }));
        }

        private void ApplySystemTheme()
        {
            try
            {
                // Check Windows registry for dark mode preference.
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int intValue && intValue == 0)
                        {
                            // Dark mode is active — swap the Colors.xaml resource dictionary.
                            var mergedDicts = this.Resources.MergedDictionaries;
                            for (int i = 0; i < mergedDicts.Count; i++)
                            {
                                var dict = mergedDicts[i];
                                if (dict.Source != null && dict.Source.OriginalString.Contains("Colors.xaml")
                                    && !dict.Source.OriginalString.Contains("Dark"))
                                {
                                    mergedDicts[i] = new ResourceDictionary
                                    {
                                        Source = new Uri("Views/Resources/DarkColors.xaml", UriKind.Relative)
                                    };

                                    Debug.Log("Dark theme applied (Windows dark mode detected).");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to detect system theme: {ex.Message}");
            }
        }

        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // ResourceManager requests satellite assemblies with names like
            // "FileConverter.resources, Version=..., Culture=ja-jp, ...".
            // By default .NET probes "<exe>\ja-jp\FileConverter.resources.dll",
            // but the WiX installer places them under "<exe>\Languages\ja-jp\".
            // We bridge the gap here.
            try
            {
                AssemblyName requestedName = new AssemblyName(args.Name);
                if (!requestedName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                CultureInfo culture = requestedName.CultureInfo;
                if (culture == null || string.IsNullOrEmpty(culture.Name))
                {
                    return null;
                }

                string exeDir = Path.GetDirectoryName(Uri.UnescapeDataString(
                    new UriBuilder(Assembly.GetExecutingAssembly().CodeBase).Path));

                string satellitePath = Path.Combine(exeDir, "Languages", culture.Name,
                    requestedName.Name + ".dll");

                if (File.Exists(satellitePath))
                {
                    return Assembly.LoadFrom(satellitePath);
                }
            }
            catch (Exception ex)
            {
                // Log but do not break the resolve chain.
                System.Diagnostics.Trace.WriteLine($"AssemblyResolve failed for '{args.Name}': {ex.Message}");
            }

            return null;
        }
    }
}
