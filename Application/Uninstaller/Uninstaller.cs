// Uninstaller shim for File Converter.
// Finds the MSI product code and calls msiexec /x to trigger standard MSI uninstall.
// This gives users a simple "uninstall.exe" they can double-click.

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace FileConverter
{
    class Uninstaller
    {
        // UpgradeCode from Product.wxs — stable across versions
        private const string UpgradeCode = "E3CA717B-A897-418A-BBEF-5C7E35C76E4B";

        static int Main(string[] args)
        {
            string productCode = FindProductCode();
            if (string.IsNullOrEmpty(productCode))
            {
                Console.WriteLine("File Converter is not installed (no matching MSI product found).");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return 1;
            }

            Console.WriteLine($"Uninstalling File Converter (ProductCode: {productCode})...");

            // Use /qr for a reduced UI (progress bar + prompts) — same as control panel uninstall.
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
                // User declined UAC
                Console.WriteLine("Uninstall cancelled (administrator privileges required).");
                return 2;
            }

            return 0;
        }

        /// <summary>
        /// Find the MSI ProductCode by looking up the UpgradeCode in the Windows Installer database.
        /// </summary>
        private static string FindProductCode()
        {
            // MSI stores upgrade-to-product mapping in:
            // HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UpgradeCodes\{packed-upgrade-code}
            // The value names are packed product codes.
            // But it's easier to just search Uninstall registry for our product name.

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
                            if (displayName != null && displayName.Equals("File Converter", StringComparison.OrdinalIgnoreCase))
                            {
                                // subKeyName is the ProductCode (e.g. {GUID})
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
