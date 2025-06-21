//============================================================================
// BDInfoCLI - Blu-ray Video and Audio Analysis Tool (CLI Version)
// Copyright © 2010 Cinema Squid
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//=============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Globalization;
using CommandLine;
using BDInfo;

namespace BDInfoCLI
{
    public class Options
    {
        [Option('p', "path", Required = true, HelpText = "Path to Blu-ray disc folder or ISO file")]
        public string Path { get; set; }

        [Option('o', "output", HelpText = "Output file path for the report (default: console output)")]
        public string OutputFile { get; set; }

        [Option('f', "format", Default = "text", HelpText = "Output format: text, xml, json")]
        public string Format { get; set; }

        [Option('v', "verbose", HelpText = "Enable verbose output")]
        public bool Verbose { get; set; }

        [Option('s', "scan", Default = true, HelpText = "Perform full scan of streams")]
        public bool Scan { get; set; }

        [Option('r', "report", Default = true, HelpText = "Generate detailed report")]
        public bool GenerateReport { get; set; }

        [Option('d', "diagnostics", Default = true, HelpText = "Generate stream diagnostics")]
        public bool GenerateDiagnostics { get; set; }

        [Option('e', "extended", Default = false, HelpText = "Generate extended stream diagnostics")]
        public bool ExtendedDiagnostics { get; set; }

        [Option('c', "chapters", Default = false, HelpText = "Display chapter count")]
        public bool DisplayChapters { get; set; }

        [Option('i', "ssif", Default = true, HelpText = "Enable SSIF (3D) support")]
        public bool EnableSSIF { get; set; }

        [Option('l', "filter-short", Default = 20, HelpText = "Filter short playlists (seconds)")]
        public int? FilterShortPlaylists { get; set; }

        [Option('k', "keep-order", Default = true, HelpText = "Keep stream order")]
        public bool KeepStreamOrder { get; set; }

        [Option('t', "text-summary", Default = true, HelpText = "Generate text summary")]
        public bool GenerateTextSummary { get; set; }

        [Option('u', "filter-looping", Default = true, HelpText = "Filter looping playlists")]
        public bool FilterLoopingPlaylists { get; set; }

        [Option('x', "image-prefix", Default = false, HelpText = "Use image prefix")]
        public bool UseImagePrefix { get; set; }

        [Option('y', "image-prefix-value", Default = "video-", HelpText = "Image prefix value")]
        public string ImagePrefixValue { get; set; }

        [Option('g', "frame-data", Default = false, HelpText = "Generate frame data file")]
        public bool GenerateFrameDataFile { get; set; }

        [Option('h', "hr-size-format", Default = true, HelpText = "Use human readable size format")]
        public bool UseHRSizeFormat { get; set; }

        [Option('a', "playlists", HelpText = "Comma-separated list of playlist names to analyze")]
        public string Playlists { get; set; }

        [Option('z', "autosave", Default = false, HelpText = "Auto-save report to file")]
        public bool AutosaveReport { get; set; }

        [Option('b', "bitrate-report", HelpText = "Include bitrate and chapter/frame statistics in the report")]
        public bool BitrateReport { get; set; }

        [Option('w', "detailed-bitrates", HelpText = "Output actual measured bitrates for each playlist, chapter, and stream")]
        public bool DetailedBitrates { get; set; }
    }

    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args)
                .MapResult(
                    (Options opts) => RunAnalysis(opts),
                    errs => 1);
        }

        static int RunAnalysis(Options options)
        {
            try
            {
                Console.WriteLine("BDInfo CLI - Blu-ray Analysis Tool");
                Console.WriteLine("==================================");
                Console.WriteLine();

                // Validate input path
                if (!File.Exists(options.Path) && !Directory.Exists(options.Path))
                {
                    Console.WriteLine($"Error: Path '{options.Path}' does not exist.");
                    return 1;
                }

                // Apply settings
                ApplySettings(options);

                // Initialize BDROM
                Console.WriteLine($"Loading BD-ROM from: {options.Path}");
                BDROM bdrom = null;
                try
                {
                    bdrom = new BDROM(options.Path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading BD-ROM: {ex.Message}");
                    return 1;
                }

                // Display basic disc information
                DisplayDiscInfo(bdrom);

                // Scan BDROM
                if (options.Scan)
                {
                    Console.WriteLine("\nScanning BD-ROM...");
                    try
                    {
                        // First call the basic BDROM scan to populate playlists and stream clips
                        bdrom.Scan();

                        // Now perform full scans of stream files to get actual file sizes and bitrates
                        Console.WriteLine("Performing full stream analysis...");

                        // Get all stream files that are referenced by playlists
                        List<TSStreamFile> streamFilesToScan = new List<TSStreamFile>();
                        Dictionary<string, List<TSPlaylistFile>> playlistMap = new Dictionary<string, List<TSPlaylistFile>>();

                        // Build playlist map for each stream file
                        foreach (TSStreamFile streamFile in bdrom.StreamFiles.Values)
                        {
                            if (!playlistMap.ContainsKey(streamFile.Name))
                            {
                                playlistMap[streamFile.Name] = new List<TSPlaylistFile>();
                            }

                            foreach (TSPlaylistFile playlist in bdrom.PlaylistFiles.Values)
                            {
                                playlist.ClearBitrates();

                                foreach (TSStreamClip clip in playlist.StreamClips)
                                {
                                    if (clip.Name == streamFile.Name)
                                    {
                                        if (!playlistMap[streamFile.Name].Contains(playlist))
                                        {
                                            playlistMap[streamFile.Name].Add(playlist);
                                        }
                                        if (!streamFilesToScan.Contains(streamFile))
                                        {
                                            streamFilesToScan.Add(streamFile);
                                        }
                                    }
                                }
                            }
                        }

                        // Perform full scan of each stream file
                        int totalFiles = streamFilesToScan.Count;
                        int currentFile = 0;

                        foreach (TSStreamFile streamFile in streamFilesToScan)
                        {
                            currentFile++;
                            Console.WriteLine($"Scanning {streamFile.DisplayName} ({currentFile}/{totalFiles})...");

                            List<TSPlaylistFile> playlists = playlistMap[streamFile.Name];
                            streamFile.Scan(playlists, true); // Full scan with isFullScan = true
                        }

                        Console.WriteLine("Full scan completed successfully.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during scan: {ex.Message}");
                        if (!options.Verbose)
                            return 1;
                    }
                }

                // Generate report
                if (options.GenerateReport)
                {
                    Console.WriteLine("\nGenerating report...");
                    string report = GenerateReport(bdrom, options);

                    if (!string.IsNullOrEmpty(options.OutputFile))
                    {
                        File.WriteAllText(options.OutputFile, report);
                        Console.WriteLine($"Report saved to: {options.OutputFile}");
                    }
                    else
                    {
                        Console.WriteLine(report);
                    }
                }

                // Cleanup
                if (bdrom != null && bdrom.IsImage)
                {
                    bdrom.CloseDiscImage();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                if (options.Verbose)
                {
                    Console.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }

        static void ApplySettings(Options options)
        {
            // Apply CLI options to BDInfo settings to match GUI defaults exactly
            BDInfoSettings.GenerateStreamDiagnostics = options.GenerateDiagnostics;
            BDInfoSettings.ExtendedStreamDiagnostics = options.ExtendedDiagnostics;
            BDInfoSettings.DisplayChapterCount = options.DisplayChapters;
            BDInfoSettings.EnableSSIF = options.EnableSSIF;
            BDInfoSettings.KeepStreamOrder = options.KeepStreamOrder;
            BDInfoSettings.GenerateTextSummary = options.GenerateTextSummary;
            BDInfoSettings.FilterLoopingPlaylists = options.FilterLoopingPlaylists;
            BDInfoSettings.UseImagePrefix = options.UseImagePrefix;
            BDInfoSettings.GenerateFrameDataFile = options.GenerateFrameDataFile;
            BDInfoSettings.AutosaveReport = options.AutosaveReport;
            BDInfoSettings.MainFormHRSizeFormat = options.UseHRSizeFormat;

            // Handle filter short playlists
            if (options.FilterShortPlaylists.HasValue)
            {
                BDInfoSettings.FilterShortPlaylists = true;
                BDInfoSettings.FilterShortPlaylistsValue = options.FilterShortPlaylists.Value;
            }
            else
            {
                BDInfoSettings.FilterShortPlaylists = true; // Default from GUI
                BDInfoSettings.FilterShortPlaylistsValue = 20; // Default from GUI
            }

            if (!string.IsNullOrEmpty(options.ImagePrefixValue))
            {
                BDInfoSettings.UseImagePrefixValue = options.ImagePrefixValue;
            }
            else
            {
                BDInfoSettings.UseImagePrefixValue = "video-"; // Default from GUI
            }
        }

        static void DisplayDiscInfo(BDROM bdrom)
        {
            Console.WriteLine($"Disc Title: {bdrom.DiscTitle ?? "Unknown"}");
            Console.WriteLine($"Volume Label: {bdrom.VolumeLabel ?? "Unknown"}");
            Console.WriteLine($"Size: {FormatSize(bdrom.Size)}");
            Console.WriteLine($"Is Image: {bdrom.IsImage}");
            Console.WriteLine($"Is 3D: {bdrom.Is3D}");
            Console.WriteLine($"Is UHD: {bdrom.IsUHD}");
            Console.WriteLine($"Is BD+ Protected: {bdrom.IsBDPlus}");
            Console.WriteLine($"Has BD-Java: {bdrom.IsBDJava}");
            Console.WriteLine($"Has PSP: {bdrom.IsPSP}");
            Console.WriteLine($"Is 50Hz: {bdrom.Is50Hz}");
        }

        static string GenerateReport(BDROM bdrom, Options options)
        {
            switch (options.Format.ToLower())
            {
                case "xml":
                    return GenerateXmlReport(bdrom, options);
                case "json":
                    return GenerateJsonReport(bdrom, options);
                default:
                    return GenerateTextReport(bdrom, options);
            }
        }

        static string GenerateTextReport(BDROM bdrom, Options options)
        {
            var report = new StringBuilder();
            string protection = (bdrom.IsBDPlus ? "BD+" : bdrom.IsUHD ? "AACS2" : "AACS");

            // Disc Information - match GUI format exactly
            if (!string.IsNullOrEmpty(bdrom.DiscTitle))
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Title:", bdrom.DiscTitle));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Label:", bdrom.VolumeLabel));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1:N0} bytes", "Disc Size:", bdrom.Size));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Protection:", protection));

            List<string> extraFeatures = new List<string>();
            if (bdrom.IsUHD)
            {
                extraFeatures.Add("Ultra HD");
            }
            if (bdrom.IsBDJava)
            {
                extraFeatures.Add("BD-Java");
            }
            if (bdrom.Is50Hz)
            {
                extraFeatures.Add("50Hz Content");
            }
            if (bdrom.Is3D)
            {
                extraFeatures.Add("Blu-ray 3D");
            }
            if (bdrom.IsDBOX)
            {
                extraFeatures.Add("D-BOX Motion Code");
            }
            if (bdrom.IsPSP)
            {
                extraFeatures.Add("PSP Digital Copy");
            }
            if (extraFeatures.Count > 0)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Extras:", string.Join(", ", extraFeatures.ToArray())));
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "BDInfo:", "CLI Version"));
            report.AppendLine();

            // Notes section - match GUI format
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Notes:", ""));
            report.AppendLine();
            report.AppendLine("BDINFO HOME:");
            report.AppendLine("  Cinema Squid (old)");
            report.AppendLine("    http://www.cinemasquid.com/blu-ray/tools/bdinfo");
            report.AppendLine("  UniqProject GitHub (new)");
            report.AppendLine("   https://github.com/UniqProject/BDInfo");
            report.AppendLine();

            // Get playlists to analyze
            var playlistsToAnalyze = GetPlaylistsToAnalyze(bdrom, options);

            // Generate report for each playlist - match GUI format exactly
            foreach (var playlist in playlistsToAnalyze.OrderBy(p => p.Name))
            {
                TimeSpan playlistTotalLength = new TimeSpan((long)(playlist.TotalLength * 10000000));
                string totalLength = string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                    playlistTotalLength.Hours, playlistTotalLength.Minutes, playlistTotalLength.Seconds, playlistTotalLength.Milliseconds);
                string totalLengthShort = string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}",
                    playlistTotalLength.Hours, playlistTotalLength.Minutes, playlistTotalLength.Seconds);
                string totalSize = string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", playlist.TotalSize);
                string discSize = string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", bdrom.Size);
                double totalBitrate = Math.Round((double)playlist.TotalBitRate / 1000000, 2);
                double videoBitrate = 0;

                // Get video bitrate
                if (playlist.VideoStreams.Count > 0)
                {
                    var videoStream = playlist.VideoStreams.First();
                    videoBitrate = Math.Round((double)videoStream.BitRate / 1000000, 2);
                }

                string videoCodec = "";
                if (playlist.VideoStreams.Count > 0)
                {
                    videoCodec = playlist.VideoStreams.First().CodecName;
                }

                string audio1 = "";
                string audio2 = "";
                if (playlist.AudioStreams.Count > 0)
                {
                    audio1 = string.Format(CultureInfo.InvariantCulture, "{0} / {1} / {2}",
                        playlist.AudioStreams[0].LanguageName,
                        playlist.AudioStreams[0].CodecName,
                        playlist.AudioStreams[0].Description);
                }
                if (playlist.AudioStreams.Count > 1)
                {
                    audio2 = string.Format(CultureInfo.InvariantCulture, "{0} / {1} / {2}",
                        playlist.AudioStreams[1].LanguageName,
                        playlist.AudioStreams[1].CodecName,
                        playlist.AudioStreams[1].Description);
                }

                // Forums paste section - match GUI format exactly
                report.AppendLine("********************");
                report.AppendLine("PLAYLIST: " + playlist.Name);
                report.AppendLine("********************");
                report.AppendLine();
                report.AppendLine("<--- BEGIN FORUMS PASTE --->");
                report.AppendLine("[code]");

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-64}{1,-8}{2,-8}{3,-16}{4,-18}{5,-13}{6,-13}{7,-42}{8}",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "Total",
                    "Video",
                    "",
                    ""));

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-64}{1,-8}{2,-8}{3,-16}{4,-18}{5,-13}{6,-13}{7,-42}{8}",
                    "Title",
                    "Codec",
                    "Length",
                    "Movie Size",
                    "Disc Size",
                    "Bitrate",
                    "Bitrate",
                    "Main Audio Track",
                    "Secondary Audio Track"));

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-64}{1,-8}{2,-8}{3,-16}{4,-18}{5,-13}{6,-13}{7,-42}{8}",
                    "-----",
                    "------",
                    "-------",
                    "--------------",
                    "----------------",
                    "-----------",
                    "-----------",
                    "------------------",
                    "---------------------"));

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-64}{1,-8}{2,-8}{3,-16}{4,-18}{5,-13}{6,-13}{7,-42}{8}",
                    playlist.Name,
                    videoCodec,
                    totalLengthShort,
                    totalSize,
                    discSize,
                    totalBitrate + " Mbps",
                    videoBitrate + " Mbps",
                    audio1,
                    audio2));

                report.AppendLine("[/code]");
                report.AppendLine();
                report.AppendLine("[code]");
                report.AppendLine();

                // DISC INFO section
                report.AppendLine("DISC INFO:");
                report.AppendLine();
                if (!string.IsNullOrEmpty(bdrom.DiscTitle))
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Title:", bdrom.DiscTitle));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Label:", bdrom.VolumeLabel));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1:N0} bytes", "Disc Size:", bdrom.Size));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Protection:", protection));
                if (extraFeatures.Count > 0)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Extras:", string.Join(", ", extraFeatures.ToArray())));
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "BDInfo:", "CLI Version"));
                report.AppendLine();

                // PLAYLIST REPORT section
                report.AppendLine("PLAYLIST REPORT:");
                report.AppendLine();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1}", "Name:", playlist.Name));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1} (h:m:s.ms)", "Length:", totalLength));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1:N0} bytes", "Size:", playlist.TotalSize));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1} Mbps", "Total Bitrate:", totalBitrate));

                if (playlist.HasHiddenTracks)
                {
                    report.AppendLine();
                    report.AppendLine("(*) Indicates included stream hidden by this playlist.");
                }

                // VIDEO section
                if (playlist.VideoStreams.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("VIDEO:");
                    report.AppendLine();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1,-20}{2,-16}", "Codec", "Bitrate", "Description"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1,-20}{2,-16}", "---------------", "-------------", "-----------"));

                    foreach (TSStream stream in playlist.SortedStreams)
                    {
                        if (!stream.IsVideoStream) continue;

                        string streamName = stream.CodecName;
                        if (stream.AngleIndex > 0)
                        {
                            streamName += string.Format(CultureInfo.InvariantCulture, " ({0})", stream.AngleIndex);
                        }

                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0:N0}", (int)Math.Round((double)stream.BitRate / 1000));
                        if (stream.AngleIndex > 0)
                        {
                            streamBitrate += string.Format(CultureInfo.InvariantCulture, " ({0:D})", (int)Math.Round((double)stream.ActiveBitRate / 1000));
                        }
                        streamBitrate += " kbps";

                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1,-20}{2,-16}",
                            (stream.IsHidden ? "* " : "") + streamName, streamBitrate, stream.Description));
                    }
                }

                // AUDIO section
                if (playlist.AudioStreams.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("AUDIO:");
                    report.AppendLine();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "Codec", "Language", "Bitrate", "Description"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "---------------", "-------------", "-------------", "-----------"));

                    foreach (TSStream stream in playlist.SortedStreams)
                    {
                        if (!stream.IsAudioStream) continue;

                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0,5:D} kbps", (int)Math.Round((double)stream.BitRate / 1000));

                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}",
                            (stream.IsHidden ? "* " : "") + stream.CodecName, stream.LanguageName, streamBitrate, stream.Description));
                    }
                }

                // SUBTITLES section
                if (playlist.GraphicsStreams.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("SUBTITLES:");
                    report.AppendLine();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "Codec", "Language", "Bitrate", "Description"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "---------------", "-------------", "-------------", "-----------"));

                    foreach (TSStream stream in playlist.SortedStreams)
                    {
                        if (!stream.IsGraphicsStream) continue;

                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0,5:F2} kbps", (double)stream.BitRate / 1000);

                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}",
                            (stream.IsHidden ? "* " : "") + stream.CodecName, stream.LanguageName, streamBitrate, stream.Description));
                    }
                }

                // TEXT section
                if (playlist.TextStreams.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("TEXT:");
                    report.AppendLine();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "Codec", "Language", "Bitrate", "Description"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}", "---------------", "-------------", "-------------", "-----------"));

                    foreach (TSStream stream in playlist.SortedStreams)
                    {
                        if (!stream.IsTextStream) continue;

                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0,5:F2} kbps", (double)stream.BitRate / 1000);

                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32}{1,-16}{2,-16}{3,-16}",
                            (stream.IsHidden ? "* " : "") + stream.CodecName, stream.LanguageName, streamBitrate, stream.Description));
                    }
                }

                // FILES section
                report.AppendLine();
                report.AppendLine("FILES:");
                report.AppendLine();
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}", "Name", "Time In", "Length", "Size", "Total Bitrate"));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}", "---------------", "-------------", "-------------", "-------------", "-------------"));

                foreach (var clip in playlist.StreamClips)
                {
                    string clipName = clip.DisplayName;
                    if (clip.AngleIndex > 0)
                    {
                        clipName += string.Format(CultureInfo.InvariantCulture, " ({0})", clip.AngleIndex);
                    }

                    TimeSpan clipInSpan = new TimeSpan((long)(clip.RelativeTimeIn * 10000000));
                    TimeSpan clipLengthSpan = new TimeSpan((long)(clip.Length * 10000000));

                    string clipTimeIn = string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                        clipInSpan.Hours, clipInSpan.Minutes, clipInSpan.Seconds, clipInSpan.Milliseconds);
                    string clipLength = string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                        clipLengthSpan.Hours, clipLengthSpan.Minutes, clipLengthSpan.Seconds, clipLengthSpan.Milliseconds);
                    string clipSize = string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", clip.PacketSize);
                    string clipBitrate = string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round((double)clip.PacketBitRate / 1000));

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}",
                        clipName, clipTimeIn, clipLength, clipSize, clipBitrate));
                }

                // Bitrate/Chapter/Frame statistics (if requested)
                if (options.BitrateReport)
                {
                    report.AppendLine();
                    report.AppendLine(GenerateBitrateReport(playlist, bdrom));
                }

                // Detailed bitrates (if requested)
                if (options.DetailedBitrates)
                {
                    report.AppendLine();
                    report.AppendLine(GenerateDetailedBitrates(playlist));
                }

                // Stream diagnostics (if enabled)
                if (options.GenerateDiagnostics)
                {
                    report.AppendLine();
                    report.AppendLine("STREAM DIAGNOSTICS:");
                    report.AppendLine();
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-24}{5,-24}{6,-24}{7,-16}{8,-16}",
                        "File", "PID", "Type", "Codec", "Language", "Seconds", "Bitrate", "Bytes", "Packets"));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-24}{5,-24}{6,-24}{7,-16}{8,-16}",
                        "----------", "-------------", "-----", "----------", "-------------", "--------------", "---------------", "--------------", "-----------"));

                    foreach (var clip in playlist.StreamClips)
                    {
                        string clipName = clip.DisplayName;
                        if (clip.StreamClipFile != null && clip.StreamClipFile.Streams != null)
                        {
                            foreach (var clipStream in clip.StreamClipFile.Streams.Values)
                            {
                                string language = clipStream.LanguageName ?? "";
                                string clipSeconds = string.Format(CultureInfo.InvariantCulture, "{0,6:F1}", clip.Length);
                                string clipBitRate = string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round((double)clipStream.BitRate / 1000));

                                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1,-16}{2,-16}{3,-16}{4,-24}{5,-24}{6,-24}{7,-16}{8,-16}",
                                    clipName,
                                    string.Format(CultureInfo.InvariantCulture, "{0} (0x{1:X})", clipStream.PID, clipStream.PID),
                                    string.Format(CultureInfo.InvariantCulture, "0x{0:X2}", (byte)clipStream.StreamType),
                                    clipStream.CodecShortName,
                                    language,
                                    clipSeconds,
                                    clipBitRate,
                                    string.Format(CultureInfo.InvariantCulture, "{0,14:N0}", clipStream.PayloadBytes),
                                    string.Format(CultureInfo.InvariantCulture, "{0,11:N0}", clipStream.PacketCount)));
                            }
                        }
                    }
                }

                report.AppendLine();
                report.AppendLine("[/code]");
                report.AppendLine("<---- END FORUMS PASTE ---->");
                report.AppendLine();

                // Quick Summary (if enabled)
                if (options.GenerateTextSummary)
                {
                    report.AppendLine("QUICK SUMMARY:");
                    report.AppendLine();
                    if (!string.IsNullOrEmpty(bdrom.DiscTitle))
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Title:", bdrom.DiscTitle));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Disc Label:", bdrom.VolumeLabel));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1:N0} bytes", "Disc Size:", bdrom.Size));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Protection:", protection));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Playlist:", playlist.Name));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1:N0} bytes", "Size:", playlist.TotalSize));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1}", "Length:", totalLength));
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1} Mbps", "Total Bitrate:", totalBitrate));

                    if (playlist.VideoStreams.Count > 0)
                    {
                        var videoStream = playlist.VideoStreams.First();
                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0:N0}", (int)Math.Round((double)videoStream.BitRate / 1000)) + " kbps";
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1} / {2} / {3}",
                            (videoStream.IsHidden ? "* " : "") + "Video:", videoStream.CodecName, streamBitrate, videoStream.Description));
                    }

                    foreach (var audioStream in playlist.AudioStreams)
                    {
                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0,5:D} kbps", (int)Math.Round((double)audioStream.BitRate / 1000));
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1} / {2} / {3}",
                            (audioStream.IsHidden ? "* " : "") + "Audio:", audioStream.LanguageName, audioStream.CodecName, audioStream.Description));
                    }

                    foreach (var subtitleStream in playlist.GraphicsStreams)
                    {
                        string streamBitrate = string.Format(CultureInfo.InvariantCulture, "{0,5:F2} kbps", (double)subtitleStream.BitRate / 1000);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16}{1} / {2}",
                            (subtitleStream.IsHidden ? "* " : "") + "Subtitle:", subtitleStream.LanguageName, streamBitrate.Trim()));
                    }
                    report.AppendLine();
                }
            }

            return report.ToString();
        }

        static string GenerateBitrateReport(TSPlaylistFile playlist, BDROM bdrom)
        {
            var report = new StringBuilder();

            if (playlist.VideoStreams.Count == 0)
            {
                report.AppendLine("No video streams found for bitrate analysis.");
                return report.ToString();
            }

            report.AppendLine("BITRATE/CHAPTER/FRAME STATISTICS:");
            report.AppendLine("=================================");
            report.AppendLine();

            // Files table
            report.AppendLine("FILES:");
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}",
                "Name", "Time In", "Length", "Size", "Total Bitrate"));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}",
                "---------------", "-------------", "-------------", "-------------", "-------------"));

            foreach (TSStreamClip clip in playlist.StreamClips)
            {
                string clipName = clip.DisplayName;

                if (clip.AngleIndex > 0)
                {
                    clipName += string.Format(CultureInfo.InvariantCulture, " ({0})", clip.AngleIndex);
                }

                string clipSize = string.Format(CultureInfo.InvariantCulture, "{0:N0}", clip.PacketSize);

                TimeSpan clipInSpan = new TimeSpan((long)(clip.RelativeTimeIn * 10000000));
                TimeSpan clipLengthSpan = new TimeSpan((long)(clip.Length * 10000000));

                string clipTimeIn = string.Format(CultureInfo.InvariantCulture,
                    "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                    clipInSpan.Hours, clipInSpan.Minutes, clipInSpan.Seconds, clipInSpan.Milliseconds);
                string clipLength = string.Format(CultureInfo.InvariantCulture,
                    "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                    clipLengthSpan.Hours, clipLengthSpan.Minutes, clipLengthSpan.Seconds, clipLengthSpan.Milliseconds);

                string clipBitrate = string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round((double)clip.PacketBitRate / 1000));

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}",
                    clipName, clipTimeIn, clipLength, clipSize, clipBitrate));
            }

            report.AppendLine();
            report.AppendLine("CHAPTERS:");
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}{5,-16}{6,-16}{7,-16}{8,-16}{9,-16}{10,-16}{11,-16}{12,-16}",
                "Number", "Time In", "Length", "Avg Video Rate", "Max 1-Sec Rate", "Max 1-Sec Time",
                "Max 5-Sec Rate", "Max 5-Sec Time", "Max 10Sec Rate", "Max 10Sec Time",
                "Avg Frame Size", "Max Frame Size", "Max Frame Time"));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}{5,-16}{6,-16}{7,-16}{8,-16}{9,-16}{10,-16}{11,-16}{12,-16}",
                "------", "-------------", "-------------", "--------------", "--------------", "--------------",
                "--------------", "--------------", "--------------", "--------------", "--------------", "--------------", "--------------"));

            Queue<double> window1Bits = new Queue<double>();
            Queue<double> window1Seconds = new Queue<double>();
            double window1BitsSum = 0;
            double window1SecondsSum = 0;
            double window1PeakBitrate = 0;
            double window1PeakLocation = 0;

            Queue<double> window5Bits = new Queue<double>();
            Queue<double> window5Seconds = new Queue<double>();
            double window5BitsSum = 0;
            double window5SecondsSum = 0;
            double window5PeakBitrate = 0;
            double window5PeakLocation = 0;

            Queue<double> window10Bits = new Queue<double>();
            Queue<double> window10Seconds = new Queue<double>();
            double window10BitsSum = 0;
            double window10SecondsSum = 0;
            double window10PeakBitrate = 0;
            double window10PeakLocation = 0;

            double chapterPosition = 0;
            double chapterBits = 0;
            long chapterFrameCount = 0;
            double chapterSeconds = 0;
            double chapterMaxFrameSize = 0;
            double chapterMaxFrameLocation = 0;

            ushort diagPID = playlist.VideoStreams[0].PID;

            int chapterIndex = 0;
            int clipIndex = 0;
            int diagIndex = 0;

            while (chapterIndex < playlist.Chapters.Count)
            {
                TSStreamClip clip = null;
                TSStreamFile file = null;

                if (clipIndex < playlist.StreamClips.Count)
                {
                    clip = playlist.StreamClips[clipIndex];
                    file = clip.StreamFile;
                }

                double chapterStart = playlist.Chapters[chapterIndex];
                double chapterEnd;
                if (chapterIndex < playlist.Chapters.Count - 1)
                {
                    chapterEnd = playlist.Chapters[chapterIndex + 1];
                }
                else
                {
                    chapterEnd = playlist.TotalLength;
                }
                double chapterLength = chapterEnd - chapterStart;

                List<TSStreamDiagnostics> diagList = null;

                if (clip != null && clip.AngleIndex == 0 && file != null && file.StreamDiagnostics.ContainsKey(diagPID))
                {
                    diagList = file.StreamDiagnostics[diagPID];

                    while (diagIndex < diagList.Count && chapterPosition < chapterEnd)
                    {
                        TSStreamDiagnostics diag = diagList[diagIndex++];

                        if (diag.Marker < clip.TimeIn) continue;

                        chapterPosition = diag.Marker - clip.TimeIn + clip.RelativeTimeIn;

                        double seconds = diag.Interval;
                        double bits = diag.Bytes * 8.0;

                        chapterBits += bits;
                        chapterSeconds += seconds;

                        if (diag.Tag != null)
                        {
                            chapterFrameCount++;
                        }

                        window1SecondsSum += seconds;
                        window1Seconds.Enqueue(seconds);
                        window1BitsSum += bits;
                        window1Bits.Enqueue(bits);

                        window5SecondsSum += diag.Interval;
                        window5Seconds.Enqueue(diag.Interval);
                        window5BitsSum += bits;
                        window5Bits.Enqueue(bits);

                        window10SecondsSum += seconds;
                        window10Seconds.Enqueue(seconds);
                        window10BitsSum += bits;
                        window10Bits.Enqueue(bits);

                        if (bits > chapterMaxFrameSize * 8)
                        {
                            chapterMaxFrameSize = bits / 8;
                            chapterMaxFrameLocation = chapterPosition;
                        }
                        if (window1SecondsSum > 1.0)
                        {
                            double bitrate = window1BitsSum / window1SecondsSum;
                            if (bitrate > window1PeakBitrate && chapterPosition - window1SecondsSum > 0)
                            {
                                window1PeakBitrate = bitrate;
                                window1PeakLocation = chapterPosition - window1SecondsSum;
                            }
                            window1BitsSum -= window1Bits.Dequeue();
                            window1SecondsSum -= window1Seconds.Dequeue();
                        }
                        if (window5SecondsSum > 5.0)
                        {
                            double bitrate = window5BitsSum / window5SecondsSum;
                            if (bitrate > window5PeakBitrate && chapterPosition - window5SecondsSum > 0)
                            {
                                window5PeakBitrate = bitrate;
                                window5PeakLocation = chapterPosition - window5SecondsSum;
                                if (window5PeakLocation < 0)
                                {
                                    window5PeakLocation = 0;
                                }
                            }
                            window5BitsSum -= window5Bits.Dequeue();
                            window5SecondsSum -= window5Seconds.Dequeue();
                        }
                        if (window10SecondsSum > 10.0)
                        {
                            double bitrate = window10BitsSum / window10SecondsSum;
                            if (bitrate > window10PeakBitrate && chapterPosition - window10SecondsSum > 0)
                            {
                                window10PeakBitrate = bitrate;
                                window10PeakLocation = chapterPosition - window10SecondsSum;
                            }
                            window10BitsSum -= window10Bits.Dequeue();
                            window10SecondsSum -= window10Seconds.Dequeue();
                        }
                    }
                }
                if (diagList == null || diagIndex == diagList.Count)
                {
                    if (clipIndex < playlist.StreamClips.Count)
                    {
                        clipIndex++; diagIndex = 0;
                    }
                    else
                    {
                        chapterPosition = chapterEnd;
                    }
                }
                if (chapterPosition >= chapterEnd)
                {
                    ++chapterIndex;

                    TimeSpan window1PeakSpan = new TimeSpan((long)(window1PeakLocation * 10000000));
                    TimeSpan window5PeakSpan = new TimeSpan((long)(window5PeakLocation * 10000000));
                    TimeSpan window10PeakSpan = new TimeSpan((long)(window10PeakLocation * 10000000));
                    TimeSpan chapterMaxFrameSpan = new TimeSpan((long)(chapterMaxFrameLocation * 10000000));
                    TimeSpan chapterStartSpan = new TimeSpan((long)(chapterStart * 10000000));
                    TimeSpan chapterLengthSpan = new TimeSpan((long)(chapterLength * 10000000));

                    double chapterBitrate = 0;
                    if (chapterLength > 0)
                    {
                        chapterBitrate = chapterBits / chapterLength;
                    }
                    double chapterAvgFrameSize = 0;
                    if (chapterFrameCount > 0)
                    {
                        chapterAvgFrameSize = chapterBits / chapterFrameCount / 8;
                    }

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16}{1,-16}{2,-16}{3,-16}{4,-16}{5,-16}{6,-16}{7,-16}{8,-16}{9,-16}{10,-16}{11,-16}{12,-16}",
                        chapterIndex,
                        string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}.{3:D3}", chapterStartSpan.Hours, chapterStartSpan.Minutes, chapterStartSpan.Seconds, chapterStartSpan.Milliseconds),
                        string.Format(CultureInfo.InvariantCulture, "{0:D1}:{1:D2}:{2:D2}.{3:D3}", chapterLengthSpan.Hours, chapterLengthSpan.Minutes, chapterLengthSpan.Seconds, chapterLengthSpan.Milliseconds),
                        string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round(chapterBitrate / 1000)),
                        string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round(window1PeakBitrate / 1000)),
                        string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}.{3:D3}", window1PeakSpan.Hours, window1PeakSpan.Minutes, window1PeakSpan.Seconds, window1PeakSpan.Milliseconds),
                        string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round(window5PeakBitrate / 1000)),
                        string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}.{3:D3}", window5PeakSpan.Hours, window5PeakSpan.Minutes, window5PeakSpan.Seconds, window5PeakSpan.Milliseconds),
                        string.Format(CultureInfo.InvariantCulture, "{0,6:N0} kbps", Math.Round(window10PeakBitrate / 1000)),
                        string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}.{3:D3}", window10PeakSpan.Hours, window10PeakSpan.Minutes, window10PeakSpan.Seconds, window10PeakSpan.Milliseconds),
                        string.Format(CultureInfo.InvariantCulture, "{0,7:N0} bytes", chapterAvgFrameSize),
                        string.Format(CultureInfo.InvariantCulture, "{0,7:N0} bytes", chapterMaxFrameSize),
                        string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}.{3:D3}", chapterMaxFrameSpan.Hours, chapterMaxFrameSpan.Minutes, chapterMaxFrameSpan.Seconds, chapterMaxFrameSpan.Milliseconds)));

                    // Reset for next chapter
                    window1Bits = new Queue<double>();
                    window1Seconds = new Queue<double>();
                    window1BitsSum = 0;
                    window1SecondsSum = 0;
                    window1PeakBitrate = 0;
                    window1PeakLocation = 0;

                    window5Bits = new Queue<double>();
                    window5Seconds = new Queue<double>();
                    window5BitsSum = 0;
                    window5SecondsSum = 0;
                    window5PeakBitrate = 0;
                    window5PeakLocation = 0;

                    window10Bits = new Queue<double>();
                    window10Seconds = new Queue<double>();
                    window10BitsSum = 0;
                    window10SecondsSum = 0;
                    window10PeakBitrate = 0;
                    window10PeakLocation = 0;

                    chapterBits = 0;
                    chapterSeconds = 0;
                    chapterFrameCount = 0;
                    chapterMaxFrameSize = 0;
                    chapterMaxFrameLocation = 0;
                }
            }

            return report.ToString();
        }

        static string GenerateDetailedBitrates(TSPlaylistFile playlist)
        {
            var report = new StringBuilder();
            report.AppendLine("DETAILED BITRATES:");
            report.AppendLine("==================");
            report.AppendLine();

            // Per-stream bitrates
            report.AppendLine("STREAM BITRATES:");
            report.AppendLine("----------------");
            foreach (var stream in playlist.SortedStreams)
            {
                double bitrateKbps = stream.BitRate / 1000.0;
                double activeBitrateKbps = stream.ActiveBitRate / 1000.0;

                string streamType = stream.IsVideoStream ? "Video" :
                                   stream.IsAudioStream ? "Audio" :
                                   stream.IsGraphicsStream ? "Graphics" :
                                   stream.IsTextStream ? "Text" : "Unknown";

                report.AppendLine($"Stream PID: {stream.PID}, Type: {streamType}, Codec: {stream.CodecName}");
                report.AppendLine($"  Average Bitrate: {bitrateKbps:F2} kbps");
                if (stream.ActiveBitRate > 0 && stream.ActiveBitRate != stream.BitRate)
                {
                    report.AppendLine($"  Active Bitrate: {activeBitrateKbps:F2} kbps");
                }
                if (!string.IsNullOrEmpty(stream.LanguageName))
                {
                    report.AppendLine($"  Language: {stream.LanguageName}");
                }
                report.AppendLine($"  Description: {stream.Description}");
                report.AppendLine();
            }

            // Per-chapter bitrates (if chapters exist)
            if (playlist.Chapters.Count > 0)
            {
                report.AppendLine("CHAPTER BITRATES:");
                report.AppendLine("-----------------");
                for (int i = 0; i < playlist.Chapters.Count; i++)
                {
                    double chapterStart = playlist.Chapters[i];
                    double chapterEnd = (i < playlist.Chapters.Count - 1) ? playlist.Chapters[i + 1] : playlist.TotalLength;
                    double chapterLength = chapterEnd - chapterStart;

                    if (chapterLength > 0)
                    {
                        TimeSpan chapterStartSpan = new TimeSpan((long)(chapterStart * 10000000));
                        TimeSpan chapterLengthSpan = new TimeSpan((long)(chapterLength * 10000000));

                        string chapterStartTime = string.Format(CultureInfo.InvariantCulture,
                            "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                            chapterStartSpan.Hours, chapterStartSpan.Minutes, chapterStartSpan.Seconds, chapterStartSpan.Milliseconds);
                        string chapterLengthTime = string.Format(CultureInfo.InvariantCulture,
                            "{0:D1}:{1:D2}:{2:D2}.{3:D3}",
                            chapterLengthSpan.Hours, chapterLengthSpan.Minutes, chapterLengthSpan.Seconds, chapterLengthSpan.Milliseconds);

                        report.AppendLine($"Chapter {i + 1}: Start {chapterStartTime}, Length {chapterLengthTime}");

                        // Calculate chapter bitrate if we have video streams
                        if (playlist.VideoStreams.Count > 0)
                        {
                            // This is a simplified calculation - in practice, you'd need to sum the actual bits
                            // from the stream diagnostics for this specific time range
                            double estimatedBitrate = playlist.TotalBitRate / 1000.0; // kbps
                            report.AppendLine($"  Estimated Average Bitrate: {estimatedBitrate:F2} kbps");
                        }
                        report.AppendLine();
                    }
                }
            }

            // Overall playlist statistics
            report.AppendLine("PLAYLIST STATISTICS:");
            report.AppendLine("-------------------");
            report.AppendLine($"Total Length: {playlist.TotalLength:F2} seconds");
            report.AppendLine($"Total Size: {playlist.TotalSize:N0} bytes");
            report.AppendLine($"Total Bitrate: {playlist.TotalBitRate / 1000.0:F2} kbps");
            report.AppendLine($"Video Streams: {playlist.VideoStreams.Count}");
            report.AppendLine($"Audio Streams: {playlist.AudioStreams.Count}");
            report.AppendLine($"Graphics Streams: {playlist.GraphicsStreams.Count}");
            report.AppendLine($"Text Streams: {playlist.TextStreams.Count}");
            report.AppendLine($"Stream Clips: {playlist.StreamClips.Count}");
            report.AppendLine($"Chapters: {playlist.Chapters.Count}");

            return report.ToString();
        }

        static string GenerateXmlReport(BDROM bdrom, Options options)
        {
            var xmlDoc = new XmlDocument();
            var root = xmlDoc.CreateElement("BDInfo");
            xmlDoc.AppendChild(root);

            // Disc Information
            var discInfo = xmlDoc.CreateElement("DiscInfo");
            root.AppendChild(discInfo);

            AddXmlElement(xmlDoc, discInfo, "DiscTitle", bdrom.DiscTitle ?? "Unknown");
            AddXmlElement(xmlDoc, discInfo, "VolumeLabel", bdrom.VolumeLabel ?? "Unknown");
            AddXmlElement(xmlDoc, discInfo, "Size", bdrom.Size.ToString());
            AddXmlElement(xmlDoc, discInfo, "IsImage", bdrom.IsImage.ToString());
            AddXmlElement(xmlDoc, discInfo, "Is3D", bdrom.Is3D.ToString());
            AddXmlElement(xmlDoc, discInfo, "IsUHD", bdrom.IsUHD.ToString());
            AddXmlElement(xmlDoc, discInfo, "IsBDPlus", bdrom.IsBDPlus.ToString());
            AddXmlElement(xmlDoc, discInfo, "IsBDJava", bdrom.IsBDJava.ToString());
            AddXmlElement(xmlDoc, discInfo, "IsPSP", bdrom.IsPSP.ToString());
            AddXmlElement(xmlDoc, discInfo, "Is50Hz", bdrom.Is50Hz.ToString());

            // Playlists
            var playlistsToAnalyze = GetPlaylistsToAnalyze(bdrom, options);
            if (playlistsToAnalyze.Count > 0)
            {
                var playlistsElement = xmlDoc.CreateElement("Playlists");
                root.AppendChild(playlistsElement);

                foreach (var playlist in playlistsToAnalyze.OrderBy(p => p.Name))
                {
                    var playlistElement = xmlDoc.CreateElement("Playlist");
                    playlistsElement.AppendChild(playlistElement);

                    AddXmlElement(xmlDoc, playlistElement, "Name", playlist.Name);
                    AddXmlElement(xmlDoc, playlistElement, "Duration", playlist.TotalLength.ToString());
                    AddXmlElement(xmlDoc, playlistElement, "Size", playlist.TotalSize.ToString());
                    AddXmlElement(xmlDoc, playlistElement, "StreamClips", playlist.StreamClips.Count.ToString());

                    if (options.DisplayChapters)
                    {
                        AddXmlElement(xmlDoc, playlistElement, "Chapters", playlist.Chapters.Count.ToString());
                    }
                }
            }

            // Stream Files
            if (bdrom.StreamFiles.Count > 0)
            {
                var streamFilesElement = xmlDoc.CreateElement("StreamFiles");
                root.AppendChild(streamFilesElement);

                foreach (var stream in bdrom.StreamFiles.Values.OrderBy(s => s.Name))
                {
                    var streamElement = xmlDoc.CreateElement("StreamFile");
                    streamFilesElement.AppendChild(streamElement);

                    AddXmlElement(xmlDoc, streamElement, "Name", stream.Name);
                    AddXmlElement(xmlDoc, streamElement, "Size", stream.Size.ToString());
                    AddXmlElement(xmlDoc, streamElement, "Duration", stream.Length.ToString());
                    AddXmlElement(xmlDoc, streamElement, "Streams", stream.Streams.Count.ToString());
                }
            }

            return xmlDoc.OuterXml;
        }

        static string GenerateJsonReport(BDROM bdrom, Options options)
        {
            var json = new StringBuilder();
            json.AppendLine("{");

            // Disc Information
            json.AppendLine("  \"discInfo\": {");
            json.AppendLine($"    \"discTitle\": \"{bdrom.DiscTitle ?? "Unknown"}\",");
            json.AppendLine($"    \"volumeLabel\": \"{bdrom.VolumeLabel ?? "Unknown"}\",");
            json.AppendLine($"    \"size\": {bdrom.Size},");
            json.AppendLine($"    \"isImage\": {bdrom.IsImage.ToString().ToLower()},");
            json.AppendLine($"    \"is3D\": {bdrom.Is3D.ToString().ToLower()},");
            json.AppendLine($"    \"isUHD\": {bdrom.IsUHD.ToString().ToLower()},");
            json.AppendLine($"    \"isBDPlus\": {bdrom.IsBDPlus.ToString().ToLower()},");
            json.AppendLine($"    \"isBDJava\": {bdrom.IsBDJava.ToString().ToLower()},");
            json.AppendLine($"    \"isPSP\": {bdrom.IsPSP.ToString().ToLower()},");
            json.AppendLine($"    \"is50Hz\": {bdrom.Is50Hz.ToString().ToLower()}");
            json.AppendLine("  },");

            // Playlists
            var playlistsToAnalyze = GetPlaylistsToAnalyze(bdrom, options);
            if (playlistsToAnalyze.Count > 0)
            {
                json.AppendLine("  \"playlists\": [");
                var playlistCount = 0;
                foreach (var playlist in playlistsToAnalyze.OrderBy(p => p.Name))
                {
                    if (playlistCount > 0) json.AppendLine(",");
                    json.AppendLine("    {");
                    json.AppendLine($"      \"name\": \"{playlist.Name}\",");
                    json.AppendLine($"      \"duration\": \"{playlist.TotalLength}\",");
                    json.AppendLine($"      \"size\": {playlist.TotalSize},");
                    json.AppendLine($"      \"streamClips\": {playlist.StreamClips.Count}");
                    if (options.DisplayChapters)
                    {
                        json.AppendLine($",      \"chapters\": {playlist.Chapters.Count}");
                    }
                    json.AppendLine("    }");
                    playlistCount++;
                }
                json.AppendLine("  ],");
            }

            // Stream Files
            if (bdrom.StreamFiles.Count > 0)
            {
                json.AppendLine("  \"streamFiles\": [");
                var streamCount = 0;
                foreach (var stream in bdrom.StreamFiles.Values.OrderBy(s => s.Name))
                {
                    if (streamCount > 0) json.AppendLine(",");
                    json.AppendLine("    {");
                    json.AppendLine($"      \"name\": \"{stream.Name}\",");
                    json.AppendLine($"      \"size\": {stream.Size},");
                    json.AppendLine($"      \"duration\": \"{stream.Length}\",");
                    json.AppendLine($"      \"streams\": {stream.Streams.Count}");
                    json.AppendLine("    }");
                    streamCount++;
                }
                json.AppendLine("  ]");
            }

            json.AppendLine("}");
            return json.ToString();
        }

        static List<TSPlaylistFile> GetPlaylistsToAnalyze(BDROM bdrom, Options options)
        {
            var playlists = new List<TSPlaylistFile>();

            if (!string.IsNullOrEmpty(options.Playlists))
            {
                var playlistNames = options.Playlists.Split(',');
                playlists = bdrom.PlaylistFiles.Values
                    .Where(p => playlistNames.Contains(p.Name))
                    .ToList();
            }
            else
            {
                playlists = bdrom.PlaylistFiles.Values.ToList();
            }

            // Apply the same filtering logic as the GUI
            if (BDInfoSettings.FilterShortPlaylists)
            {
                playlists = playlists.Where(p => p.TotalLength >= BDInfoSettings.FilterShortPlaylistsValue).ToList();
            }

            if (BDInfoSettings.FilterLoopingPlaylists)
            {
                playlists = playlists.Where(p => !p.HasLoops).ToList();
            }

            // Apply the same sorting as the GUI (FormMain.ComparePlaylistFiles)
            playlists.Sort((x, y) =>
            {
                if (x.TotalLength != y.TotalLength)
                {
                    return y.TotalLength.CompareTo(x.TotalLength); // Descending by length
                }
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });

            return playlists;
        }

        static void AddXmlElement(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var element = doc.CreateElement(name);
            element.InnerText = value;
            parent.AppendChild(element);
        }

        static string FormatSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
