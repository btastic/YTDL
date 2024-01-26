using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Konseben.CueSheets;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using Spectre.Console;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Exceptions;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YTDL.YoutubeExplode;

namespace YTDL;

public enum MenuOption
{
    DownloadAudio,
    DownloadVideo,
    DownloadFFMpeg,
    Exit,
}

public static class Program
{
    private static readonly YoutubeClient Client = new YoutubeClient();

    private static bool _ffmpegAvailable;

    public static ApplicationConfiguration ApplicationConfiguration { get; private set; }

    public static async Task Main(string[] args)
    {
        // this ensures we are always in the right directory
        // otherwise the config won't be found
        var exeDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        Directory.SetCurrentDirectory(exeDirectory);

        Initialize();
        var cancellationTokenSource = new CancellationTokenSource();
        _ffmpegAvailable = FFMpegAvailable();

        if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            if (_ffmpegAvailable)
            {
                var videoId = VideoId.Parse(args[0]);
                var downloadedFile =
                    await ExecuteCommand(MenuOption.DownloadAudio, videoId, cancellationTokenSource.Token);

                if (ApplicationConfiguration.OpenAfterCommandLineDownload)
                {
                    OpenWithDefaultProgram(downloadedFile);
                }

                Environment.Exit(0);
            }
            else
            {
                AnsiConsole.WriteLine("[red]WARNING[/]");
                AnsiConsole.WriteLine("[red]Parsing via command line is only available when FFMpeg was previously installed.[/]");
                AnsiConsole.WriteLine("");
            }
        }

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        while (true)
        {
            if (cancellationTokenSource.Token.IsCancellationRequested)
            {
                cancellationTokenSource = new CancellationTokenSource();
            }

            PrintLogo();

            var command = AnsiConsole.Prompt(new SelectionPrompt<MenuOption>()
                .Title("Please select an option")
                .UseConverter(d =>
                {
                    switch (d)
                    {
                        case MenuOption.DownloadAudio:
                            return "Download audio";
                        case MenuOption.DownloadVideo:
                            return "Download video";
                        case MenuOption.DownloadFFMpeg:
                            return _ffmpegAvailable ? "Install ffmpeg (installed)" : "Install ffmpeg (required to download audio)";
                        case MenuOption.Exit:
                            return "Exit";
                        default:
                            break;
                    }

                    return string.Empty;
                })
                .AddChoices(MenuOption.DownloadAudio, MenuOption.DownloadVideo, MenuOption.DownloadFFMpeg, MenuOption.Exit));

            try
            {
                await ExecuteCommand(command, new VideoId(), cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine("Task was canceled by user.");
            }

            AnsiConsole.WriteLine($"Press a key to restart.");
            Console.ReadKey();

            Console.Clear();
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

        FFmpeg.SetExecutablesPath(new DirectoryInfo(".").FullName, "ffmpeg.exe", "ffprobe.exe");
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

    private static VideoId ParseVideoId()
    {
        var link = AnsiConsole.Ask<string>("Enter a YouTube Link: ");

        string videoId;

        try
        {
            videoId = VideoId.Parse(link);
        }
        catch (Exception)
        {
            AnsiConsole.WriteLine("Input was not recognized as a youtube link.");
            return string.Empty;
        }

        return videoId;
    }

    private static async Task<string> ExecuteCommand(MenuOption command, VideoId videoId = default, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case MenuOption.DownloadAudio:
                if (!_ffmpegAvailable)
                {
                    AnsiConsole.WriteLine("Please install ffmpeg ...");
                    break;
                }

                if (videoId == default)
                {
                    videoId = ParseVideoId();
                }

                if (string.IsNullOrEmpty(videoId))
                {
                    break;
                }

                return await DownloadAudioAsync(videoId, cancellationToken);

            case MenuOption.DownloadVideo:
                videoId = ParseVideoId();

                if (string.IsNullOrEmpty(videoId))
                {
                    break;
                }

                return await DownloadVideoAsync(videoId, cancellationToken);

            case MenuOption.DownloadFFMpeg:
                if (_ffmpegAvailable)
                {
                    AnsiConsole.WriteLine($"ffmpeg already available.");
                    break;
                }
                AnsiConsole.WriteLine($"Downloading ffmpeg. This might take a while ... ");

                bool downloaded = false;

                await AnsiConsole.Progress()
                    .StartAsync(async ctx =>
                    {
                        var conversionTask = ctx.AddTask("Downloading ffmpeg");
                        conversionTask.IsIndeterminate(true);
                        downloaded = await FFmpegDownloader.DownloadFFmpegAsync();
                    });


                if (downloaded)
                {
                    AnsiConsole.WriteLine($"ffmpeg downloaded ... ");
                    _ffmpegAvailable = true;
                }
                else
                {
                    AnsiConsole.WriteLine("ffmpeg download failed.");
                }

                break;

            case MenuOption.Exit:
                Environment.Exit(0);
                break;
        }

        return string.Empty;
    }

    private static async Task<string> DownloadAudioAsync(string videoId, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine($"[{videoId}] Loading data ... ");
        var generalInfo = await Client.Videos.GetAsync(videoId);
        var videoInfo = await Client.Videos.Streams.GetManifestAsync(videoId);

        var audioInfo = videoInfo.GetAudioOnlyStreams().GetWithHighestBitrate();
        var downloadedFile = await DownloadMedia(audioInfo, generalInfo, cancellationToken);
        var convertedFile = await ConvertAudio(downloadedFile, cancellationToken);

        if (ApplicationConfiguration.CreateCueFileFromChapters)
        {
            AnsiConsole.WriteLine("Generating cue file ... ");
            await CreateCueFileFromChapters(generalInfo, convertedFile);
        }

        AnsiConsole.WriteLine($"Converted to: {convertedFile}");

        return convertedFile;
    }

    private static async Task CreateCueFileFromChapters(Video video, string targetMp3File)
    {
        AnsiConsole.WriteLine("Try getting chapters ... ");
        var chapters = await video.TryGetChaptersAsync(Client);

        if (chapters.Count() == 0)
        {
            AnsiConsole.WriteLine("[red]No chapters found for video.[/]");

            return;
        }

        var filePath = Path.GetDirectoryName(targetMp3File);
        var fileName = Path.GetFileName(targetMp3File);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetMp3File);

        var cueSheet = new CueSheet
        {
            Performer = "Various Artists",
            Title = video.Title,
            File = fileName,
            FileType = "MP3",
        };

        int trackIndex = 0;
        foreach (var chapter in chapters)
        {
            var artist = chapter.Title;
            var title = chapter.Title;

            MatchCollection regexResult = Regex.Matches(chapter.Title, "^(.+)(?=-)|(?!<-)([^-]+)$");

            if (regexResult.Count > 1)
            {
                if (regexResult[0].Success && regexResult[1].Success)
                {
                    artist = regexResult[0].Groups[0].Value.Trim();
                    title = regexResult[1].Groups[0].Value.Trim();
                }
            }

            var duration = TimeSpan.FromMilliseconds(chapter.TimeRangeStart);

            if (cueSheet.Tracks.Count == 0)
            {
                cueSheet.AddTrack(title, artist);
                cueSheet.AddIndex(trackIndex++, 1, 0, 0, 0);
                continue;
            }

            cueSheet.AddTrack(title, artist);
            cueSheet.AddIndex(trackIndex++, 1, (int)Math.Round(duration.TotalMinutes, MidpointRounding.ToEven), duration.Seconds, 0);
        }

        cueSheet.SaveCue(Path.Combine(filePath, fileNameWithoutExtension + ".cue"));
    }

    private static async Task<string> ConvertAudio(string downloadedFile, CancellationToken cancellationToken)
    {
        var mediaInfo = await FFmpeg.GetMediaInfo(downloadedFile);

        var mp3File = Path.ChangeExtension(downloadedFile, "mp3");

        if (!string.IsNullOrEmpty(ApplicationConfiguration.DownloadPathOverride))
        {
            mp3File = Path.Combine(ApplicationConfiguration.DownloadPathOverride, mp3File);
        }

        AnsiConsole.Write($"Convert to mp3 ... ");

        var conversion = await FFmpeg.Conversions.FromSnippet.ExtractAudio(downloadedFile, mp3File);

        conversion.SetOverwriteOutput(true);
        conversion.UseMultiThread(true);

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var conversionTask = ctx.AddTask("Converting to mp3");
                conversion.OnProgress += (sender, args) =>
                {
                    conversionTask.MaxValue = args.TotalLength.TotalSeconds;
                    conversionTask.Value = args.Duration.TotalSeconds;
                };

                try
                {
                    await conversion.Start(cancellationToken);
                }
                catch (ConversionException ex)
                {
                    AnsiConsole.WriteLine("Conversion error: ");
                    AnsiConsole.WriteLine(ex.Message);
                }
            });

        if (!ApplicationConfiguration.KeepSourceFile)
        {
            File.Delete(downloadedFile);
        }

        return mp3File;
    }

    private static async Task<string> DownloadVideoAsync(string videoId, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine($"[{videoId}] Loading data ... ");
        var generalInfo = await Client.Videos.GetAsync(videoId);
        var videoInfo = await Client.Videos.Streams.GetManifestAsync(videoId);

        var streamInfo = videoInfo.GetMuxedStreams().GetWithHighestVideoQuality();
        var downloadedFile = await DownloadMedia(streamInfo, generalInfo, cancellationToken);

        AnsiConsole.WriteLine($"Downloaded to: {downloadedFile}");

        return downloadedFile;
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

        AnsiConsole.Write("Downloading... ");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var downloadTask = ctx.AddTask("Downloading video stream");
                downloadTask.MaxValue = 1;
                await Client.Videos.Streams.DownloadAsync(mediaInfo, fileName, downloadTask, cancellationToken);
            });

        return fileName;
    }

    public static void OpenWithDefaultProgram(string path)
    {
        Process process = new Process();
        process.StartInfo.FileName = "explorer";
        process.StartInfo.Arguments = "\"" + path + "\"";
        process.Start();
    }

    private static void PrintLogo()
    {
        AnsiConsole.Write(new FigletText("YTDL").LeftJustified().Color(Color.DarkCyan));
    }
}
