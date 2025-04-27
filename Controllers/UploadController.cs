using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace UploadLargeFileNet8.Controllers;

[ApiController]
[Route("[controller]")]
public class UploadController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly SemaphoreSlim _downloadSemaphore = new(3);//���������������

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
            return StatusCode(429, "��ǰϵͳ��æ�����Ժ�����");

        try
        {
            if (_downloadAttempts >= _maxDownloadAttempts)
                return StatusCode(403, "�Ѵﵽ������ش���");
            //���ӻ�����ƣ�����ÿ�ζ�����
            var filePath = Path.Combine(_env.WebRootPath, "cache", "largefile.zip");

            if (!System.IO.File.Exists(filePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                //var client = _httpClientFactory.CreateClient();
                //����ָ����client����֤��ʱ�Լ����Ĵ�С
                var client = _httpClientFactory.CreateClient("LargeFileClient");
                var url = "https://example.com/largefile.zip";

                _downloadAttempts++;
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));//��ֹ��������ʱ�����
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);//��һ���Ƚ���Ҫ�����ò���һ���Զ�ȡ���أ�������ʽ��ȡ����
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
            Console.WriteLine($"[����ʧ��] {DateTime.Now}: {ex.Message}");
            return StatusCode(500, "����ʧ�ܣ����Ժ�����");
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
            return BadRequest("��ѡ��һ���ļ�");

        if (file.Length > MaxFileSize)
            return BadRequest("�ļ�����");

        if (!AllowedMimeTypes.Contains(file.ContentType))
            return BadRequest("��֧�ֵ��ļ�����");

        if (!await _uploadSemaphore.WaitAsync(0))
            return StatusCode(429, "��ǰ�ϴ�����æ�����Ժ�����");

        try
        {
            var uploads = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploads);

            var safeFileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploads, safeFileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            return Ok("�ϴ��ɹ�");
        }
        finally
        {
            _uploadSemaphore.Release();
        }
    }
}
