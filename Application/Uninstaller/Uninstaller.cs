// Uninstaller shim for File Converter.
// Finds the MSI product code and calls msiexec /x to trigger standard MSI uninstall.
// This gives users a simple "uninstall.exe" they can double-click.
// Built as WinExe so no CMD window appears.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FileConverter
{
    static class Uninstaller
    {
        [STAThread]
        static int Main(string[] args)
        {
            string productCode = FindProductCode();
            if (string.IsNullOrEmpty(productCode))
            {
                MessageBox.Show(
                    "File Converter is not installed (no matching MSI product found).",
                    "File Converter Uninstaller",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 1;
            }

            // Use /qr for reduced UI (progress bar + prompts) — same as control panel.
            var startInfo = new ProcessStartInfo("msiexec.exe", $"/x {productCode} /qr")
            {
                UseShellExecute = true,
                Verb = "runas" // Request UAC elevation
            };

            try
            {
                Process.Start(startInfo)?.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User declined UAC — silently exit.
                return 2;
            }

            return 0;
        }

        private static string FindProductCode()
        {
            // Strategy: find the entry whose InstallLocation or UninstallString
            // matches the directory where THIS exe is running from.
            // This avoids picking up stale registry entries from prior installs.
            string myDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            string[] uninstallPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };

            string fallbackCode = null;

            foreach (string uninstallPath in uninstallPaths)
            {
                using (var baseKey = Registry.LocalMachine.OpenSubKey(uninstallPath))
                {
                    if (baseKey == null) continue;

                    foreach (string subKeyName in baseKey.GetSubKeyNames())
                    {
                        using (var subKey = baseKey.OpenSubKey(subKeyName))
                        {
                            if (subKey == null) continue;

                            string displayName = subKey.GetValue("DisplayName") as string;
                            if (displayName == null ||
                                !displayName.Equals("File Converter", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // Check if this entry's install location matches our directory
                            string installLocation = subKey.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(installLocation))
                            {
                                string loc = installLocation.TrimEnd('\\');
                                if (loc.Equals(myDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    return subKeyName; // Best match — same directory
                                }
                            }

                            // Keep as fallback (name matches but location didn't)
                            if (fallbackCode == null)
                            {
                                fallbackCode = subKeyName;
                            }
                        }
                    }
                }
            }

            return fallbackCode;
        }
    }
}
