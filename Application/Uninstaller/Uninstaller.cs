// Uninstaller shim for File Converter.
// Finds the MSI product code and calls msiexec /x to trigger standard MSI uninstall.
// This gives users a simple "uninstall.exe" they can double-click.
// Built as WinExe so no CMD window appears.

using System;
using System.Diagnostics;
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
            string[] uninstallPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };

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
                            if (displayName != null &&
                                displayName.Equals("File Converter", StringComparison.OrdinalIgnoreCase))
                            {
                                return subKeyName;
                            }
                        }
                    }
                }
            }

            return null;
        }
    }
}
