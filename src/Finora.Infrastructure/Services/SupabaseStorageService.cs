using System.Net.Http.Headers;
using Finora.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Finora.Infrastructure.Services;

public class SupabaseStorageService : IFileStorageService
{
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(HttpClient http, IConfiguration configuration, ILogger<SupabaseStorageService> logger)
    {
        _http = http;
        _logger = logger;

        var url = configuration["Supabase:Url"]
            ?? throw new InvalidOperationException("Supabase:Url not configured.");
        var serviceKey = configuration["Supabase:ServiceRoleKey"]
            ?? throw new InvalidOperationException("Supabase:ServiceRoleKey not configured.");
        _bucket = configuration["Supabase:StorageBucket"] ?? "reports";

        _http.BaseAddress = new Uri(url.TrimEnd('/') + "/storage/v1/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        _http.DefaultRequestHeaders.Add("apikey", serviceKey);
    }

    public async Task UploadAsync(string path, byte[] data, string contentType, CancellationToken cancellationToken = default)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        // Upsert: overwrite if exists
        var request = new HttpRequestMessage(HttpMethod.Post, $"object/{_bucket}/{path}")
        {
            Content = content
        };
        request.Headers.Add("x-upsert", "true");

        var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Supabase Storage upload failed ({Status}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Supabase Storage upload failed: {response.StatusCode}");
        }

        _logger.LogInformation("Uploaded to Supabase Storage: {Bucket}/{Path} ({Size} bytes)", _bucket, path, data.Length);
    }

    public async Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"object/{_bucket}/{path}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Supabase Storage download failed ({Status}): {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"object/{_bucket}/{path}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Supabase Storage delete failed ({Status}): {Body}", response.StatusCode, body);
        }
    }
}
