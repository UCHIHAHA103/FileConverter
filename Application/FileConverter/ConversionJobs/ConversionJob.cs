// <copyright file="ConversionJob.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Windows.Input;
    
    using CommunityToolkit.Mvvm.Input;

    using FileConverter.Diagnostics;

    public class ConversionJob : INotifyPropertyChanged
    {
        private float progress = 0f;
        private DateTime startTime;
        private ConversionState state = ConversionState.Unknown;
        private string errorMessage = string.Empty;
        private string userState = string.Empty;
        private RelayCommand cancelCommand;

        private readonly string initialInputPath;
        private int currentOutputFilePathIndex;

        public ConversionJob()
        {
            this.UserState = "Design Mode";

            this.ConversionPreset = null;
            this.initialInputPath = string.Empty;
            this.InputFilePath = @"C:\Path\To\AVery\Long\Location\WithAVeryNiceFile.png";
            this.OutputFilePaths = new[] { "C:\\Path\\To\\AVery\\Long\\Location\\WithAVeryNiceFile.jpg" };
            this.StartTime = DateTime.Now - TimeSpan.FromMinutes(1.2);
            this.State = ConversionState.InProgress;
            this.StateFlags = ConversionFlags.None;
            this.Progress = 0.6f;
        }

        public ConversionJob(ConversionPreset conversionPreset, string inputFilePath)
        {
            if (conversionPreset == null)
            {
                throw new ArgumentNullException(nameof(conversionPreset));
            }

            if (string.IsNullOrEmpty(inputFilePath))
            {
                throw new ArgumentNullException(nameof(inputFilePath));
            }

            this.State = ConversionState.Unknown;
            this.initialInputPath = inputFilePath;
            this.InputFilePath = inputFilePath;
            this.ConversionPreset = conversionPreset;
            this.UserState = Properties.Resources.ConversionStatePrepareConversion;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        public ConversionPreset ConversionPreset
        {
            get;
            private set;
        }

        public string InputFilePath
        {
            get;
            set;
        }

        public string OutputFilePath
        {
            get
            {
                if (this.OutputFilePaths == null || this.OutputFilePaths.Length == 0)
                {
                    return string.Empty;
                }

                if (this.CurrentOutputFilePathIndex < 0)
                {
                    return this.OutputFilePaths[0];
                }

                if (this.CurrentOutputFilePathIndex >= this.OutputFilePaths.Length)
                {
                    return this.OutputFilePaths[this.OutputFilePaths.Length - 1];
                }

                return this.OutputFilePaths[this.CurrentOutputFilePathIndex];
            }
        }

        /// <summary>
        /// User-friendly version of OutputFilePath.
        /// Replaces ffmpeg sequence patterns like %06d with a readable placeholder
        /// (e.g. "序号") so users don't see cryptic formatting codes in the UI.
        /// </summary>
        public string OutputDisplayPath
        {
            get
            {
                string path = this.OutputFilePath;
                if (string.IsNullOrEmpty(path))
                {
                    return string.Empty;
                }

                // Replace %06d / %04d / %d with a friendly placeholder.
                return System.Text.RegularExpressions.Regex.Replace(path, @"%0?\d*d", "序号");
            }
        }

        /// <summary>
        /// When set via the CLI --output-dir flag, overrides the preset's
        /// OutputFileNameTemplate and places every output file in this directory.
        /// Null/empty = use the preset template as before (default behavior).
        /// </summary>
        public string OutputDirectoryOverride { get; set; }

        public ConversionState State
        {
            get => this.state;

            private set
            {
                this.state = value;
                this.NotifyPropertyChanged();
                try
                {
                    Application.Current?.Dispatcher?.BeginInvoke((Action)(() => this.cancelCommand?.NotifyCanExecuteChanged()));
                }
                catch
                {
                    // Application may be shutting down.
                }
            }
        }

        public string UserState
        {
            get => this.userState;

            protected set
            {
                this.userState = value;
                this.NotifyPropertyChanged();
            }
        }

        public float Progress
        {
            get => this.progress;

            protected set
            {
                this.progress = value;
                this.NotifyPropertyChanged();
            }
        }

        public DateTime StartTime
        {
            get => this.startTime;

            protected set
            {
                this.startTime = value;
                this.NotifyPropertyChanged();
            }
        }

        public string ErrorMessage
        {
            get => this.errorMessage;

            private set
            {
                this.errorMessage = value;
                this.NotifyPropertyChanged();
            }
        }

        public ConversionFlags StateFlags
        {
            get;
            protected set;
        }

        public ICommand CancelCommand
        {
            get
            {
                if (this.cancelCommand == null)
                {
                    this.cancelCommand = new RelayCommand(this.Cancel, this.IsCancelable);
                }

                return this.cancelCommand;
            }
        }

        protected bool CancelIsRequested
        {
            get;
            private set;
        }

        protected int CurrentOutputFilePathIndex
        {
            get => this.currentOutputFilePathIndex;

            set
            {
                this.currentOutputFilePathIndex = value;
                this.NotifyPropertyChanged(nameof(this.OutputFilePath));
                this.NotifyPropertyChanged(nameof(this.OutputDisplayPath));
            }
        }

        protected virtual InputPostConversionAction InputPostConversionAction
        {
            get
            {
                if (this.ConversionPreset == null)
                {
                    return InputPostConversionAction.None;
                }

                return this.ConversionPreset.InputPostConversionAction;
            }
        }

        protected virtual bool IsCancelable() => this.State == ConversionState.InProgress;

        protected string[] OutputFilePaths
        {
            get;
            private set;
        }

        public virtual bool CanStartConversion(ConversionFlags conversionFlags)
        {
            return (conversionFlags & ConversionFlags.CdDriveExtraction) == 0;
        }

        public void PrepareConversion(string outputDirectoryOverride = null, params string[] outputFilePaths)
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            this.InputFilePath = this.initialInputPath;

            Debug.Log(Debug.CatConversion,
                $"PrepareConversion: preset='{this.ConversionPreset.FullName}' outputType={this.ConversionPreset.OutputType} input='{this.InputFilePath}' template='{this.ConversionPreset.OutputFileNameTemplate}' customCmd={this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand)} outputDirOverride='{outputDirectoryOverride ?? this.OutputDirectoryOverride ?? "(none)"}'");

            string extension = System.IO.Path.GetExtension(this.initialInputPath);
            extension = extension.Substring(1, extension.Length - 1);
            string extensionCategory = Helpers.GetExtensionCategory(extension);

            // Skip compatibility check when custom FFmpeg command is enabled —
            // custom commands can handle any input→output combination (e.g., video→PNG frames).
            bool customCommandEnabled = this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand);
            if (!customCommandEnabled && !Helpers.IsOutputTypeCompatibleWithCategory(this.ConversionPreset.OutputType, extensionCategory))
            {
                Debug.LogWarning(Debug.CatConversion,
                    $"Input type '{extension}' (category {extensionCategory}) is not compatible with output type {this.ConversionPreset.OutputType}");
                this.ConversionFailed(Properties.Resources.ErrorInputTypeIncompatibleWithOutputType);
                return;
            }

            this.OutputFilePaths = outputFilePaths;

            // Check available disk space on output drive (#674).
            try
            {
                string outputDir = System.IO.Path.GetDirectoryName(this.initialInputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    string root = System.IO.Path.GetPathRoot(outputDir);
                    if (!string.IsNullOrEmpty(root))
                    {
                        var driveInfo = new System.IO.DriveInfo(root);
                        long inputSize = new System.IO.FileInfo(this.initialInputPath).Length;
                        if (driveInfo.AvailableFreeSpace < inputSize)
                        {
                            this.ConversionFailed($"Not enough disk space on {root} ({driveInfo.AvailableFreeSpace / 1048576} MB free, need ~{inputSize / 1048576} MB).");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Disk space check failed (non-fatal): {ex.Message}");
            }

            if (this.OutputFilePaths.Length == 0)
            {
                int outputFilesCount = this.GetOutputFilesCount();
                this.OutputFilePaths = new string[outputFilesCount];
            }

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                if (!string.IsNullOrEmpty(this.OutputFilePaths[index]))
                {
                    // Don't generate a path if it has already been set.
                    continue;
                }

                string path;
                if (!string.IsNullOrEmpty(outputDirectoryOverride ?? this.OutputDirectoryOverride))
                {
                    // --output-dir override: write output into the specified directory,
                    // keeping only the input file name (no source directory tree).
                    string dir = outputDirectoryOverride ?? this.OutputDirectoryOverride;
                    path = PathHelpers.GenerateFilePathInDirectory(
                        this.initialInputPath, this.ConversionPreset.OutputType, dir);
                }
                else
                {
                    path = this.ConversionPreset.GenerateOutputFilePath(this.initialInputPath, index + 1, this.OutputFilePaths.Length);
                }

                if (!PathHelpers.IsPathValid(path))
                {
                    this.ConversionFailed(Properties.Resources.ErrorInvalidOutputPath);
                    Debug.Log($"Invalid output path generated: {path} from input: {this.InputFilePath}.");
                    return;
                }

                if (path == this.InputFilePath)
                {
                    // If the input post conversion action is to move or delete the input file, change its name in order to keep the output name intact.
                    if (this.ConversionPreset.InputPostConversionAction == InputPostConversionAction.MoveInArchiveFolder ||
                        this.ConversionPreset.InputPostConversionAction == InputPostConversionAction.Delete)
                    {
                        string inputExtension = System.IO.Path.GetExtension(this.InputFilePath);
                        string pathWithoutExtension = this.InputFilePath.Substring(0, this.InputFilePath.Length - inputExtension.Length);
                        this.InputFilePath = PathHelpers.GenerateUniquePath(pathWithoutExtension + "_TEMP" + inputExtension);
                        System.IO.File.Move(this.initialInputPath, this.InputFilePath);
                    }
                }

                // Guard: if a directory with the same name as the intended output file
                // already exists (e.g. a mis-created "花盛开动画.gif\" folder), ffmpeg
                // will silently succeed but never write the file, resulting in the
                // "Conversion appeared to succeed but output file was not found" error.
                // Detect this early and fail with a clear diagnostic.
                if (System.IO.Directory.Exists(path))
                {
                    Debug.LogError(
                        $"Output path collision: a directory named '{System.IO.Path.GetFileName(path)}' " +
                        $"already exists at '{System.IO.Path.GetDirectoryName(path)}'. " +
                        $"Please delete or rename that directory before converting.");
                    this.ConversionFailed(
                        $"A folder named '{System.IO.Path.GetFileName(path)}' exists at the output location. " +
                        $"Please delete or rename it and retry.");
                    return;
                }

                // Create output folders that doesn't exist.
                if (!PathHelpers.CreateFolders(path))
                {
                    this.ConversionFailed(Properties.Resources.ErrorFailToCreateOutputPathFolders);
                    return;
                }

                // Make the output path valid.
                try
                {
                    path = PathHelpers.GenerateUniquePath(path, this.OutputFilePaths);
                }
                catch (Exception exception)
                {
                    this.ConversionFailed(Properties.Resources.ErrorFailToGenerateUniqueOutputPath);
                    Debug.LogException(Debug.CatConversion, "GenerateUniquePath failed", exception);
                    return;
                }

                this.OutputFilePaths[index] = path;
            }

            this.CurrentOutputFilePathIndex = 0;

            // Check if the input file is located on a cd drive.
            if (PathHelpers.IsOnCDDrive(this.InputFilePath))
            {
                this.StateFlags = ConversionFlags.CdDriveExtraction;
            }

            try
            {
                this.Initialize();
            }
            catch (Exception exception)
            {
                this.ConversionFailed(Properties.Resources.ErrorDuringJobInitialization);
                Debug.LogException(Debug.CatConversion, $"Initialize() threw for preset='{this.ConversionPreset?.FullName}' input='{this.InputFilePath}'", exception);
                return;
            }

            if (this.State == ConversionState.Unknown)
            {
                this.State = ConversionState.Ready;
            }

            Debug.Log(Debug.CatConversion,
                $"Job initialized: Preset='{this.ConversionPreset.FullName}' OutputType={this.ConversionPreset.OutputType} Input='{this.InputFilePath}' Output='{this.OutputFilePath}' Template='{this.ConversionPreset.OutputFileNameTemplate}'");

            if (this.State != ConversionState.Failed)
            {
                this.UserState = Properties.Resources.ConversionStateInQueue;
            }
        }

        public void StartConversion()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            if (this.State != ConversionState.Ready)
            {
                throw new Exception("Invalid conversion state.");
            }

            Debug.Log(Debug.CatConversion,
                $"Convert START: '{this.InputFilePath}' -> '{this.OutputFilePath}' preset='{this.ConversionPreset.FullName}'");

            // Write progress header when --progress CLI flag is active.
            Diagnostics.ProgressWriter.WriteHeader(
                this.InputFilePath,
                this.ConversionPreset?.FullName ?? string.Empty,
                Diagnostics.MediaProber.GetDuration(this.InputFilePath));

            this.StartTime = DateTime.Now;
            this.State = ConversionState.InProgress;

            try
            {
                this.Convert();
            }
            catch (Exception exception)
            {
                Debug.LogException(Debug.CatConversion, "Convert() threw", exception);
                this.ConversionFailed(exception.Message);
            }

            this.StateFlags = ConversionFlags.None;

            if (this.State == ConversionState.Failed)
            {
                this.OnConversionFailed();
            }
            else
            {
                this.OnConversionSucceed();
            }

            var totalDuration = DateTime.Now - this.StartTime;

            if (this.State == ConversionState.Done && !this.AllOutputFilesExists())
            {
                Debug.LogWarning(Debug.CatConversion,
                    $"Conversion reported Done but output file(s) missing. Details:");
                foreach (var p in this.OutputFilePaths ?? new string[0])
                {
                    bool fileExists = System.IO.File.Exists(p);
                    bool dirExists  = System.IO.Directory.Exists(p);
                    Debug.LogWarning(Debug.CatConversion,
                        $"  expected: '{p}' | file={fileExists} | sameNameDir={dirExists}");
                    if (dirExists)
                    {
                        Debug.LogWarning(Debug.CatConversion,
                            $"  *** A DIRECTORY named '{System.IO.Path.GetFileName(p)}' exists at this path! " +
                            $"This caused the output file to be silently lost. Delete that directory and retry.");
                    }
                }
                this.ConversionFailed($"Conversion appeared to succeed but output file was not found: {this.OutputFilePath}");
            }
            else if (this.State == ConversionState.Failed && this.AtLeastOneOutputFilesExists())
            {
                Debug.Log(Debug.CatConversion, Properties.Resources.ErrorConversionFailedWithOutput);
            }

            Debug.Log(Debug.CatConversion,
                $"Convert END: state={this.State} duration={totalDuration.TotalSeconds:F2}s output='{this.OutputFilePath}'");

            // Write end marker when --progress CLI flag is active.
            Diagnostics.ProgressWriter.WriteEnd(
                this.State == ConversionState.Done,
                this.OutputFilePath);
        }

        public virtual void Cancel()
        {
            if (!this.IsCancelable())
            {
                Debug.Log(Debug.CatConversion,
                    $"Cancel ignored (not cancelable): state={this.State} input='{this.InputFilePath}'");
                return;
            }

            Debug.Log(Debug.CatConversion,
                $"Cancel requested: input='{this.InputFilePath}' state={this.State}");
            this.CancelIsRequested = true;
            this.ConversionFailed(Properties.Resources.ErrorCanceled);
        }

        protected virtual int GetOutputFilesCount()
        {
            return 1;
        }

        protected virtual void Convert()
        {
        }

        protected virtual void Initialize()
        {
        }

        protected virtual void OnConversionFailed()
        {
            Debug.Log("Conversion Failed.");

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                try
                {
                    if (System.IO.File.Exists(outputFilePath))
                    {
                        System.IO.File.Delete(outputFilePath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.Log($"Can't delete file '{outputFilePath}' after conversion job failure.");
                    Debug.Log($"An exception as been thrown: {exception}.");
                }
            }
        }

        protected virtual void OnConversionSucceed()
        {
            Debug.Log("Conversion Succeed!");

            this.ChangeOutputFileTimestampToMatchOriginal();

            // Apply the input post conversion action.
            switch (this.InputPostConversionAction)
            {
                case InputPostConversionAction.None:
                    break;

                case InputPostConversionAction.MoveInArchiveFolder:
                    string basePath = System.IO.Path.GetDirectoryName(this.initialInputPath);
                    string inputFilename = System.IO.Path.GetFileName(this.initialInputPath);
                    string archivePath = basePath + "\\" + this.ConversionPreset.ConversionArchiveFolderName;
                    if (!System.IO.Directory.Exists(archivePath))
                    {
                        System.IO.Directory.CreateDirectory(archivePath);
                    }

                    string newPath = PathHelpers.GenerateUniquePath(archivePath + "\\" + inputFilename);
                    System.IO.File.Move(this.InputFilePath, newPath);
                    Debug.Log($"Input file moved in archive folder: '{newPath}'");
                    break;

                case InputPostConversionAction.Delete:
                    System.IO.File.Delete(this.InputFilePath);
                    Debug.Log($"Input file deleted: '{this.initialInputPath}'");
                    break;
            }

            Debug.Log(string.Empty);

            this.Progress = 1f;
            this.State = ConversionState.Done;
            this.UserState = Properties.Resources.ConversionStateDone;
            Debug.Log("Conversion Done!");
        }

        protected void ConversionFailed(string exitingMessage)
        {
            // Capture a stack trace so we know which code path triggered the failure.
            string[] frames = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false)
                .ToString()
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int frameCount = Math.Min(6, frames.Length);
            var sb = new System.Text.StringBuilder();
            for (int fi = 0; fi < frameCount; fi++) { sb.Append("\n    ").Append(frames[fi].Trim()); }

            Debug.LogErrorSilent(Debug.CatConversion,
                $"ConversionFailed: {exitingMessage}" +
                $"\n  input  = '{this.InputFilePath}'" +
                $"\n  output = '{this.OutputFilePath}'" +
                $"\n  preset = '{this.ConversionPreset?.FullName}'" +
                $"\n  caller = {sb}");

            if (this.State == ConversionState.Failed)
            {
                // Already failed, don't override informations.
                return;
            }

            this.State = ConversionState.Failed;
            this.UserState = Properties.Resources.ConversionStateFailed;
            this.ErrorMessage = exitingMessage;
        }

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (this.PropertyChanged != null)
            {
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void ChangeOutputFileTimestampToMatchOriginal()
        {
            Debug.Log("Changing output files timestamp to match original timestamp ...");

            var originalFileCreationTime = System.IO.File.GetCreationTimeUtc(this.InputFilePath);
            var originalFileLastAccesTime = System.IO.File.GetLastAccessTimeUtc(this.InputFilePath);
            var originalFileLastWriteTime = System.IO.File.GetLastWriteTimeUtc(this.InputFilePath);
            Debug.Log($"  original timestamp: {originalFileCreationTime}, {originalFileLastAccesTime}, {originalFileLastWriteTime}");

            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];
                try
                {
                    System.IO.File.SetCreationTimeUtc(outputFilePath, originalFileCreationTime);
                    System.IO.File.SetLastAccessTimeUtc(outputFilePath, originalFileLastAccesTime);
                    System.IO.File.SetLastWriteTimeUtc(outputFilePath, originalFileLastWriteTime);
                    Debug.Log($"  output file '{outputFilePath}' timestamp changed");
                }
                catch (Exception exception)
                {
                    Debug.Log($"Can't change timestamp from file '{outputFilePath}'");
                    Debug.Log($"An exception as been thrown: {exception}.");
                }
            }

            Debug.Log("... timestamp matching finished.");
        }

        private bool AllOutputFilesExists()
        {
            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];

                // Handle ffmpeg sequence output pattern like "path/prefix_%06d.png".
                // Check if at least one file matching the pattern exists in the output directory.
                if (System.Text.RegularExpressions.Regex.IsMatch(outputFilePath, @"%0?\d*d"))
                {
                    string dir = System.IO.Path.GetDirectoryName(outputFilePath);
                    string fileName = System.IO.Path.GetFileName(outputFilePath);
                    if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                    {
                        Debug.LogWarning(Debug.CatConversion,
                            $"AllOutputFilesExists: output dir missing for sequence pattern '{outputFilePath}'");
                        return false;
                    }

                    // Convert %06d to * wildcard for Directory.GetFiles search.
                    string searchPattern = System.Text.RegularExpressions.Regex.Replace(fileName, @"%0?\d*d", "*");
                    var files = System.IO.Directory.GetFiles(dir, searchPattern);
                    Debug.Log(Debug.CatConversion,
                        $"AllOutputFilesExists: sequence pattern '{searchPattern}' in '{dir}' matched {files.Length} file(s)");
                    if (files.Length == 0)
                    {
                        return false;
                    }

                    continue;
                }

                if (!System.IO.File.Exists(outputFilePath))
                {
                    Debug.LogWarning(Debug.CatConversion,
                        $"AllOutputFilesExists: file missing: '{outputFilePath}'");
                    return false;
                }
            }

            return true;
        }

        private bool AtLeastOneOutputFilesExists()
        {
            for (int index = 0; index < this.OutputFilePaths.Length; index++)
            {
                string outputFilePath = this.OutputFilePaths[index];

                // Handle ffmpeg sequence output pattern.
                if (System.Text.RegularExpressions.Regex.IsMatch(outputFilePath, @"%0?\d*d"))
                {
                    string dir = System.IO.Path.GetDirectoryName(outputFilePath);
                    string fileName = System.IO.Path.GetFileName(outputFilePath);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    {
                        string searchPattern = System.Text.RegularExpressions.Regex.Replace(fileName, @"%0?\d*d", "*");
                        if (System.IO.Directory.GetFiles(dir, searchPattern).Length > 0)
                        {
                            return true;
                        }
                    }

                    continue;
                }

                if (System.IO.File.Exists(outputFilePath))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
