namespace YTDL;

public class ApplicationConfiguration
{
    public bool KeepSourceFile { get; set; }
    public string DownloadPathOverride { get; set; }
    public bool OpenAfterCommandLineDownload { get; set; }
    public bool CreateCueFileFromChapters { get; set; }
}
