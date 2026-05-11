// <copyright file="ProgressWriter.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System;
    using System.IO;

    /// <summary>
    /// Writes machine-readable conversion progress to <see cref="Console.Out"/> when the
    /// <c>--progress</c> CLI flag is active.
    ///
    /// Output format mirrors ffmpeg's <c>-progress pipe:1</c> key=value protocol so that
    /// the file-converter skill's PowerShell parser can consume it without extra dependencies:
    /// <code>
    ///   file=input.mp4
    ///   preset=To Mp3
    ///   duration=6.030
    ///   out_time=00:00:01.000000
    ///   out_size=12345
    ///   speed=3.2x
    ///   progress=continue
    ///   ... (one block per progress update)
    ///   progress=end
    ///   exit_code=0
    ///   out_file=C:\...\input.mp3
    /// </code>
    /// </summary>
    public static class ProgressWriter
    {
        /// <summary>
        /// Gets or sets a value indicating whether progress output is active.
        /// Set to <c>true</c> by <c>Application.xaml.cs</c> when <c>--progress</c> is parsed.
        /// </summary>
        public static bool IsEnabled { get; set; }

        public static void WriteHeader(string inputFile, string preset, double durationSec)
        {
            if (!IsEnabled)
            {
                return;
            }

            Console.WriteLine($"file={Path.GetFileName(inputFile)}");
            Console.WriteLine($"preset={preset}");
            Console.WriteLine($"duration={durationSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            Console.Out.Flush();
        }

        public static void WriteProgress(double outTimeSec, string speed, long outSizeBytes)
        {
            if (!IsEnabled)
            {
                return;
            }

            var ts = TimeSpan.FromSeconds(outTimeSec);
            Console.WriteLine($"out_time={ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}000");
            Console.WriteLine($"out_size={outSizeBytes}");
            if (!string.IsNullOrEmpty(speed))
            {
                Console.WriteLine($"speed={speed}");
            }

            Console.WriteLine("progress=continue");
            Console.Out.Flush();
        }

        public static void WriteEnd(bool success, string outputFile)
        {
            if (!IsEnabled)
            {
                return;
            }

            Console.WriteLine("progress=end");
            Console.WriteLine($"exit_code={(success ? 0 : 1)}");
            if (!string.IsNullOrEmpty(outputFile))
            {
                Console.WriteLine($"out_file={outputFile}");
            }

            Console.Out.Flush();
        }
    }
}
