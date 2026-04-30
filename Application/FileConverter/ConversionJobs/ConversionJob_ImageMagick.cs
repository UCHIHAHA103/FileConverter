// <copyright file="ConversionJob_ImageMagick.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;

    using FileConverter.Diagnostics;
    using ImageMagick;

    public class ConversionJob_ImageMagick : ConversionJob
    {
        private const float BaseDpiForPdfConversion = 200f;
        private const int PdfSuperSamplingRatio = 1;

        private bool isInputFilePdf;
        private int pageCount;

        public ConversionJob_ImageMagick() : base()
        {
        }

        public ConversionJob_ImageMagick(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override void Initialize()
        {
            base.Initialize();

            string applicationDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            MagickNET.SetGhostscriptDirectory(applicationDirectory);

            this.isInputFilePdf = System.IO.Path.GetExtension(this.InputFilePath).ToLowerInvariant() == ".pdf";

            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }
        }

        protected override int GetOutputFilesCount()
        {
            if (System.IO.Path.GetExtension(this.InputFilePath).ToLowerInvariant() == ".pdf")
            {
                using (MagickImageCollection images = new MagickImageCollection())
                {
                    MagickReadSettings settings = new MagickReadSettings();
                    settings.Density = new Density(1, 1);
                    images.Read(this.InputFilePath);

                    return images.Count;
                }
            }

            return 1;
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            this.CurrentOutputFilePathIndex = 0;

            if (this.isInputFilePdf)
            {
                this.ConvertPdf();
            }
            else
            {
                this.pageCount = 1;
                MagickReadSettings readSettings = new MagickReadSettings();

                string inputExtension = System.IO.Path.GetExtension(this.InputFilePath).ToLowerInvariant();
                switch (inputExtension)
                {
                    case ".avif":
                        // Explicitly set AVIF format for proper reading
                        readSettings.Format = MagickFormat.Avif;
                        break;

                    case ".cr2":
                        // Requires an explicit image format otherwise the image is interpreted as a TIFF image.
                        readSettings.Format = MagickFormat.Cr2;
                        break;

                    case ".dng":
                        // Requires an explicit image format otherwise the image is interpreted as a TIFF image.
                        readSettings.Format = MagickFormat.Dng;
                        break;

                    case ".gif":
                        // Get the first frame of the gif for conversion.
                        // Maybe in the future make this user selectable.
                        readSettings.FrameIndex = 0;
                        break;

                    case ".svg":
                        // SVG is a vector format; set high density for sharp rasterization.
                        // Without this, SVG renders at a low default resolution (#129).
                        readSettings.Density = new Density(300, 300);
                        readSettings.BackgroundColor = MagickColors.Transparent;
                        break;

                    default:
                        break;
                }

                using (MagickImage image = new MagickImage(this.InputFilePath, readSettings))
                {
                    Debug.Log($"Load image {this.InputFilePath} succeed.");
                    this.ConvertImage(image);
                }
            }
        }

        private void ConvertPdf()
        {
            MagickReadSettings settings = new MagickReadSettings();

            float dpi = BaseDpiForPdfConversion;
            float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
            if (Math.Abs(scaleFactor - 1f) >= 0.005f)
            {
                Debug.Log($"Apply scale factor: {scaleFactor * 100}%.");

                dpi *= scaleFactor;
            }

            Debug.Log($"Density: {dpi}dpi.");
            settings.Density = new Density(dpi * PdfSuperSamplingRatio);

            this.UserState = Properties.Resources.ConversionStateReadDocument;

            using (MagickImageCollection images = new MagickImageCollection())
            {
                // Add all the pages of the pdf file to the collection
                images.Read(this.InputFilePath, settings);
                Debug.Log($"Load pdf {this.InputFilePath} succeed.");

                this.pageCount = images.Count;

                this.UserState = Properties.Resources.ConversionStateConversion;

                foreach (MagickImage image in images)
                {
                    Debug.Log($"Write page {this.CurrentOutputFilePathIndex + 1}/{this.pageCount}.");

                    // PDF pages have no explicit background — set white to avoid
                    // black areas when converting to image formats (#251 #643 #561).
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);

                    if (PdfSuperSamplingRatio > 1)
                    {
#pragma warning disable CS0162 // Unreachable code detected
                        image.Scale(new Percentage(100 / PdfSuperSamplingRatio));
#pragma warning restore CS0162 // Unreachable code detected
                    }

                    this.ConvertImage(image, true);
                    
                    this.CurrentOutputFilePathIndex++;
                }
            }
        }

        private void ConvertImage(MagickImage image, bool ignoreScale = false)
        {
            image.Progress += this.Image_Progress;

            if (!ignoreScale && this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageScale))
            {
                float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                {
                    Debug.Log($"Apply scale factor: {scaleFactor * 100}%.");

                    image.Scale(new Percentage(scaleFactor * 100f));
                }
            }

            if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageRotation))
            {
                float rotateAngleInDegrees = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageRotation);
                if (Math.Abs(rotateAngleInDegrees - 0f) >= 0.05f)
                {
                    Debug.Log($"Apply rotation: {rotateAngleInDegrees}°.");

                    image.Rotate(rotateAngleInDegrees);
                }
            }

            if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2))
            {
                bool clampSizeToPowerOf2 = this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.ImageClampSizePowerOf2);
                if (clampSizeToPowerOf2)
                {
                    uint referenceSize = System.Math.Min(image.Width, image.Height);
                    uint size = 2;
                    while (size * 2 <= referenceSize)
                    {
                        size *= 2;
                    }

                    Debug.Log($"Clamp size to the nearest power of 2 size (from {image.Width}x{image.Height} to {size}x{size}).");

                    image.Scale(size, size);
                }
            }

            if (this.ConversionPreset.IsRelevantSetting(ConversionPreset.ConversionSettingKeys.ImageMaximumSize))
            {
                uint maximumSize = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageMaximumSize);
                if (maximumSize > 0)
                {
                    uint width = System.Math.Min(image.Width, maximumSize);
                    uint height = System.Math.Min(image.Height, maximumSize);

                    Debug.Log($"Clamp size to maximum size of {width}x{width} (from {image.Width}x{image.Height} to {width}x{height}).");

                    image.Scale(width, height);
                }
            }

            Debug.Log($"Convert image (output: {this.OutputFilePath}).");

            // Preserve EXIF/IPTC/ICC metadata during conversion (#599 #568).
            // By default ImageMagick keeps profiles, but some operations (like
            // color space conversion) can drop them. We explicitly log the
            // profile state for diagnostics.
            var exifProfile = image.GetExifProfile();
            if (exifProfile != null)
            {
                Debug.Log($"Input has EXIF profile ({exifProfile.Values.Count} tags).");

                // Auto-orient based on EXIF orientation tag, then remove the
                // orientation tag so viewers don't double-rotate.
                image.AutoOrient();
            }

            var iptcProfile = image.GetIptcProfile();
            if (iptcProfile != null)
            {
                Debug.Log("Input has IPTC profile.");
            }

            // Auto-convert CMYK color space to sRGB for screen display (#42).
            if (image.ColorSpace == ColorSpace.CMYK)
            {
                Debug.Log("Converting CMYK color space to sRGB.");
                image.ColorSpace = ColorSpace.sRGB;
            }

            // For formats that don't support transparency (JPG, PDF), composite
            // onto a white background to avoid black areas (#129 SVG alpha).
            if (this.ConversionPreset.OutputType == OutputType.Jpg ||
                this.ConversionPreset.OutputType == OutputType.Pdf)
            {
                if (image.HasAlpha)
                {
                    Debug.Log("Flattening alpha channel onto white background.");
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);
                }
            }

            // For SVG input, set a high density before rasterization for sharp output.
            string inputExt = System.IO.Path.GetExtension(this.InputFilePath).ToLowerInvariant();
            if (inputExt == ".svg" && image.Density.X < 150)
            {
                Debug.Log("SVG input detected with low density, quality may be reduced.");
            }

            switch (this.ConversionPreset.OutputType)
            {
                case OutputType.Avif:
                    image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                    break;

                case OutputType.Png:
                    // http://stackoverflow.com/questions/27267073/imagemagick-lossless-max-compression-for-png
                    image.Quality = 95;
                    break;

                case OutputType.Jpg:
                    image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                    break;

                case OutputType.Pdf:
                    Debug.Log($"Density: {BaseDpiForPdfConversion}dpi.");
                    image.Density = new Density(BaseDpiForPdfConversion);
                    break;

                case OutputType.Webp:
                    image.Quality = this.ConversionPreset.GetSettingsValue<uint>(ConversionPreset.ConversionSettingKeys.ImageQuality);
                    break;

                default:
                    this.ConversionFailed(string.Format(Properties.Resources.ErrorUnsupportedOutputFormat, this.ConversionPreset.OutputType));
                    image.Progress -= this.Image_Progress;
                    return;
            }

            image.Write(this.OutputFilePath);
            image.Progress -= this.Image_Progress;
        }

        private void Image_Progress(object sender, ProgressEventArgs eventArgs)
        {
            if (this.CancelIsRequested)
            {
                eventArgs.Cancel = true;
                return;
            }

            float alreadyCompletedPages = this.CurrentOutputFilePathIndex / (float)this.pageCount;
            this.Progress = alreadyCompletedPages + ((float)eventArgs.Progress.ToDouble() / (100f * this.pageCount));
        }
    }
}
