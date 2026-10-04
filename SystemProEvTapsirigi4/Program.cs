using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class DownloadItem
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "Gozleyir";
    public long TotalBytes { get; set; }
    public long ReadBytes { get; set; }
    public int ProgressPercentage { get; set; }
    public int ConsoleRow { get; set; }
    public Process? Proces { get; set; }
    public void StartProcess()
    {
        Proces = Process.Start("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"", Url);
    }
    public void KillProcess()
    {
        Proces?.Kill();
    }
}

class Program
{
    static object consoleLock = new object();

    static SemaphoreSlim semaphore = new SemaphoreSlim(3, 4);

    static CancellationTokenSource cts = new CancellationTokenSource();

    static DownloadItem[] downloads = new DownloadItem[0];

    static void Main(string[] args)
    {
        Console.CursorVisible = false;
        Console.Clear();

        // Massivin elanı və mənimsədilməsi
        downloads = new DownloadItem[]
        {
            new DownloadItem { Id = 1, Url = "https://proof.ovh.net/files/10Mb.dat", FileName = "mp4_sample_file1_10MB.mp4", ConsoleRow = 10 },
            new DownloadItem { Id = 2, Url = "https://proof.ovh.net/files/10Mb.dat", FileName = "mp4_sample_file2_10MB.mp4", ConsoleRow = 13 },
            new DownloadItem { Id = 3, Url = "https://proof.ovh.net/files/10Mb.dat", FileName = "mp4_sample_file3_10MB.mp4", ConsoleRow = 16 },
            new DownloadItem { Id = 4, Url = "https://proof.ovh.net/files/10Mb.dat", FileName = "mp4_sample_file4_10MB.mp4", ConsoleRow = 19 }
        };

        DrawHeader();

        // List əvəzinə Task massivi
        Task[] tasks = new Task[downloads.Length];

        for (int i = 0; i < downloads.Length; i++)
        {
            int index = i;
            tasks[index] = Task.Run(() => StartDownload(downloads[index], cts.Token));
        }


        while (tasks.Any(t => !t.IsCompleted))
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
            {
                cts.Cancel();
                UpdateFooter("Butun yuklemeler legv edildi...");
                break;
            }
            Thread.Sleep(100);
        }

        Task.WaitAll(tasks);

        Console.SetCursorPosition(0, 24);
        Console.CursorVisible = true;
        Console.WriteLine("\nIslem sonlandi. cixmaq ucun bir duymeye basin...");
        Console.ReadKey();
    }

    static void StartDownload(DownloadItem item, CancellationToken token)
    {
        RenderItemUI(item);

        try
        {
            semaphore.Wait(token);

            item.Status = "Yuklenir";
            RenderItemUI(item);
            item.StartProcess();
            using (HttpClient client = new HttpClient())
            {
                using (HttpResponseMessage response = client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, token).Result)
                {
                    response.EnsureSuccessStatusCode();
                    item.TotalBytes = response.Content.Headers.ContentLength ?? 10 * 1024 * 1024;

                    using (Stream stream = response.Content.ReadAsStreamAsync().Result)
                    using (FileStream fs = new FileStream(item.FileName, FileMode.Create, FileAccess.Write, FileShare.None, 8192))
                    {
                        byte[] buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (token.IsCancellationRequested)
                            {
                                item.KillProcess();
                                item.Status = "Canceled";
                                RenderItemUI(item);
                                return;
                            }

                            fs.Write(buffer, 0, bytesRead);
                            item.ReadBytes += bytesRead;
                            item.ProgressPercentage = (int)((double)item.ReadBytes / item.TotalBytes * 100);

                            RenderItemUI(item);
                        }
                    }
                }
            }

            item.Status = "Tamamlandi";
            RenderItemUI(item);
        }
        catch (OperationCanceledException)
        {
            item.Status = "Canceled";
            RenderItemUI(item);
        }
        catch (Exception)
        {
            item.Status = "Error";
            RenderItemUI(item);
        }
        finally
        {
            semaphore.Release();
            UpdateSummaryBar();
        }
    }

    static void RenderItemUI(DownloadItem item)
    {
        lock (consoleLock)
        {
            Console.SetCursorPosition(0, item.ConsoleRow);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"#{item.Id,-3} ");

            if (item.Status == "Yuklenir") Console.ForegroundColor = ConsoleColor.Cyan;
            else if (item.Status == "Gozleyir") Console.ForegroundColor = ConsoleColor.Yellow;
            else if (item.Status == "Tamamlandi") Console.ForegroundColor = ConsoleColor.Green;
            else Console.ForegroundColor = ConsoleColor.Red;

            Console.Write($"{item.Status,-12} ");

            string progressBar = GetProgressBar(item.ProgressPercentage);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(progressBar + " ");

            Console.ForegroundColor = ConsoleColor.White;
            double readMb = item.ReadBytes / (1024.0 * 1024.0);
            double totalMb = item.TotalBytes / (1024.0 * 1024.0);

            if (item.Status == "Waiting")
            {
                Console.Write($"  0%                      ");
            }
            else
            {
                Console.Write($"{item.ProgressPercentage,3}%  {readMb,4:F1} MB/{totalMb,4:F1} MB   ");
            }

            Console.SetCursorPosition(5, item.ConsoleRow + 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(item.FileName + "                              ");

            Console.ResetColor();
        }

        UpdateSummaryBar();
    }

    static string GetProgressBar(int percentage)
    {
        int totalBlocks = 20;
        int filledBlocks = (percentage * totalBlocks) / 100;
        if (filledBlocks > totalBlocks) filledBlocks = totalBlocks;

        string filled = new string('█', filledBlocks);
        string empty = new string('░', totalBlocks - filledBlocks);
        return $"[{filled}{empty}]";
    }

    static void DrawHeader()
    {
        lock (consoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔═════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                 MULTI-THREAD VIDEO DOWNLOADER                   ║");
            Console.WriteLine("║                                                                 ║");
            Console.WriteLine("╠═════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  1-3  → Downloading                                             ║");
            Console.WriteLine("║  4+   → Waiting                                                 ║");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("║  ESC  → Cancel ALL                                              ║\n");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╚═════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }
    }

    static void UpdateSummaryBar()
    {
        lock (consoleLock)
        {
            int active = downloads.Count(d => d.Status == "Downloading");
            int waiting = downloads.Count(d => d.Status == "Waiting");
            int completed = downloads.Count(d => d.Status == "Completed");
            int error = downloads.Count(d => d.Status == "Error" || d.Status == "Canceled");

            Console.SetCursorPosition(0, 22);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"Aktiv: {active} | Gozleyen: {waiting} | Tamamlanan: {completed} | Xeta/Iptal: {error}        ");
            Console.ResetColor();
        }
    }

    static void UpdateFooter(string message)
    {
        lock (consoleLock)
        {
            Console.SetCursorPosition(0, 23);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(message + "                               ");
            Console.ResetColor();
        }
    }

}
