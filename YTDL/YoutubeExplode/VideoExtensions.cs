using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Spectre.Console;
using YoutubeExplode;
using YoutubeExplode.Videos;

namespace YTDL.YoutubeExplode;

public static class MethodInfoExtensions
{
    public static async Task<object> InvokeAsync(this MethodInfo @this, object obj, params object[] parameters)
    {
        var task = (Task)@this.Invoke(obj, parameters);
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty.GetValue(task);
    }
}

public static class VideoExtensions
{
    public static async Task<IReadOnlyCollection<Chapter>> TryGetChaptersAsync(this Video video, YoutubeClient client)
    {
        // wooo, whacky hacky
        try
        {
            var assembly = typeof(YoutubeClient).Assembly;
            var httpClient = typeof(VideoClient).GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(client.Videos);

            var watchPageObj = assembly.GetType("YoutubeExplode.ReverseEngineering.Responses.WatchPage");
            var methodInfo = watchPageObj.GetMethod("GetAsync");

            var parameters = new object[] { httpClient, video.Id.ToString() };

            var watchPage = await methodInfo.InvokeAsync(null, parameters);

            var root = (IHtmlDocument)watchPage.GetType().GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(watchPage);

            var ytInitialData = root
                .GetElementsByTagName("script")
                .Select(e => e.Text())
                .FirstOrDefault(s => s.Contains("window[\"ytInitialData\"] ="));

            if (string.IsNullOrWhiteSpace(ytInitialData))
            {
                return new List<Chapter>().AsReadOnly();
            }

            var json = Regex.Match(ytInitialData, "window\\[\"ytInitialData\"\\]\\s*=\\s*(.+?})(?:\"\\))?;", RegexOptions.Singleline).Groups[1].Value;

            using var doc = JsonDocument.Parse(json);
            var jsonDocument = doc.RootElement.Clone();
            var chaptersArray = jsonDocument
                    .GetProperty("playerOverlays")
                    .GetProperty("playerOverlayRenderer")
                    .GetProperty("decoratedPlayerBarRenderer")
                    .GetProperty("decoratedPlayerBarRenderer")
                    .GetProperty("playerBar")
                    .GetProperty("chapteredPlayerBarRenderer")
                    .GetProperty("chapters")
                    .EnumerateArray()
                    .Select(j => new Chapter(
                        j.GetProperty("chapterRenderer").GetProperty("title").GetProperty("simpleText").GetString(),
                        j.GetProperty("chapterRenderer").GetProperty("timeRangeStartMillis").GetUInt64()));

            return chaptersArray.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            AnsiConsole.WriteLine("Getting chapters failed");
            AnsiConsole.WriteLine(ex.Message);
            Console.ForegroundColor = color;
        }

        return new List<Chapter>().AsReadOnly();
    }
}
