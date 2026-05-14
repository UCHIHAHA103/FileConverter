// <copyright file="ConversionJob_Gif.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public class ConversionJob_Gif : ConversionJob
    {
        private string intermediateFilePath = string.Empty;
        private ConversionJob pngConversionJob = null;
        private ConversionJob gifConversionJob = null;

        public ConversionJob_Gif(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public override void Cancel()
        {
            base.Cancel();

            this.pngConversionJob.Cancel();
            this.gifConversionJob.Cancel();
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            string extension = System.IO.Path.GetExtension(this.InputFilePath);
            extension = extension.ToLowerInvariant().Substring(1, extension.Length - 1);

            string inputFilePath = string.Empty;

            // If the input is a static image (not a format ffmpeg can handle natively for animation),
            // convert it to PNG first before sending to ffmpeg for GIF creation.
            // Skip this for formats that ffmpeg handles natively (including animated webp).
            bool needsPngIntermediate = Helpers.GetExtensionCategory(extension) == Helpers.InputCategoryNames.Image
                && extension != "png"
                && extension != "webp"; // ffmpeg handles animated webp natively; don't flatten to single frame.

            if (needsPngIntermediate)
            {
                // Generate intermediate file path.
                string fileName = Path.GetFileName(this.OutputFilePath);
                string tempPath = Path.GetTempPath();
                this.intermediateFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + ".png");

                // Convert input in png file to send it to ffmpeg for the gif conversion.
                ConversionPreset intermediatePreset = new ConversionPreset("To compatible image", OutputType.Png, this.ConversionPreset.InputTypes.ToArray());
                this.pngConversionJob = ConversionJobFactory.Create(intermediatePreset, this.InputFilePath);
                this.pngConversionJob.PrepareConversion(null, this.intermediateFilePath);

                inputFilePath = this.intermediateFilePath;
            }
            else
            {
                inputFilePath = this.InputFilePath;
            }

            // Convert png file into ico.
            this.gifConversionJob = new ConversionJob_FFMPEG(this.ConversionPreset, inputFilePath);
            this.gifConversionJob.PrepareConversion(null, this.OutputFilePath);
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            Task updateProgress = this.UpdateProgress();

            if (this.pngConversionJob != null)
            {
                this.UserState = Properties.Resources.ConversionStateReadIntputImage;

                Diagnostics.Debug.Log(string.Empty);
                Diagnostics.Debug.Log("Convert image to PNG (intermediate format).");
                this.pngConversionJob.StartConversion();

                if (this.pngConversionJob.State != ConversionState.Done)
                {
                    this.ConversionFailed(this.pngConversionJob.ErrorMessage);
                    return;
                }
            }

            Diagnostics.Debug.Log(string.Empty);
            Diagnostics.Debug.Log("Convert png intermediate image to gif.");
            this.gifConversionJob.StartConversion();

            if (this.gifConversionJob.State != ConversionState.Done)
            {
                this.ConversionFailed(this.gifConversionJob.ErrorMessage);
                return;
            }

            if (!string.IsNullOrEmpty(this.intermediateFilePath))
            {
                Diagnostics.Debug.Log($"Delete intermediate file {this.intermediateFilePath}.");

                File.Delete(this.intermediateFilePath);
            }

            updateProgress.Wait();
        }

        private async Task UpdateProgress()
        {
            while (this.gifConversionJob.State != ConversionState.Done &&
                   this.gifConversionJob.State != ConversionState.Failed)
            {
                if (this.pngConversionJob != null && this.pngConversionJob.State == ConversionState.InProgress)
                {
                    this.Progress = this.pngConversionJob.Progress;
                }

                if (this.gifConversionJob != null && this.gifConversionJob.State == ConversionState.InProgress)
                {
                    this.Progress = this.gifConversionJob.Progress;
                    this.UserState = this.gifConversionJob.UserState;
                }

                await Task.Delay(40);
            }
        }
    }
}
