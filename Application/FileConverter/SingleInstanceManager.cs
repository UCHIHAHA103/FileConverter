// <copyright file="SingleInstanceManager.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipes;
    using System.Security.Principal;
    using System.Threading;

    using Debug = FileConverter.Diagnostics.Debug;

    /// <summary>
    /// Ensures only one instance of File Converter is running in interactive mode.
    /// If a second instance is launched (e.g. right-click context menu), it
    /// forwards its command-line payload (preset + file list) to the first
    /// instance through a named pipe, then exits silently.
    ///
    /// The primary instance listens on the pipe in a background thread and
    /// raises <see cref="PayloadReceived"/> on arrival. The hosting application
    /// is responsible for enqueuing the files into its conversion service.
    /// </summary>
    public static class SingleInstanceManager
    {
        // Per-user pipe name (includes SID so different users on same machine do not collide).
        private const string PipeNameFormat = "FileConverter.SingleInstance.{0}";

        // Mutex scoped to the current interactive session.
        private const string MutexNameFormat = "FileConverter.SingleInstance.Mutex.{0}";

        private static Mutex mutex;
        private static Thread listenerThread;
        private static volatile bool listenerShouldStop;

        /// <summary>
        /// Raised on the primary instance's listener thread whenever another
        /// instance sends its command-line payload. Handlers MUST marshal to
        /// the UI thread themselves.
        /// </summary>
        public static event EventHandler<SingleInstancePayload> PayloadReceived;

        public static string PipeName
        {
            get
            {
                string sid = WindowsIdentity.GetCurrent().User?.Value ?? "default";
                return string.Format(PipeNameFormat, sid);
            }
        }

        private static string MutexName
        {
            get
            {
                string sid = WindowsIdentity.GetCurrent().User?.Value ?? "default";
                return string.Format(MutexNameFormat, sid);
            }
        }

        /// <summary>
        /// Attempts to acquire the single-instance mutex.
        /// Returns true when this is the primary (first) instance and should
        /// start the full application UI. Returns false if another instance
        /// already owns the mutex; the caller should then call
        /// <see cref="SendPayloadToPrimary"/> and exit.
        /// </summary>
        public static bool TryAcquirePrimary()
        {
            try
            {
                bool createdNew;
                SingleInstanceManager.mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);
                if (!createdNew)
                {
                    // Another instance already owns the mutex — we are the secondary.
                    try { SingleInstanceManager.mutex.Dispose(); } catch { }
                    SingleInstanceManager.mutex = null;
                    return false;
                }

                Debug.Log(Debug.CatLifecycle, $"Single-instance: acquired primary mutex '{MutexName}'.");
                return true;
            }
            catch (AbandonedMutexException)
            {
                // Previous owner crashed — we take over.
                Debug.LogWarning(Debug.CatLifecycle, "Single-instance: previous instance abandoned mutex; taking over as primary.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(Debug.CatLifecycle, "Single-instance: TryAcquirePrimary failed, degrading to multi-instance", ex);
                return true; // Fail open: behave like before.
            }
        }

        /// <summary>
        /// Starts the background pipe listener. Only call this on the primary instance.
        /// </summary>
        public static void StartPipeListener()
        {
            if (SingleInstanceManager.listenerThread != null)
            {
                return;
            }

            SingleInstanceManager.listenerShouldStop = false;
            SingleInstanceManager.listenerThread = new Thread(SingleInstanceManager.PipeListenerLoop)
            {
                Name = "SingleInstance.PipeListener",
                IsBackground = true,
            };
            SingleInstanceManager.listenerThread.Start();
            Debug.Log(Debug.CatLifecycle, $"Single-instance: listening on pipe '{PipeName}'.");
        }

        public static void Shutdown()
        {
            SingleInstanceManager.listenerShouldStop = true;

            try
            {
                // Kick the listener out of WaitForConnection.
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(200);
                }
            }
            catch { /* best effort */ }

            try
            {
                SingleInstanceManager.mutex?.ReleaseMutex();
            }
            catch { }

            try
            {
                SingleInstanceManager.mutex?.Dispose();
            }
            catch { }

            SingleInstanceManager.mutex = null;
        }

        /// <summary>
        /// Called on the secondary instance. Serializes the given payload
        /// (preset name + input files) and writes it to the primary's pipe.
        /// Returns true on success.
        /// </summary>
        public static bool SendPayloadToPrimary(string conversionPresetName, IList<string> filePaths, int timeoutMs = 2000)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                return false;
            }

            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(timeoutMs);
                    using (var writer = new StreamWriter(client, new System.Text.UTF8Encoding(false)) { AutoFlush = true })
                    {
                        // Line 1: protocol version
                        writer.WriteLine("FC-SINGLE-INSTANCE-V1");
                        // Line 2: preset name (may be empty)
                        writer.WriteLine(conversionPresetName ?? string.Empty);
                        // Line 3: file count
                        writer.WriteLine(filePaths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        // Lines 4..N: one file path per line
                        foreach (string path in filePaths)
                        {
                            writer.WriteLine(path ?? string.Empty);
                        }
                        // Line N+1: EOF marker
                        writer.WriteLine("FC-EOF");
                    }
                }

                Debug.Log(Debug.CatLifecycle, $"Single-instance: forwarded {filePaths.Count} file(s) to primary (preset='{conversionPresetName}').");
                return true;
            }
            catch (TimeoutException)
            {
                Debug.LogWarning(Debug.CatLifecycle, $"Single-instance: timed out connecting to primary pipe '{PipeName}'.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogException(Debug.CatLifecycle, "Single-instance: failed to send payload to primary", ex);
                return false;
            }
        }

        private static void PipeListenerLoop()
        {
            while (!SingleInstanceManager.listenerShouldStop)
            {
                try
                {
                    // Allow Everyone on the local machine (same interactive session) to connect.
                    using (var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        server.WaitForConnection();

                        if (SingleInstanceManager.listenerShouldStop)
                        {
                            break;
                        }

                        using (var reader = new StreamReader(server, new System.Text.UTF8Encoding(false)))
                        {
                            string header = reader.ReadLine();
                            if (header != "FC-SINGLE-INSTANCE-V1")
                            {
                                Debug.LogWarning(Debug.CatLifecycle, $"Single-instance: rejected payload with bad header '{header}'.");
                                continue;
                            }

                            string presetName = reader.ReadLine() ?? string.Empty;
                            string countLine = reader.ReadLine();
                            if (!int.TryParse(countLine, out int fileCount) || fileCount < 0 || fileCount > 100000)
                            {
                                Debug.LogWarning(Debug.CatLifecycle, $"Single-instance: bad file count '{countLine}'.");
                                continue;
                            }

                            var paths = new List<string>(fileCount);
                            for (int i = 0; i < fileCount; i++)
                            {
                                string p = reader.ReadLine();
                                if (!string.IsNullOrEmpty(p))
                                {
                                    paths.Add(p);
                                }
                            }

                            string eof = reader.ReadLine();
                            if (eof != "FC-EOF")
                            {
                                Debug.LogWarning(Debug.CatLifecycle, $"Single-instance: missing EOF marker (got '{eof}').");
                                continue;
                            }

                            Debug.Log(Debug.CatLifecycle,
                                $"Single-instance: received payload (preset='{presetName}', files={paths.Count}).");

                            try
                            {
                                PayloadReceived?.Invoke(null, new SingleInstancePayload(presetName, paths));
                            }
                            catch (Exception ex)
                            {
                                Debug.LogException(Debug.CatLifecycle, "PayloadReceived handler threw", ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!SingleInstanceManager.listenerShouldStop)
                    {
                        Debug.LogException(Debug.CatLifecycle, "Single-instance: listener loop error", ex);
                        Thread.Sleep(500);
                    }
                }
            }

            Debug.Log(Debug.CatLifecycle, "Single-instance: listener stopped.");
        }
    }

    public class SingleInstancePayload : EventArgs
    {
        public SingleInstancePayload(string conversionPresetName, IList<string> filePaths)
        {
            this.ConversionPresetName = conversionPresetName;
            this.FilePaths = filePaths ?? new List<string>();
        }

        public string ConversionPresetName { get; }

        public IList<string> FilePaths { get; }
    }
}
