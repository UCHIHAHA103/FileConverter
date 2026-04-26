// <copyright file="MainWindow.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Views
{
    using System;
    using System.Linq;
    using System.Windows;

    using CommunityToolkit.Mvvm.DependencyInjection;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
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

            // Find compatible presets based on the first dropped file's extension.
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

            // Use the first compatible preset. User can change presets via Settings.
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

            // Create conversion jobs and add to queue.
            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
            var viewModel = this.DataContext as ViewModels.MainViewModel;

            try
            {
                foreach (string filePath in files)
                {
                    ConversionJob job = ConversionJobFactory.Create(selectedPreset, filePath);
                    conversionService.RegisterConversionJob(job);
                    viewModel?.ConversionJobs.Add(job);
                }

                conversionService.ConvertFilesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create conversion jobs:\n{ex.Message}",
                    "File Converter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
