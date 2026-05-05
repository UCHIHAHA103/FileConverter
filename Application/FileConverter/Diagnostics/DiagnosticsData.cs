// <copyright file="DiagnosticsData.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;

    using FileConverter.Annotations;

    public class DiagnosticsData : INotifyPropertyChanged
    {
        private readonly List<string> logMessages = new List<string>();
        private readonly StringBuilder stringBuilder = new StringBuilder();
        private System.IO.TextWriter logFileWriter;
        private string name;

        public DiagnosticsData(string name)
        {
            this.Name = name;
            this.LogMessages = new ReadOnlyCollection<string>(this.logMessages);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => this.name;

            private set
            {
                this.name = value;
                this.OnPropertyChanged();
            }
        }

        public string Content
        {
            get
            {
                this.stringBuilder.Clear();
                for (int index = 0; index < this.LogMessages.Count; index++)
                {
                    this.stringBuilder.AppendLine(this.LogMessages[index]);
                }

                return this.stringBuilder.ToString();
            }
        }

        public ReadOnlyCollection<string> LogMessages
        {
            get;
            private set;
        }

        public void Initialize(string diagnosticsFolderPath, int id)
        {
            string path = Path.Combine(diagnosticsFolderPath, $"Diagnostics{id}.log");
            path = PathHelpers.GenerateUniquePath(path);
            try
            {
                this.logFileWriter = new StreamWriter(
                    File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read),
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { this.logFileWriter = null; }

            this.Log($"{System.DateTime.Now.ToLongDateString()} {System.DateTime.Now.ToLongTimeString()}\n");
        }

        public void Release()
        {
            try
            {
                this.logFileWriter?.Flush();
                this.logFileWriter?.Close();
            }
            catch { }
            this.logFileWriter = null;
        }

        public void Log(string log)
        {
            string stamped = $"{System.DateTime.Now:HH:mm:ss.fff} {log}";
            this.logMessages.Add(stamped);
            try
            {
                this.logFileWriter?.WriteLine(stamped);
                this.logFileWriter?.Flush();
            }
            catch { /* swallow: logging must never crash */ }

            this.OnPropertyChanged(nameof(this.Content));
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}