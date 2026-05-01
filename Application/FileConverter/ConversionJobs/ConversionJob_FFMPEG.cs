// <copyright file="ConversionJob_FFMPEG.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using CommunityToolkit.Mvvm.DependencyInjection;
    using FileConverter.Controls;
    using FileConverter.Services;

    public partial class ConversionJob_FFMPEG : ConversionJob
    {
        private readonly Regex durationRegex = new Regex(@"Duration:\s*([0-9][0-9]):([0-9][0-9]):([0-9][0-9])\.([0-9][0-9]),.*bitrate:\s*([0-9]+) kb\/s");
        private readonly Regex progressRegex = new Regex(@"size=\s*([0-9]+).*time=([0-9][0-9]):([0-9][0-9]):([0-9][0-9]).([0-9][0-9])\s+bitrate=\s*([0-9]+.[0-9])");

        private TimeSpan fileDuration;
        private TimeSpan actualConvertedDuration;

        private ProcessStartInfo ffmpegProcessStartInfo;

        private readonly List<FFMpegPass> ffmpegArgumentStringByPass = new List<FFMpegPass>();

        ISettingsService settingsService;

        public ConversionJob_FFMPEG() : base()
        {
        }

        public ConversionJob_FFMPEG(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        public static VideoEncodingSpeed[] VideoEncodingSpeeds => new[]
           {
               VideoEncodingSpeed.UltraFast,
               VideoEncodingSpeed.SuperFast,
               VideoEncodingSpeed.VeryFast,
               VideoEncodingSpeed.Faster,
               VideoEncodingSpeed.Fast,
               VideoEncodingSpeed.Medium,
               VideoEncodingSpeed.Slow,
               VideoEncodingSpeed.Slower,
               VideoEncodingSpeed.VerySlow,
           };

        protected virtual string FfmpegPath
        {
            get
            {
                string applicationDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.Combine(applicationDirectory, "ffmpeg.exe");
            }
        }

        protected override void Initialize()
        {
            base.Initialize();

            this.settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            this.ffmpegProcessStartInfo = null;

            string ffmpegPath = this.FfmpegPath;
            if (!System.IO.File.Exists(ffmpegPath))
            {
                this.ConversionFailed(Properties.Resources.ErrorCantFindFFMPEG);
                Diagnostics.Debug.Log($"Can't find ffmpeg executable ({ffmpegPath}). Try to reinstall the application.");
                return;
            }

            this.ffmpegProcessStartInfo = new ProcessStartInfo(ffmpegPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                // Redirect stdin so we can send 'q' for graceful termination.
                RedirectStandardInput = true,
                // Do NOT redirect stdout: ffmpeg does not write to stdout in our usage and
                // redirecting it without reading would eventually fill the pipe buffer and
                // deadlock the process. See upstream PR #732 and issues #749 #740 #739
                // #716 #703 #700 (the "v2.2 converting process is getting stuck" regression).
                RedirectStandardOutput = false,
                RedirectStandardError = true
            };

            this.FillFFMpegArgumentsList();
        }

        protected virtual void FillFFMpegArgumentsList()
        {
            // -progress pipe:1 would send structured progress data to stdout, but since
            // stdout is no longer redirected (see above), we must not pass this flag.
            // Progress is still parsed from stderr via the legacy "size=... time=..." format.
            // Use -y to allow overwriting output files (#636); deduplication is handled by
            // GenerateUniquePath in PrepareConversion.
            // -y: allow overwrite (#636). -fflags +genpts: fix broken PTS in MKV/AVI (#748 #709).
            const string baseArgs = "-y -fflags +genpts";

            bool customCommandEnabled = this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableFFMPEGCustomCommand);
            if (customCommandEnabled)
            {
                // Custom command override other settings.
                string customCommand = this.ConversionPreset.GetSettingsValue<string>(ConversionPreset.ConversionSettingKeys.FFMPEGCustomCommand) ?? string.Empty;

                string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {customCommand} \"{this.OutputFilePath}\"";
                this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));

                return;
            }

            // This option are necessary to be able to read metadata on Windows. src: http://jonhall.info/how_to/create_id3_tags_using_ffmpeg
            const string MP3MetadataArgs = "-id3v2_version 3 -write_id3v1 1";

            // AAC have no standard tag system, use ApeV2 (that are compatible). src: http://eolindel.free.fr/foobar/tags.shtml
            const string AACMetadataArgs = "-write_apetag 1";

            switch (this.ConversionPreset.OutputType)
            {
                case OutputType.Aac:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        // https://trac.ffmpeg.org/wiki/Encode/AAC
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        string encoderArgs = $"-vn -c:a aac -q:a {this.AACBitrateToQualityIndex(audioEncodingBitrate)} {channelArgs} {AACMetadataArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Alac:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        // ALAC is lossless — no bitrate/quality setting needed.
                        // -c:v copy preserves embedded cover art from the input file.
                        string encoderArgs = $"-c:a alac -c:v copy {channelArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Avi:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/MPEG-4
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a libmp3lame -qscale:a {this.MP3VBRBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        // Compute final arguments.
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);
                        string encoderArgs = $"-c:v mpeg4 -vtag xvid -qscale:v {this.MPEG4QualityToQualityIndex(videoEncodingQuality)} {audioArgs} {videoFilteringArgs} {MP3MetadataArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Flac:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        // http://taer-naguur.blogspot.fr/2013/11/flac-audio-encoding-with-ffmpeg.html
                        string encoderArgs = $"-vn -compression_level 12 {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Gif:
                    {
                        // http://blog.pkh.me/p/21-high-quality-gif-with-ffmpeg.html
                        string fileName = Path.GetFileName(this.InputFilePath);
                        string tempPath = Path.GetTempPath();
                        string paletteFilePath = PathHelpers.GenerateUniquePath(tempPath + fileName + " - palette.png");

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);

                        // fps.
                        int framesPerSecond = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoFramesPerSecond);
                        if (!string.IsNullOrEmpty(transformArgs))
                        {
                            transformArgs += ",";
                        }

                        transformArgs += $"fps={framesPerSecond}";

                        // Generate palette.
                        string encoderArgs = $"-vf \"{transformArgs},palettegen\"";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{paletteFilePath}\"";
                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass("Indexing colors", arguments, paletteFilePath));

                        // Create gif. -loop 0 ensures infinite looping (#746 #513 #142 #38).
                        encoderArgs = $"-i \"{paletteFilePath}\" -lavfi \"{transformArgs},paletteuse\" -loop 0";
                        arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";
                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ico:
                    {
                        string encoderArgs = string.Empty;
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Jpg:
                    {
                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.ImageQuality);

                        float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                        string scaleArgs = string.Empty;
                        if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                        {
                            scaleArgs = $"-vf scale=iw*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}:ih*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}";
                        }

                        string encoderArgs = $"-q:v {this.JPGQualityToQualityIndex(encodingQuality)} {scaleArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Mp3:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        string encoderArgs = string.Empty;
                        EncodingMode encodingMode = this.ConversionPreset.GetSettingsValue<EncodingMode>(ConversionPreset.ConversionSettingKeys.AudioEncodingMode);
                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        switch (encodingMode)
                        {
                            case EncodingMode.Mp3VBR:
                                encoderArgs = $"-vn -codec:a libmp3lame -q:a {this.MP3VBRBitrateToQualityIndex(encodingQuality)} {channelArgs} {MP3MetadataArgs}";
                                break;

                            case EncodingMode.Mp3CBR:
                                encoderArgs = $"-vn -codec:a libmp3lame -b:a {encodingQuality}k {channelArgs} {MP3MetadataArgs}";
                                break;

                            default:
                                break;
                        }

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Mkv:
                case OutputType.Mp4:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/H.264
                        // https://trac.ffmpeg.org/wiki/Encode/AAC
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        VideoEncodingSpeed videoEncodingSpeed = this.ConversionPreset.GetSettingsValue<VideoEncodingSpeed>(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        Helpers.HardwareAccelerationMode hwAccel = settingsService.Settings.HardwareAccelerationMode;

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset, hwAccel);
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a aac -qscale:a {this.AACBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        string videoCodec = "libx264";
                        string videoCodecArgs = $"-preset {this.H264EncodingSpeedToPreset(videoEncodingSpeed)} -crf {this.H264QualityToCRF(videoEncodingQuality)}";
                        string hwAccelArg = string.Empty;

                        switch (hwAccel)
                        {
                            case Helpers.HardwareAccelerationMode.CUDA:
                                videoCodec = "h264_nvenc";
                                int nvencQP = this.H264QualityToCRF(videoEncodingQuality);
                                videoCodecArgs = $"-preset {this.H264EncodingSpeedToNVENCPreset(videoEncodingSpeed)} -rc constqp -qp {nvencQP}";

                                hwAccelArg = "-hwaccel cuda -hwaccel_output_format cuda";
                                break;

                            case Helpers.HardwareAccelerationMode.AMF:
                                int amfQP = this.H264QualityToCRF(videoEncodingQuality);
                                int amfBFrameQP = Math.Min(51, amfQP + 2);
                                videoCodec = "h264_amf";
                                videoCodecArgs = $"-usage transcoding -quality {this.H264EncodingSpeedToAMFQuality(videoEncodingSpeed)} -qp_i {amfQP} -qp_p {amfQP} -qp_b {amfBFrameQP}";
                                break;
                        }

                        // -movflags +faststart moves moov atom to start of file for streaming and thumbnail display (#270).
                        string movflags = this.ConversionPreset.OutputType == OutputType.Mp4 ? "-movflags +faststart" : string.Empty;
                        // -map 0:v:0 -map 0:a? preserves all audio tracks from MKV inputs (#438 #289 T-F6).
                        // The '?' suffix means "don't fail if no audio stream exists".
                        string mapArgs = "-map 0:v:0 -map 0:a?";
                        string encoderArgs = $"{mapArgs} -c:v {videoCodec} {videoCodecArgs} {audioArgs} {videoFilteringArgs} {movflags}";

                        string arguments = $"{baseArgs} {hwAccelArg} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ogg:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        int encodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);
                        string encoderArgs = $"-vn -codec:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(encodingQuality)} {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Ogv:
                    {
                        // https://trac.ffmpeg.org/wiki/TheoraVorbisEncodingGuide
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);

                        // Theora requires yuv420p pixel format (#678).
                        transformArgs += (transformArgs.Length > 0 ? "," : string.Empty) + "format=yuv420p";
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-codec:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(audioEncodingBitrate)}";
                        }

                        string encoderArgs = $"-codec:v libtheora -qscale:v {this.OGVTheoraQualityToQualityIndex(videoEncodingQuality)} {audioArgs} {videoFilteringArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Png:
                    {
                        float scaleFactor = this.ConversionPreset.GetSettingsValue<float>(ConversionPreset.ConversionSettingKeys.ImageScale);
                        string scaleArgs = string.Empty;
                        if (Math.Abs(scaleFactor - 1f) >= 0.005f)
                        {
                            scaleArgs = $"-vf scale=iw*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}:ih*{scaleFactor.ToString("#.##", CultureInfo.InvariantCulture)}";
                        }

                        // http://www.howtogeek.com/203979/is-the-png-format-lossless-since-it-has-a-compression-parameter/
                        // -pix_fmt rgba preserves alpha/transparency channel (#676 #129).
                        string encoderArgs = $"-pix_fmt rgba -compression_level 100 {scaleArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Wav:
                    {
                        string channelArgs = ConversionJob_FFMPEG.ComputeAudioChannelArgs(this.ConversionPreset);

                        EncodingMode encodingMode = this.ConversionPreset.GetSettingsValue<EncodingMode>(ConversionPreset.ConversionSettingKeys.AudioEncodingMode);
                        string encoderArgs = $"-vn -acodec {this.WAVEncodingToCodecArgument(encodingMode)} {channelArgs}";
                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                case OutputType.Webm:
                    {
                        // https://trac.ffmpeg.org/wiki/Encode/VP9
                        int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
                        int audioEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

                        string encodingArgs = string.Empty;
                        if (videoEncodingQuality == 63)
                        {
                            // Replace maximum quality settings by lossless compression.
                            encodingArgs = $"-lossless 1";
                        }
                        else
                        {
                            encodingArgs = $"-crf {this.WebmQualityToCRF(videoEncodingQuality)} -b:v 0";
                        }

                        string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset);
                        string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

                        string audioArgs = "-an";
                        if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
                        {
                            audioArgs = $"-c:a libvorbis -qscale:a {this.OGGVBRBitrateToQualityIndex(audioEncodingQuality)}";
                        }

                        string encoderArgs = $"-c:v libvpx-vp9 {encodingArgs} {audioArgs} {videoFilteringArgs}";

                        string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";

                        this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
                    }

                    break;

                default:
                    throw new NotImplementedException("Converter not implemented for output file type " +
                                                      this.ConversionPreset.OutputType);
            }

            if (this.ffmpegArgumentStringByPass.Count == 0)
            {
                throw new Exception("No ffmpeg arguments generated.");
            }

            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                if (string.IsNullOrEmpty(this.ffmpegArgumentStringByPass[index].Arguments))
                {
                    throw new Exception("Invalid ffmpeg process arguments.");
                }
            }
        }
        
        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new Exception("The conversion preset must be valid.");
            }

            var fullStderrLog = new System.Text.StringBuilder();

            // Run all passes.
            this.RunAllPasses(fullStderrLog);

            // GPU auto-fallback: if hardware encoding failed, retry with software.
            this.TrySoftwareFallback(fullStderrLog);

            // Write ffmpeg stderr log to Logs/ folder.
            WriteConversionLog(this.InputFilePath, fullStderrLog);

            // Clean up old log files (keep last 100).
            CleanOldLogs();

            Diagnostics.Debug.Log(string.Empty);

            // Clean intermediate files.
            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                FFMpegPass currentPass = this.ffmpegArgumentStringByPass[index];

                if (string.IsNullOrEmpty(currentPass.FileToDelete))
                {
                    continue;
                }

                Diagnostics.Debug.Log($"Delete intermediate file {currentPass.FileToDelete}.");

                File.Delete(currentPass.FileToDelete);
            }
        }

        /// <summary>
        /// Execute all ffmpeg passes, reading stderr and updating progress.
        /// </summary>
        private void RunAllPasses(System.Text.StringBuilder stderrLog, string logPrefix = "")
        {
            for (int index = 0; index < this.ffmpegArgumentStringByPass.Count; index++)
            {
                // Skip remaining passes if cancel was requested (#T-P3).
                if (this.CancelIsRequested)
                {
                    break;
                }

                FFMpegPass currentPass = this.ffmpegArgumentStringByPass[index];

                this.UserState = currentPass.Name;
                this.ffmpegProcessStartInfo.Arguments = currentPass.Arguments;

                Diagnostics.Debug.Log($"{logPrefix}Execute command: {this.ffmpegProcessStartInfo.FileName} {this.ffmpegProcessStartInfo.Arguments}.");
                Diagnostics.Debug.Log(string.Empty);

                try
                {
                    using (Process exeProcess = Process.Start(this.ffmpegProcessStartInfo))
                    {
                        using (StreamReader reader = exeProcess.StandardError)
                        {
                            while (!reader.EndOfStream)
                            {
                                if (this.CancelIsRequested && !exeProcess.HasExited)
                                {
                                    this.GracefullyTerminateProcess(exeProcess);
                                    break;
                                }

                                string result = reader.ReadLine();

                                this.ParseFFMPEGOutput(result);
                                stderrLog.AppendLine(result);

                                Diagnostics.Debug.Log($"ffmpeg output: {result}");
                            }
                        }

                        if (!exeProcess.HasExited)
                        {
                            exeProcess.WaitForExit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.Debug.Log($"FFmpeg launch exception: {ex.Message}");
                    this.ConversionFailed(Properties.Resources.ErrorFailedToLaunchFFMPEG);
                    throw;
                }
            }
        }

        /// <summary>
        /// Send 'q' to ffmpeg stdin for graceful shutdown, then force kill after 3 seconds.
        /// </summary>
        private void GracefullyTerminateProcess(Process process)
        {
            try
            {
                process.StandardInput.Write("q");
                process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.Log($"Failed to send 'q' to ffmpeg: {ex.Message}");
            }

            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    Diagnostics.Debug.Log($"Failed to kill ffmpeg process: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// If hardware encoding failed, retry with software encoding (libx264).
        /// </summary>
        private void TrySoftwareFallback(System.Text.StringBuilder stderrLog)
        {
            if (this.State != ConversionState.Failed || this.CancelIsRequested)
            {
                return;
            }

            Helpers.HardwareAccelerationMode hwAccel = settingsService.Settings.HardwareAccelerationMode;
            if (hwAccel == Helpers.HardwareAccelerationMode.Off)
            {
                return;
            }

            if (this.ConversionPreset.OutputType != OutputType.Mkv && this.ConversionPreset.OutputType != OutputType.Mp4)
            {
                return;
            }

            Diagnostics.Debug.Log($"Hardware encoding ({hwAccel}) failed. Retrying with software encoder (libx264)...");

            this.ResetStateForRetry();

            this.ffmpegArgumentStringByPass.Clear();
            this.FillFFMpegArgumentsListSoftwareFallback();

            this.RunAllPasses(stderrLog, "[SW Fallback] ");
        }

        /// <summary>
        /// Reset internal state so the conversion can be retried (used by GPU fallback).
        /// </summary>
        private void ResetStateForRetry()
        {
            // Use reflection to reset State to InProgress since ConversionFailed has set it to Failed.
            var stateProp = typeof(ConversionJob).GetProperty("State",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (stateProp != null)
            {
                stateProp.SetValue(this, ConversionState.InProgress);
            }
            else
            {
                Diagnostics.Debug.Log("Warning: could not find State property via reflection for retry reset.");
            }

            this.Progress = 0f;

            if (System.IO.File.Exists(this.OutputFilePath))
            {
                try
                {
                    System.IO.File.Delete(this.OutputFilePath);
                }
                catch (Exception ex)
                {
                    Diagnostics.Debug.Log($"Failed to delete output file for retry: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Build ffmpeg arguments using software encoding (libx264) as a fallback.
        /// </summary>
        private void FillFFMpegArgumentsListSoftwareFallback()
        {
            const string baseArgs = "-y -fflags +genpts"; // Allow overwrite (#636) + fix broken PTS (#748)

            int videoEncodingQuality = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.VideoQuality);
            VideoEncodingSpeed videoEncodingSpeed = this.ConversionPreset.GetSettingsValue<VideoEncodingSpeed>(ConversionPreset.ConversionSettingKeys.VideoEncodingSpeed);
            int audioEncodingBitrate = this.ConversionPreset.GetSettingsValue<int>(ConversionPreset.ConversionSettingKeys.AudioBitrate);

            string transformArgs = ConversionJob_FFMPEG.ComputeTransformArgs(this.ConversionPreset, Helpers.HardwareAccelerationMode.Off);
            string videoFilteringArgs = ConversionJob_FFMPEG.Encapsulate("-vf", transformArgs);

            string audioArgs = "-an";
            if (this.ConversionPreset.GetSettingsValue<bool>(ConversionPreset.ConversionSettingKeys.EnableAudio))
            {
                audioArgs = $"-c:a aac -qscale:a {this.AACBitrateToQualityIndex(audioEncodingBitrate)}";
            }

            string movflags = this.ConversionPreset.OutputType == OutputType.Mp4 ? "-movflags +faststart" : string.Empty;
            string encoderArgs = $"-c:v libx264 -preset {this.H264EncodingSpeedToPreset(videoEncodingSpeed)} -crf {this.H264QualityToCRF(videoEncodingQuality)} {audioArgs} {videoFilteringArgs} {movflags}";
            string arguments = $"{baseArgs} -i \"{this.InputFilePath}\" {encoderArgs} \"{this.OutputFilePath}\"";
            this.ffmpegArgumentStringByPass.Add(new FFMpegPass(arguments));
        }

        /// <summary>
        /// Write ffmpeg stderr output to a log file in the Logs/ folder.
        /// </summary>
        private static void WriteConversionLog(string inputFilePath, System.Text.StringBuilder stderrLog)
        {
            try
            {
                string logsDir = System.IO.Path.Combine(
                    FileConverterExtension.PathHelpers.GetUserDataFolderPath, "Logs");
                if (!System.IO.Directory.Exists(logsDir))
                {
                    System.IO.Directory.CreateDirectory(logsDir);
                }

                string safeFileName = System.IO.Path.GetFileNameWithoutExtension(inputFilePath);
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    safeFileName = safeFileName.Replace(c, '_');
                }

                string logFilePath = System.IO.Path.Combine(logsDir,
                    $"{safeFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                System.IO.File.WriteAllText(logFilePath, stderrLog.ToString());
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.Log($"Failed to write ffmpeg log: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean old log files, keeping only the most recent 100 files.
        /// </summary>
        private static void CleanOldLogs()
        {
            try
            {
                string logsDir = System.IO.Path.Combine(
                    FileConverterExtension.PathHelpers.GetUserDataFolderPath, "Logs");
                if (!System.IO.Directory.Exists(logsDir))
                {
                    return;
                }

                var logFiles = new System.IO.DirectoryInfo(logsDir)
                    .GetFiles("*.log")
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(100);

                foreach (var file in logFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Debug.Log($"Failed to delete old log: {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Debug.Log($"Failed to clean old logs: {ex.Message}");
            }
        }

        private void ParseFFMPEGOutput(string input)
        {
            Match match = this.durationRegex.Match(input);
            if (match.Success && match.Groups.Count >= 6)
            {
                int hours = int.Parse(match.Groups[1].Value);
                int minutes = int.Parse(match.Groups[2].Value);
                int seconds = int.Parse(match.Groups[3].Value);
                int milliseconds = int.Parse(match.Groups[4].Value) * 10;
                float bitrate = float.Parse(match.Groups[5].Value);
                this.fileDuration = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                return;
            }

            if (this.fileDuration.Ticks > 0)
            {
                match = this.progressRegex.Match(input);
                if (match.Success && match.Groups.Count >= 7)
                {
                    int size = int.Parse(match.Groups[1].Value);
                    int hours = int.Parse(match.Groups[2].Value);
                    int minutes = int.Parse(match.Groups[3].Value);
                    int seconds = int.Parse(match.Groups[4].Value);
                    int milliseconds = int.Parse(match.Groups[5].Value) * 10;
                    float bitrate = 0f;
                    float.TryParse(match.Groups[6].Value, out bitrate);

                    this.actualConvertedDuration = new TimeSpan(0, hours, minutes, seconds, milliseconds);

                    this.Progress = this.actualConvertedDuration.Ticks / (float)this.fileDuration.Ticks;
                    return;
                }
            }

            // Remove file names from log to avoid false negative when some words like 'Error' are in file name (github issue #247).
            string inputWithoutFileNames = input.Replace(this.InputFilePath, string.Empty).Replace(this.OutputFilePath, string.Empty);

            if (inputWithoutFileNames.Contains("Exiting.") || inputWithoutFileNames.Contains("Error") || inputWithoutFileNames.Contains("Unsupported dimensions") || inputWithoutFileNames.Contains("No such file or directory"))
            {
                if (inputWithoutFileNames.StartsWith("Error while decoding stream") && inputWithoutFileNames.Contains("Invalid data found when processing input"))
                {
                    // It is normal for certain containers (transport streams, damaged files)
                    // to have broken frames at the beginning. These are non-fatal (#452).
                    // https://trac.ffmpeg.org/ticket/1622
                }
                else
                {
                    this.ConversionFailed(input);
                }
            }
        }

        private struct FFMpegPass
        {
            public string Name;
            public string Arguments;
            public string FileToDelete;

            public FFMpegPass(string name, string arguments, string fileToDelete)
            {
                this.Name = name;
                this.Arguments = arguments;
                this.FileToDelete = fileToDelete;
            }

            public FFMpegPass(string arguments)
            {
                this.Name = "Conversion";
                this.Arguments = arguments;
                this.FileToDelete = string.Empty;
            }
        }
    }
}
