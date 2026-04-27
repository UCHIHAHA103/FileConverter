// <copyright file="FileConverterExtension.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System.IO;
    using System;
    using Microsoft.Win32;

    public static class PathHelpers
    {
        private static RegistryKey fileConverterRegistryKey;
        private static string fileConverterPath;

        public static string UserSettingsFilePath => Path.Combine(PathHelpers.GetUserDataFolderPath, "Settings.user.xml");

        public static string DefaultSettingsFilePath
        {
            get
            {
                string pathToFileConverterExecutable = PathHelpers.FileConverterPath;
                if (string.IsNullOrEmpty(pathToFileConverterExecutable))
                {
                    return null;
                }

                return Path.Combine(Path.GetDirectoryName(pathToFileConverterExecutable), "Settings.default.xml");
            }
        }

        public static RegistryKey FileConverterRegistryKey
        {
            get
            {
                if (PathHelpers.fileConverterRegistryKey == null)
                {
                    try
                    {
                        PathHelpers.fileConverterRegistryKey = Registry.CurrentUser.OpenSubKey(@"Software\FileConverter");
                    }
                    catch
                    {
                        // Registry access might fail in certain host process contexts.
                    }
                }

                return PathHelpers.fileConverterRegistryKey;
            }
        }

        public static string FileConverterPath
        {
            get
            {
                if (string.IsNullOrEmpty(PathHelpers.fileConverterPath))
                {
                    try
                    {
                        PathHelpers.fileConverterPath = PathHelpers.FileConverterRegistryKey.GetValue("Path") as string;
                    }
                    catch
                    {
                        // Registry key might not be accessible in certain host processes.
                    }
                }

                // Fallback: derive path from this DLL's location (same directory).
                if (string.IsNullOrEmpty(PathHelpers.fileConverterPath))
                {
                    try
                    {
                        string dllDir = Path.GetDirectoryName(typeof(PathHelpers).Assembly.Location);
                        string candidate = Path.Combine(dllDir, "FileConverter.exe");
                        if (File.Exists(candidate))
                        {
                            PathHelpers.fileConverterPath = candidate;
                        }
                    }
                    catch
                    {
                    }
                }

                return PathHelpers.fileConverterPath;
            }
        }

        public static string GetUserDataFolderPath
        {
            get
            {
                string path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                path = Path.Combine(path, "FileConverter");

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }
    }
}
