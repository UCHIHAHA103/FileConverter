// <copyright file="MainViewModel.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Input;

    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.DependencyInjection;
    using CommunityToolkit.Mvvm.Input;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;

    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// </summary>
    public class MainViewModel : ObservableRecipient
    {
        private string informationMessage;
        private ObservableCollection<ConversionJob> conversionJobs;

        private RelayCommand showSettingsCommand;
        private RelayCommand showDiagnosticsCommand;
        private RelayCommand openLogsFolderCommand;
        private RelayCommand<CancelEventArgs> closeCommand;
        private RelayCommand<DragEventArgs> dropFilesCommand;
        private RelayCommand<ConversionJob> openOutputFileCommand;
        private RelayCommand<ConversionJob> openOutputFolderCommand;

        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            IConversionService settingsService = Ioc.Default.GetRequiredService<IConversionService>();
            this.ConversionJobs = new ObservableCollection<ConversionJob>(settingsService.ConversionJobs);

            FileConverter.Application application = System.Windows.Application.Current as FileConverter.Application;
            application.OnApplicationTerminate += this.Application_OnApplicationTerminate;
        }

        public string InformationMessage
        {
            get => this.informationMessage;

            private set
            {
                this.SetProperty(ref this.informationMessage, value);
            }
        }

        public ObservableCollection<ConversionJob> ConversionJobs
        {
            get => this.conversionJobs;

            private set
            {
                this.SetProperty(ref this.conversionJobs, value);

                foreach (var job in this.conversionJobs)
                {
                    job.PropertyChanged += this.ConversionJob_PropertyChanged;
                }
            }
        }

        public ICommand ShowSettingsCommand
        {
            get
            {
                if (this.showSettingsCommand == null)
                {
                    this.showSettingsCommand = new RelayCommand(() => Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Settings));
                }

                return this.showSettingsCommand;
            }
        }

        public ICommand ShowDiagnosticsCommand
        {
            get
            {
                if (this.showDiagnosticsCommand == null)
                {
                    this.showDiagnosticsCommand = new RelayCommand(() => Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Diagnostics));
                }

                return this.showDiagnosticsCommand;
            }
        }

        /// <summary>
        /// Opens the folder containing persistent FileConverter.log (and rotated
        /// backups) in Windows Explorer, with FileConverter.log selected.
        /// Useful for users reporting issues.
        /// </summary>
        public ICommand OpenLogsFolderCommand
        {
            get
            {
                if (this.openLogsFolderCommand == null)
                {
                    this.openLogsFolderCommand = new RelayCommand(() =>
                    {
                        try
                        {
                            string logPath = FileConverter.Diagnostics.Debug.PersistentLogPath;
                            if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                            }
                            else
                            {
                                string dir = System.IO.Path.GetDirectoryName(logPath);
                                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            FileConverter.Diagnostics.Debug.LogException(
                                FileConverter.Diagnostics.Debug.CatGeneral,
                                "OpenLogsFolderCommand failed", ex);
                        }
                    });
                }

                return this.openLogsFolderCommand;
            }
        }

        public ICommand CloseCommand
        {
            get
            {
                if (this.closeCommand == null)
                {
                    this.closeCommand = new RelayCommand<CancelEventArgs>(this.Close);
                }

                return this.closeCommand;
            }
        }

        public ICommand DropFilesCommand
        {
            get
            {
                if (this.dropFilesCommand == null)
                {
                    this.dropFilesCommand = new RelayCommand<DragEventArgs>(this.DropFiles);
                }

                return this.dropFilesCommand;
            }
        }

        public ICommand OpenOutputFileCommand
        {
            get
            {
                if (this.openOutputFileCommand == null)
                {
                    this.openOutputFileCommand = new RelayCommand<ConversionJob>(this.OpenOutputFile);
                }

                return this.openOutputFileCommand;
            }
        }

        public ICommand OpenOutputFolderCommand
        {
            get
            {
                if (this.openOutputFolderCommand == null)
                {
                    this.openOutputFolderCommand = new RelayCommand<ConversionJob>(this.OpenOutputFolder);
                }

                return this.openOutputFolderCommand;
            }
        }

        private void Close(CancelEventArgs args)
        {
            // Check if any conversion is still in progress.
            if (args != null)
            {
                bool hasActiveJobs = false;
                foreach (var job in this.ConversionJobs)
                {
                    if (job.State == ConversionState.InProgress || job.State == ConversionState.Ready)
                    {
                        hasActiveJobs = true;
                        break;
                    }
                }

                if (hasActiveJobs)
                {
                    var result = MessageBox.Show(
                        "Conversions are still in progress. Are you sure you want to close?\n转换仍在进行中，确定要关闭吗？",
                        "File Converter",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        args.Cancel = true;
                        return;
                    }
                }
            }

            // Cancel all active conversion jobs before closing (#361).
            foreach (var job in this.ConversionJobs)
            {
                if (job.State == ConversionState.InProgress || job.State == ConversionState.Ready)
                {
                    job.Cancel();
                }
            }

            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
            navigationService.Close(Pages.Main, args != null);
        }

        private void DropFiles(DragEventArgs e)
        {
            if (e == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
            {
                return;
            }

            // Expand directories to individual files (#704 multi-folder support).
            var expandedFiles = new System.Collections.Generic.List<string>();
            foreach (string path in files)
            {
                if (System.IO.Directory.Exists(path))
                {
                    expandedFiles.AddRange(System.IO.Directory.GetFiles(path, "*.*", System.IO.SearchOption.AllDirectories));
                }
                else
                {
                    expandedFiles.Add(path);
                }
            }

            files = expandedFiles.ToArray();
            if (files.Length == 0)
            {
                return;
            }

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            if (settingsService.Settings?.ConversionPresets == null ||
                settingsService.Settings.ConversionPresets.Count == 0)
            {
                MessageBox.Show("No conversion presets available.\n没有可用的转换预设。",
                    "File Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string firstExtension = System.IO.Path.GetExtension(files[0]);
            if (!string.IsNullOrEmpty(firstExtension))
            {
                firstExtension = firstExtension.Substring(1).ToLowerInvariant();
            }

            var compatiblePresets = settingsService.Settings.ConversionPresets
                .Where(p => p.InputTypes != null && p.InputTypes.Contains(firstExtension))
                .ToList();

            if (compatiblePresets.Count == 0)
            {
                MessageBox.Show($"No preset supports '.{firstExtension}' input.\n没有预设支持 '.{firstExtension}' 输入格式。",
                    "File Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConversionPreset selectedPreset = compatiblePresets[0];

            var confirmResult = MessageBox.Show(
                $"Convert {files.Length} file(s) using preset '{selectedPreset.FullName}'?\n" +
                $"使用预设 '{selectedPreset.FullName}' 转换 {files.Length} 个文件？",
                "File Converter — Drag & Drop",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.OK)
            {
                return;
            }

            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

            try
            {
                foreach (string filePath in files)
                {
                    ConversionJob job = ConversionJobFactory.Create(selectedPreset, filePath);
                    conversionService.RegisterConversionJob(job);
                    this.ConversionJobs.Add(job);
                    job.PropertyChanged += this.ConversionJob_PropertyChanged;
                }

                conversionService.ConvertFilesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create conversion jobs:\n{ex.Message}",
                    "File Converter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenOutputFile(ConversionJob job)
        {
            if (job == null || string.IsNullOrEmpty(job.OutputFilePath) || !System.IO.File.Exists(job.OutputFilePath))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = job.OutputFilePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.Log($"Failed to open output file: {ex.Message}");
            }
        }

        private void OpenOutputFolder(ConversionJob job)
        {
            if (job == null || string.IsNullOrEmpty(job.OutputFilePath))
            {
                return;
            }

            try
            {
                string folder = System.IO.Path.GetDirectoryName(job.OutputFilePath);
                if (System.IO.File.Exists(job.OutputFilePath))
                {
                    // Select the file in Explorer.
                    System.Diagnostics.Process.Start("explorer.exe", $"/select, \"{job.OutputFilePath}\"");
                }
                else if (System.IO.Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.Log($"Failed to open output folder: {ex.Message}");
            }
        }

        private void ConversionJob_PropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName != "State" && eventArgs.PropertyName != "Progress")
            {
                return;
            }

            this.OnPropertyChanged(nameof(this.ConversionJobs));
        }

        private void Application_OnApplicationTerminate(object sender, ApplicationTerminateArgs eventArgs)
        {
            if (float.IsNaN(eventArgs.RemainingTimeBeforeTermination))
            {
                this.InformationMessage = string.Empty;
                return;
            }

            int remaingingSeconds = (int)eventArgs.RemainingTimeBeforeTermination;

            if (remaingingSeconds >= 2)
            {
                this.InformationMessage = string.Format(Properties.Resources.ApplicationWillTerminateInMultipleSeconds, remaingingSeconds);
            }
            else if (remaingingSeconds == 1)
            {
                this.InformationMessage = Properties.Resources.ApplicationWillTerminateInOneSecond;
            }

            if (remaingingSeconds <= 0)
            {
                this.InformationMessage = Properties.Resources.ApplicationIsTerminating;
            }
        }
    }
}
