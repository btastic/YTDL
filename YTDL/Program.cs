using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using YoutubeExplode;
using YoutubeExplode.Models;
using YoutubeExplode.Models.MediaStreams;

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

        public static async Task Main()
        {
            _ffmpegAvailable = FFMpegAvailable();

            PrintMenu(true);

            while (true)
            {
                var optionInput = Console.ReadLine();

                var command = EvaluateInput(optionInput);

                if (command == MenuOption.Invalid)
                {
                    Console.WriteLine("Invalid option.");
                    PrintMenu(false);
                    continue;
                }

                await ExecuteCommand(command);

                Console.WriteLine($"Press a key to restart.");
                Console.ReadKey();

                Console.Clear();
                PrintMenu(true);
            }
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

        private static bool GetYesOrNo(string question)
        {
            while (true)
            {
                Console.Write(question);

                var input = Console.ReadLine();

                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                switch (input.ToLower())
                {
                    case "n":
                        return false;
                    case "y":
                        return true;
                }
            }
        }

        private static string ParseVideoId()
        {
            Console.Write("Enter link > ");
            var link = Console.ReadLine();

            string videoId;

            try
            {
                videoId = YoutubeClient.ParseVideoId(link);
            }
            catch (Exception)
            {
                Console.WriteLine("Input was not recognized as a youtube link.");
                return string.Empty;
            }

            return videoId;
        }

        private static async Task ExecuteCommand(MenuOption command)
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

                    var convertAudio = false;

                    await DownloadAudioAsync(videoId);
                    break;

                case MenuOption.DownloadVideo:
                    videoId = ParseVideoId();

                    if (string.IsNullOrEmpty(videoId))
                    {
                        break;
                    }

                    await DownloadVideoAsync(videoId);
                    break;

                case MenuOption.DownloadFFMpeg:
                    if (_ffmpegAvailable)
                    {
                        Console.WriteLine($"ffmpeg already available.");
                        break;
                    }
                    Console.WriteLine($"Downloading ffmpeg. This might take a while ... ");
                    await FFmpeg.GetLatestVersion();
                    Console.WriteLine($"ffmpeg downloaded ... ");
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

        private static async Task DownloadAudioAsync(string videoId)
        {
            Console.WriteLine($"[{videoId}] Loading data ... ");
            var generalInfo = await Client.GetVideoAsync(videoId);
            var videoInfo = await Client.GetVideoMediaStreamInfosAsync(videoId);

            var audioInfo = videoInfo.Audio.WithHighestBitrate();
            var downloadedFile = await DownloadMedia(audioInfo, generalInfo);
            var convertedFile = await ConvertAudio(downloadedFile);

            Console.WriteLine($"Converted to: {convertedFile}");
        }

        private static async Task<string> ConvertAudio(string downloadedFile)
        {
            var mediaInfo = await MediaInfo.Get(downloadedFile);

            var mp3File = Path.ChangeExtension(downloadedFile, "mp3");

            Console.Write($"Convert to mp3 ... ");

            var conversion = Conversion.ExtractAudio(downloadedFile, mp3File);
            conversion.SetOverwriteOutput(true);
            //var conversion = Conversion
            //    .New()
            //    .SetPreset(ConversionPreset.VerySlow)
            //    .UseMultiThread(true)
            //    .AddStream(mediaInfo.AudioStreams.FirstOrDefault())
            //    .SetOutput(mp3File)
            //    .SetOverwriteOutput(true);

            Console.ForegroundColor = ConsoleColor.Cyan;
            var progress = new ConsoleProgressBar();

            conversion.OnProgress += (sender, args) =>
            {
                var percent = args.Duration.TotalSeconds / args.TotalLength.TotalSeconds;
                // we can safely ignore this error due to always disposing it
                // ReSharper disable once AccessToDisposedClosure
                progress.Report(percent);
            };

            try
            {
                await conversion.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Conversion error: ");
                Console.WriteLine(ex.Message);
            }
            finally
            {
                progress.Dispose();
                Console.ResetColor();
            }

            File.Delete(downloadedFile);

            return mp3File;
        }

        private static async Task DownloadVideoAsync(string videoId)
        {
            Console.WriteLine($"[{videoId}] Loading data ... ");
            var generalInfo = await Client.GetVideoAsync(videoId);
            var videoInfo = await Client.GetVideoMediaStreamInfosAsync(videoId);

            var streamInfo = videoInfo.Muxed.WithHighestVideoQuality();
            var downloadedFile = await DownloadMedia(streamInfo, generalInfo);

            Console.WriteLine($"Downloaded to: {downloadedFile}");
        }

        private static async Task<string> DownloadMedia(MediaStreamInfo mediaInfo, Video generalInfo)
        {
            var fileExtension = mediaInfo.Container.GetFileExtension();
            var fileName = $"{generalInfo.Title}.{fileExtension}";

            fileName = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, '-'));

            Console.Write("Downloading... ");

            Console.ForegroundColor = ConsoleColor.Cyan;

            using (var progress = new ConsoleProgressBar())
            {
                await Client.DownloadMediaStreamAsync(mediaInfo, fileName, progress);
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
