using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Spectre.Console;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Extensions;

namespace YTDL;

public static class FFmpegDownloader
{
    private static HttpClient _httpClient = new HttpClient();

    public static async Task<bool> DownloadFFmpegAsync()
    {
        var ffmpegDownloadUrl = "https://webradio.hinekure.net/ffmpeg/ffmpeg.zeranoe.com/builds/win64/shared/ffmpeg-4.3.1-win64-shared.zip";

        var ffmpegFolderName = $"ffmpeg-4.1.3-{(Environment.Is64BitProcess ? "64" : "32")}-static";
        var ffmpegZipName = $"{ffmpegFolderName}.zip";

        try
        {
            using (FileStream fs = new(ffmpegZipName, FileMode.Create, FileAccess.Write))
            {
                await _httpClient.DownloadAsync(ffmpegDownloadUrl, fs);
                fs.Flush();
            }
        }
        catch (WebException ex)
        {
            AnsiConsole.WriteLine(ex.ToString());
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine(ex.ToString());
            return false;
        }

        ZipFile.ExtractToDirectory($".\\{ffmpegZipName}", $".\\{ffmpegFolderName}\\", true);

        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\ffmpeg.exe", ".\\ffmpeg.exe");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\ffprobe.exe", ".\\ffprobe.exe");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\ffplay.exe", ".\\ffplay.exe");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\avcodec-58.dll", ".\\avcodec-58.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\avdevice-58.dll", ".\\avdevice-58.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\avfilter-7.dll", ".\\avfilter-7.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\avformat-58.dll", ".\\avformat-58.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\avutil-56.dll", ".\\avutil-56.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\swresample-3.dll", ".\\swresample-3.dll");
        File.Copy($".\\{ffmpegFolderName}\\ffmpeg-4.3.1-win64-shared\\bin\\swscale-5.dll", ".\\swscale-5.dll");

        Directory.Delete($".\\{ffmpegFolderName}\\", true);

        File.Delete($".\\{ffmpegZipName}");

        return true;
    }
}
