namespace csv_repo.Models;

public class FileResponse
{
    public object[] Items { get; set; }

    public int Count { get; set; }

    public int ScannedCount { get; set; }
}