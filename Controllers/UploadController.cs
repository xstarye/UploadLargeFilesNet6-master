using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace UploadLargeFileNet8.Controllers;

[ApiController]
[Route("[controller]")]
public class UploadController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly SemaphoreSlim _downloadSemaphore = new(3);//最多允许三个并发

    private static int _downloadAttempts = 0;
    private static readonly int _maxDownloadAttempts = 5;

    private static readonly SemaphoreSlim _uploadSemaphore = new(2);
    private static readonly string[] AllowedMimeTypes = { "image/jpeg", "image/png", "application/pdf" };
    private const long MaxFileSize = 100 * 1024 * 1024; // 100MB

    public UploadController(IWebHostEnvironment env, IHttpClientFactory httpClientFactory)
    {
        _env = env;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet(Name = "Upload")]
    public async Task<IActionResult> Get()
    {
        if (!await _downloadSemaphore.WaitAsync(0))
            return StatusCode(429, "当前系统繁忙，请稍后再试");

        try
        {
            if (_downloadAttempts >= _maxDownloadAttempts)
                return StatusCode(403, "已达到最大下载次数");
            //增加缓存机制，减少每次都下载
            var filePath = Path.Combine(_env.WebRootPath, "cache", "largefile.zip");

            if (!System.IO.File.Exists(filePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                //var client = _httpClientFactory.CreateClient();
                //创建指定的client，保证超时以及最大的大小
                var client = _httpClientFactory.CreateClient("LargeFileClient");
                var url = "https://example.com/largefile.zip";

                _downloadAttempts++;
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));//防止下载任务长时间挂起
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);//这一个比较重要，设置不是一次性读取加载，而是流式读取加载
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fs);

                _downloadAttempts = 0; // Reset on success
            }

            return PhysicalFile(filePath, "application/zip", "largefile.zip");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[下载失败] {DateTime.Now}: {ex.Message}");
            return StatusCode(500, "下载失败，请稍后重试");
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Post(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("请选择一个文件");

        if (file.Length > MaxFileSize)
            return BadRequest("文件过大");

        if (!AllowedMimeTypes.Contains(file.ContentType))
            return BadRequest("不支持的文件类型");

        if (!await _uploadSemaphore.WaitAsync(0))
            return StatusCode(429, "当前上传任务繁忙，请稍后重试");

        try
        {
            var uploads = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploads);

            var safeFileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploads, safeFileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            return Ok("上传成功");
        }
        finally
        {
            _uploadSemaphore.Release();
        }
    }
}
