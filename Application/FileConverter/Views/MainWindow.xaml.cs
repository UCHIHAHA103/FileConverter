// <copyright file="MainWindow.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Views
{
    using System.Windows;

    using FileConverter.ViewModels;

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            // Forward to ViewModel via command (MVVM pattern).
            if (this.DataContext is MainViewModel vm && vm.DropFilesCommand.CanExecute(e))
            {
                vm.DropFilesCommand.Execute(e);
            }
        }
    }
}
