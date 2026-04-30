// <copyright file="FileConverterExtension.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverterExtension
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Forms;

    using SharpShell.Attributes;
    using SharpShell.SharpContextMenu;

    /// <summary>
    /// File converter context menu extension class.
    /// </summary>
    [ComVisible(true), Guid("AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90")]
    [COMServerAssociation(AssociationType.AllFiles)]
    public class FileConverterExtension : SharpContextMenu
    {
        private const int MaximumProcessArgumentsLength = 8000; // https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/command-line-string-limitation

        private PresetReference[] presetReferences = null;
        private List<MenuEntry> menuEntries = new List<MenuEntry>();

        private HashSet<string> extensionCache = new HashSet<string>();

        // Index: extension → true. Used for O(1) CanShowMenu check (#675 T-CTX3).
        private HashSet<string> allSupportedExtensions = null;

        // Cached bitmaps to avoid re-creating Icon objects on every right-click (#675).
        private static Bitmap cachedAppIcon;
        private static Bitmap cachedFolderIcon;
        private static Bitmap cachedPresetIcon;
        private static Bitmap cachedSettingsIcon;

        private static Bitmap GetAppIcon()
        {
            if (cachedAppIcon == null)
            {
                cachedAppIcon = new Icon(Properties.Resources.ApplicationIcon, SystemInformation.SmallIconSize).ToBitmap();
            }

            return cachedAppIcon;
        }

        private static Bitmap GetFolderIcon()
        {
            if (cachedFolderIcon == null)
            {
                cachedFolderIcon = new Icon(Properties.Resources.FolderIcon, SystemInformation.SmallIconSize).ToBitmap();
            }

            return cachedFolderIcon;
        }

        private static Bitmap GetPresetIcon()
        {
            if (cachedPresetIcon == null)
            {
                cachedPresetIcon = new Icon(Properties.Resources.PresetIcon, SystemInformation.SmallIconSize).ToBitmap();
            }

            return cachedPresetIcon;
        }

        private static Bitmap GetSettingsIcon()
        {
            if (cachedSettingsIcon == null)
            {
                cachedSettingsIcon = new Icon(Properties.Resources.SettingsIcon, SystemInformation.SmallIconSize).ToBitmap();
            }

            return cachedSettingsIcon;
        }

        private class MenuEntry
        {
            public PresetReference PresetReference;
            public bool Enabled;
            public int ExtensionRefCount;

            public MenuEntry(PresetReference presetReference)
            {
                this.PresetReference = presetReference;
                this.Enabled = false;
                this.ExtensionRefCount = 0;
            }
        }
        
        private bool DisplayPresetIcons
        {
            get
            {
                string displayPresetIcons = PathHelpers.FileConverterRegistryKey.GetValue("DisplayPresetIcons") as string;
                if (displayPresetIcons == null)
                {
                    return false;
                }

                if (!bool.TryParse(displayPresetIcons, out bool value))
                {
                    return false;
                }

                return value;
            }
        }

        private PresetReference[] PresetReferences
        {
            get
            {
                this.LoadExtensionSettingsIfNecessary();

                return this.presetReferences;
            }
        }

        protected override bool CanShowMenu()
        {
            try
            {
                this.RefreshExtensionCacheFromSelectedItems();

                // Fast path: check against pre-built extension index (O(1) per extension).
                this.LoadExtensionSettingsIfNecessary();
                if (this.allSupportedExtensions == null || this.allSupportedExtensions.Count == 0)
                {
                    return false;
                }

                foreach (string extension in this.extensionCache)
                {
                    if (this.allSupportedExtensions.Contains(extension))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Never let an exception from preset evaluation crash the shell extension.
            }

            return false;
        }

        protected override ContextMenuStrip CreateMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            try
            {
                this.RefreshPresetList();

                bool displayPresetIcons = this.DisplayPresetIcons;

                ToolStripMenuItem fileConverterItem = new ToolStripMenuItem
                {
                    Text = "File Converter",
                    Image = GetAppIcon(),
                };

                int menuItemIndex = 0;
                foreach (MenuEntry menuEntry in this.menuEntries)
                {
                    try
                    {
                        menuItemIndex++;
                    
                        ToolStripMenuItem root = fileConverterItem;
                        if (menuEntry.PresetReference.Folders != null)
                        {
                            foreach (string folder in menuEntry.PresetReference.Folders)
                            {
                                ToolStripItem[] folderItems = root.DropDownItems.Find(folder, false);
                                if (folderItems.Length == 0)
                                {
                                    ToolStripMenuItem folderItem = new ToolStripMenuItem
                                    {
                                        Name = folder,
                                        Text = folder,
                                        Image = GetFolderIcon(),
                                    };

                                    root.DropDownItems.Add(folderItem);
                                    root = folderItem;
                                }
                                else
                                {
                                    root = folderItems[0] as ToolStripMenuItem;
                                }

                                if (root == null)
                                {
                                    break;
                                }
                            }
                        }

                        if (root == null)
                        {
                            root = fileConverterItem;
                        }

                        string uniqueSuffix = new string('\u200B', menuItemIndex);
                        string displayText = menuEntry.PresetReference.Name + uniqueSuffix;

                        ToolStripMenuItem subItem = new ToolStripMenuItem
                        {
                            Text = displayText,
                            Enabled = menuEntry.Enabled
                        };

                        if (displayPresetIcons)
                        {
                            subItem.Image = GetPresetIcon();
                        }

                        root.DropDownItems.Add(subItem);
                        subItem.Click += (sender, args) => this.ConvertFiles(menuEntry.PresetReference.FullName);
                    }
                    catch
                    {
                        // Skip this preset — other presets remain available.
                    }
                }

                if (this.menuEntries.Count > 0)
                {
                    fileConverterItem.DropDownItems.Add(new ToolStripSeparator());
                }

                {
                    ToolStripMenuItem subItem = new ToolStripMenuItem
                    {
                        Text = "Configure presets...",
                        Image = GetSettingsIcon(),
                    };

                    fileConverterItem.DropDownItems.Add(subItem);
                    subItem.Click += (sender, args) => this.OpenSettings();
                }

                menu.Items.Add(fileConverterItem);
            }
            catch
            {
                // Last resort: return an empty menu instead of crashing the shell extension.
            }

            return menu;
        }

        private void RefreshExtensionCacheFromSelectedItems()
        {
            // Retrieve selected files extensions.
            this.extensionCache.Clear();
            foreach (string filePath in this.SelectedItemPaths)
            {
                string extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension))
                {
                    continue;
                }

                extension = extension.Substring(1).ToLowerInvariant();

                this.extensionCache.Add(extension);
            }
        }

        private void RefreshPresetList()
        {
            this.RefreshExtensionCacheFromSelectedItems();

            // Activate compatible menu entries.
            PresetReference[] presets = this.presetReferences;
            this.menuEntries.Clear();
            if (presets == null)
            {
                return;
            }

            foreach (string extension in this.extensionCache)
            {
                foreach (PresetReference presetReference in presets)
                {
                    if (presetReference?.InputTypes == null || !presetReference.InputTypes.Contains(extension))
                    {
                        continue;
                    }

                    MenuEntry menuEntry = this.menuEntries.Find(entry => entry.PresetReference.FullName == presetReference.FullName);
                    if (menuEntry == null)
                    {
                        menuEntry = new MenuEntry(presetReference);
                        this.menuEntries.Add(menuEntry);
                    }

                    menuEntry.ExtensionRefCount++;
                }
            }

            // Enable presets compatible with all input files.
            foreach (MenuEntry menuEntry in this.menuEntries)
            {
                menuEntry.Enabled = menuEntry.ExtensionRefCount == this.extensionCache.Count;
            }
        }

        private void LoadExtensionSettingsIfNecessary()
        {
            if (this.presetReferences != null)
            {
                return;
            }

            if (File.Exists(PathHelpers.UserSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile("Settings", PathHelpers.UserSettingsFilePath, out this.presetReferences);
                }
                catch
                {
                    // Can't handle this error in the explorer extension.
                }
            }

            if (this.presetReferences == null)
            {
                try
                {
                    XmlHelpers.LoadFromFile("Settings", PathHelpers.DefaultSettingsFilePath, out this.presetReferences);
                }
                catch
                {
                    // Can't handle this error in the explorer extension.
                }
            }

            // Sanitize: remove any preset with null/empty InputTypes or null FullName
            // so downstream code never encounters them.
            if (this.presetReferences != null)
            {
                var valid = new System.Collections.Generic.List<PresetReference>();
                foreach (var preset in this.presetReferences)
                {
                    if (preset != null
                        && !string.IsNullOrEmpty(preset.FullName)
                        && preset.InputTypes != null
                        && preset.InputTypes.Length > 0)
                    {
                        valid.Add(preset);
                    }
                }

                this.presetReferences = valid.ToArray();
            }

            // Build extension index for O(1) lookup in CanShowMenu (#675 T-CTX3).
            this.allSupportedExtensions = new HashSet<string>();
            if (this.presetReferences != null)
            {
                foreach (var preset in this.presetReferences)
                {
                    foreach (string ext in preset.InputTypes)
                    {
                        this.allSupportedExtensions.Add(ext);
                    }
                }
            }
        }

        private void OpenSettings()
        {
            if (string.IsNullOrEmpty(PathHelpers.FileConverterPath))
            {
                MessageBox.Show("Can't retrieve the file converter executable path. You should try to reinstall the application.");
                return;
            }

            if (!File.Exists(PathHelpers.FileConverterPath))
            {
                MessageBox.Show($"Can't find the file converter executable ({PathHelpers.FileConverterPath}). You should try to reinstall the application.");
                return;
            }

            ProcessStartInfo processStartInfo = new ProcessStartInfo(PathHelpers.FileConverterPath)
            {
                CreateNoWindow = false, 
                UseShellExecute = false, 
                RedirectStandardOutput = false,
            };

            // Build arguments string.
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("--settings");
            
            processStartInfo.Arguments = stringBuilder.ToString();
            Process exeProcess = Process.Start(processStartInfo);
        }

        private void ConvertFiles(string presetName)
        {
            if (string.IsNullOrEmpty(PathHelpers.FileConverterPath))
            {
                MessageBox.Show("Can't retrieve the file converter executable path. You should try to reinstall the application.");
                return;
            }

            if (!File.Exists(PathHelpers.FileConverterPath))
            {
                MessageBox.Show($"Can't find the file converter executable ({PathHelpers.FileConverterPath}). You should try to reinstall the application.");
                return;
            }

            void BuildConversionPresetArgument(StringBuilder sb)
            {
                sb.Append("--conversion-preset ");
                sb.Append(" \"");
                sb.Append(presetName);
                sb.Append("\"");
            }

            // Build arguments string.
            StringBuilder stringBuilder = new StringBuilder();
            BuildConversionPresetArgument(stringBuilder);
            
            string fileListPath = null;
            foreach (var filePath in this.SelectedItemPaths)
            {
                stringBuilder.Append(" \"");
                stringBuilder.Append(filePath);
                stringBuilder.Append("\"");

                if (stringBuilder.Length >= MaximumProcessArgumentsLength)
                {
                    // Alternative way of passing arguments to not overflow the command line.
                    stringBuilder.Clear();
                    BuildConversionPresetArgument(stringBuilder);

                    // Store list of file to convert in a file in Temp folder.
                    fileListPath = Path.Combine(Path.GetTempPath(), "file-converter-input-list.txt");
                    int index = 1;
                    while (File.Exists(fileListPath))
                    {
                        fileListPath = Path.Combine(Path.GetTempPath(), $"file-converter-input-list-{index}.txt");
                        index++;
                    }

                    using (FileStream file = File.OpenWrite(fileListPath))
                    using (StreamWriter writer = new StreamWriter(file))
                    {
                        foreach (var path in this.SelectedItemPaths)
                        {
                            writer.WriteLine(path);
                        }
                    }

                    stringBuilder.Append(" --input-files ");
                    stringBuilder.Append(" \"");
                    stringBuilder.Append(fileListPath);
                    stringBuilder.Append("\"");
                    break;
                }
            }

            var processStartInfo = new ProcessStartInfo(PathHelpers.FileConverterPath)
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                Arguments = stringBuilder.ToString(),
            };

            Process exeProcess = Process.Start(processStartInfo);
            exeProcess.EnableRaisingEvents = true;
            exeProcess.Exited += (sender, args) =>
            {
                if (fileListPath != null)
                {
                    try
                    {
                        File.Delete(fileListPath);
                    }
                    catch 
                    { 
                    }
                }
            };
        }
    }
}
