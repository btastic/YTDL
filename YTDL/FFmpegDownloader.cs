using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace YTDL
{
    public static class FFmpegDownloader
    {
        public static async Task<bool> DownloadFFmpegAsync()
        {
            var architecture = Environment.Is64BitProcess ? "win64" : "win32";
            
            var ffmpegVersion = "4.1.3";
            var ffmpegFolderName = $"ffmpeg-{ffmpegVersion}-{architecture}-static";
            var ffmpegZipName = $"{ffmpegFolderName}.zip";
            var ffmpegDownloadUrl = $"https://ffmpeg.zeranoe.com/builds/{architecture}/static/{ffmpegZipName}";

            using (var webClient = new WebClient())
            {
                try
                {
                    await webClient.DownloadFileTaskAsync(new Uri(ffmpegDownloadUrl), ffmpegZipName);
                }
                catch (WebException ex)
                {
                    Console.WriteLine(ex);
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    return false;
                }
            }

            ZipFile.ExtractToDirectory($".\\{ffmpegZipName}", $".\\{ffmpegFolderName}\\", true);

            File.Copy($".\\{ffmpegFolderName}\\{ffmpegFolderName}\\bin\\ffmpeg.exe", ".\\ffmpeg.exe");
            File.Copy($".\\{ffmpegFolderName}\\{ffmpegFolderName}\\bin\\ffprobe.exe", ".\\ffprobe.exe");

            Directory.Delete($".\\{ffmpegFolderName}\\", true);

            File.Delete($".\\{ffmpegZipName}");

            return true;
        }
    }
}
