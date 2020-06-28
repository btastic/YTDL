using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Exceptions;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YTDL
{
    public enum MenuOption
    {
        DownloadAudio,
        DownloadVideo,
        DownloadFFMpeg,
        Exit,
        Invalid,
    }

    public static class Program
    {
        private static readonly YoutubeClient Client = new YoutubeClient();

        private static bool _ffmpegAvailable;

        public static ApplicationConfiguration ApplicationConfiguration { get; private set; }

        public static async Task Main()
        {
            Initialize();

            var cancellationTokenSource = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
            };

            _ffmpegAvailable = FFMpegAvailable();

            PrintMenu(true);

            while (true)
            {
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    cancellationTokenSource = new CancellationTokenSource();
                }

                cancellationTokenSource = new CancellationTokenSource();
                var optionInput = Console.ReadLine();

                var command = EvaluateInput(optionInput);

                if (command == MenuOption.Invalid)
                {
                    Console.WriteLine("Invalid option.");
                    PrintMenu(false);
                    continue;
                }

                try
                {
                    await ExecuteCommand(command, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Task was canceled by user.");
                }

                Console.WriteLine($"Press a key to restart.");
                Console.ReadKey();

                Console.Clear();
                PrintMenu(true);
            }
        }

        private static void Initialize()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var configuration = builder.Build();

            var config = new ApplicationConfiguration();
            configuration.Bind("Settings", config);

            ApplicationConfiguration = config;

            ApplicationConfiguration.DownloadPathOverride =
                Environment.ExpandEnvironmentVariables(ApplicationConfiguration.DownloadPathOverride);

            FFmpeg.ExecutablesPath = new DirectoryInfo(".").FullName;
        }

        private static bool FFMpegAvailable()
        {
            const string ffmpegExecutable = "ffmpeg.exe";
            const string ffprobeExecutable = "ffprobe.exe";

            var ffmpegExists = false;
            var ffProbeExists = false;

            var directoryInfo = new DirectoryInfo(".");
            var executables = directoryInfo.GetFiles("ff*.exe");

            foreach (var executable in executables)
            {
                switch (executable.Name)
                {
                    case ffmpegExecutable:
                        ffmpegExists = true;
                        break;
                    case ffprobeExecutable:
                        ffProbeExists = true;
                        break;
                }
            }

            return ffmpegExists && ffProbeExists;
        }

        private static string ParseVideoId()
        {
            Console.Write("Enter link > ");
            var link = Console.ReadLine();

            string videoId;

            try
            {
                videoId = new VideoId(link);
            }
            catch (Exception)
            {
                Console.WriteLine("Input was not recognized as a youtube link.");
                return string.Empty;
            }

            return videoId;
        }

        private static async Task ExecuteCommand(MenuOption command, CancellationToken cancellationToken)
        {
            string videoId;

            switch (command)
            {
                case MenuOption.DownloadAudio:
                    if (!_ffmpegAvailable)
                    {
                        Console.WriteLine("Please install ffmpeg ...");
                        break;
                    }

                    videoId = ParseVideoId();

                    if (string.IsNullOrEmpty(videoId))
                    {
                        break;
                    }

                    await DownloadAudioAsync(videoId, cancellationToken);
                    break;

                case MenuOption.DownloadVideo:
                    videoId = ParseVideoId();

                    if (string.IsNullOrEmpty(videoId))
                    {
                        break;
                    }

                    await DownloadVideoAsync(videoId, cancellationToken);
                    break;

                case MenuOption.DownloadFFMpeg:
                    if (_ffmpegAvailable)
                    {
                        Console.WriteLine($"ffmpeg already available.");
                        break;
                    }
                    Console.WriteLine($"Downloading ffmpeg. This might take a while ... ");
                    var downloaded = await FFmpegDownloader.DownloadFFmpegAsync();

                    if (downloaded)
                    {
                        Console.WriteLine($"ffmpeg downloaded ... ");
                        _ffmpegAvailable = true;
                    }
                    else
                    {
                        Console.WriteLine("ffmpeg download failed.");
                    }

                    break;

                case MenuOption.Exit:
                    Environment.Exit(0);
                    break;
            }
        }

        private static MenuOption EvaluateInput(string optionInput)
        {
            switch (optionInput)
            {
                case "1":
                    return MenuOption.DownloadAudio;
                case "2":
                    return MenuOption.DownloadVideo;
                case "3":
                    return MenuOption.DownloadFFMpeg;
                case "4":
                    return MenuOption.Exit;
                default:
                    return MenuOption.Invalid;
            }
        }

        private static async Task DownloadAudioAsync(string videoId, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[{videoId}] Loading data ... ");
            var generalInfo = await Client.Videos.GetAsync(videoId);
            var videoInfo = await Client.Videos.Streams.GetManifestAsync(videoId);

            var audioInfo = videoInfo.GetAudio().WithHighestBitrate();
            var downloadedFile = await DownloadMedia(audioInfo, generalInfo, cancellationToken);
            var convertedFile = await ConvertAudio(downloadedFile, cancellationToken);

            Console.WriteLine($"Converted to: {convertedFile}");
        }

        private static async Task<string> ConvertAudio(string downloadedFile, CancellationToken cancellationToken)
        {
            var mediaInfo = await MediaInfo.Get(downloadedFile);

            var mp3File = Path.ChangeExtension(downloadedFile, "mp3");

            if (!string.IsNullOrEmpty(ApplicationConfiguration.DownloadPathOverride))
            {
                mp3File = Path.Combine(ApplicationConfiguration.DownloadPathOverride, mp3File);
            }

            Console.Write($"Convert to mp3 ... ");

            var conversion = Conversion.ExtractAudio(downloadedFile, mp3File);

            conversion.SetOverwriteOutput(true);
            conversion.UseMultiThread(true);

            Console.ForegroundColor = ConsoleColor.Cyan;
            var progress = new ConsoleProgressBar();

            conversion.OnProgress += (sender, args) =>
            {
                var percent = args.Duration.TotalSeconds / args.TotalLength.TotalSeconds;
                progress.Report(percent);
            };

            Stopwatch sw = new Stopwatch();
            try
            {
                sw.Start();
                await conversion.Start(cancellationToken);
            }
            catch (ConversionException ex)
            {
                Console.WriteLine("Conversion error: ");
                Console.WriteLine(ex.Message);
            }
            finally
            {
                sw.Stop();
                progress.Dispose();
                Console.ResetColor();
            }

            Console.WriteLine($"Conversion took {sw.ElapsedMilliseconds} ms");

            if (!ApplicationConfiguration.KeepSourceFile)
            {
                File.Delete(downloadedFile);
            }

            return mp3File;
        }

        private static async Task DownloadVideoAsync(string videoId, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[{videoId}] Loading data ... ");
            var generalInfo = await Client.Videos.GetAsync(videoId);
            var videoInfo = await Client.Videos.Streams.GetManifestAsync(videoId);

            var streamInfo = videoInfo.GetMuxed().WithHighestVideoQuality();
            var downloadedFile = await DownloadMedia(streamInfo, generalInfo, cancellationToken);

            Console.WriteLine($"Downloaded to: {downloadedFile}");
        }

        private static async Task<string> DownloadMedia(IStreamInfo mediaInfo, Video generalInfo, CancellationToken cancellationToken)
        {
            var fileExtension = mediaInfo.Container.Name;
            var fileName = $"{generalInfo.Title}.{fileExtension}";

            fileName = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, '-'));

            if (!string.IsNullOrEmpty(ApplicationConfiguration.DownloadPathOverride))
            {
                fileName = Path.Combine(ApplicationConfiguration.DownloadPathOverride, fileName);
            }

            Console.Write("Downloading... ");

            Console.ForegroundColor = ConsoleColor.Cyan;

            using (var progress = new ConsoleProgressBar())
            {
                await Client.Videos.Streams.DownloadAsync(mediaInfo, fileName, progress, cancellationToken);
                ;
            }

            Console.ResetColor();

            Console.WriteLine();

            return fileName;
        }

        private static void PrintMenu(bool printLogo)
        {
            if (printLogo)
            {
                PrintLogo();
            }

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("1) Download audio");
            Console.WriteLine("2) Download video");

            Console.Write("3) Download ffmpeg (required to download audio)");

            if (_ffmpegAvailable)
            {
                Console.WriteLine(" (installed)");
            }
            else
            {
                Console.WriteLine();
            }

            Console.WriteLine("4) Exit");

            Console.WriteLine("");

            Console.Write("Please enter an option > ");
        }

        private static void PrintLogo()
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(
                "██╗   ██╗████████╗██████╗ ██╗       \r\n" +
                "╚██╗ ██╔╝╚══██╔══╝██╔══██╗██║       \r\n" +
                " ╚████╔╝    ██║   ██║  ██║██║       YOUTUBE\r\n" +
                "  ╚██╔╝     ██║   ██║  ██║██║       DOWNLOADER\r\n" +
                "   ██║      ██║   ██████╔╝███████╗  \r\n" +
                "   ╚═╝      ╚═╝   ╚═════╝ ╚══════╝  \r\n");

            Console.ResetColor();
        }
    }
}
