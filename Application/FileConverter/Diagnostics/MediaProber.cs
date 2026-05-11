// <copyright file="MediaProber.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>Result of probing a media file with the bundled ffmpeg.</summary>
    public class ProbeResult
    {
        public double DurationSec;
        public int Width;
        public int Height;
        public string VideoCodec = string.Empty;
        public string AudioCodec = string.Empty;
        public int BitrateKbps;
        public long SizeBytes;
    }

    /// <summary>
    /// Lightweight wrapper around the bundled <c>ffmpeg.exe</c> for extracting
    /// media metadata (duration, resolution, codec, bitrate) without any
    /// additional libraries.
    ///
    /// Used by:
    /// <list type="bullet">
    ///   <item><c>ConversionJob.StartConversion</c> → <see cref="ProgressWriter.WriteHeader"/> for the CLI <c>--progress</c> header.</item>
    ///   <item>The CLI <c>--probe</c> command.</item>
    ///   <item>The <c>file-converter</c> skill's PowerShell scripts for pre-conversion size estimation.</item>
    /// </list>
    /// </summary>
    public static class MediaProber
    {
        private static string FfmpegPath
        {
            get
            {
                string dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                return Path.Combine(dir, "ffmpeg.exe");
            }
        }

        /// <summary>Convenience overload that returns only the duration in seconds.</summary>
        public static double GetDuration(string inputFile)
            => Probe(inputFile).DurationSec;

        /// <summary>
        /// Probes <paramref name="inputFile"/> and returns a <see cref="ProbeResult"/>.
        /// All fields default to zero/empty when ffmpeg is unavailable or the file is not a media file.
        /// Never throws.
        /// </summary>
        public static ProbeResult Probe(string inputFile)
        {
            var r = new ProbeResult();
            if (!File.Exists(inputFile))
            {
                return r;
            }

            r.SizeBytes = new FileInfo(inputFile).Length;
            if (!File.Exists(FfmpegPath))
            {
                return r;
            }

            try
            {
                var psi = new ProcessStartInfo(FfmpegPath, $"-i \"{inputFile}\"")
                {
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };

                using (var proc = Process.Start(psi))
                {
                    // ffmpeg writes media info to stderr (even for a probe-only invocation).
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    // Duration: HH:MM:SS.mmm
                    var dm = Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):([\d.]+)");
                    if (dm.Success)
                    {
                        r.DurationSec = int.Parse(dm.Groups[1].Value) * 3600
                                      + int.Parse(dm.Groups[2].Value) * 60
                                      + double.Parse(dm.Groups[3].Value, CultureInfo.InvariantCulture);
                    }

                    // Overall bitrate: "bitrate: 21244 kb/s"
                    var bm = Regex.Match(stderr, @"bitrate:\s*(\d+)\s*kb/s");
                    if (bm.Success)
                    {
                        r.BitrateKbps = int.Parse(bm.Groups[1].Value);
                    }

                    // Video stream — codec and resolution: "Video: h264 ... 1920x1080"
                    var vm = Regex.Match(stderr, @"Video:\s*(\w+).*?(\d{3,5})x(\d{3,5})");
                    if (vm.Success)
                    {
                        r.VideoCodec = vm.Groups[1].Value;
                        r.Width  = int.Parse(vm.Groups[2].Value);
                        r.Height = int.Parse(vm.Groups[3].Value);
                    }

                    // Audio stream — codec: "Audio: aac"
                    var am = Regex.Match(stderr, @"Audio:\s*(\w+)");
                    if (am.Success)
                    {
                        r.AudioCodec = am.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // Best-effort only; never crash the caller.
            }

            return r;
        }
    }
}
