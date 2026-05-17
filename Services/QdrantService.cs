using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SpaApi.Settings;

namespace SpaApi.Services;

public record KnowledgeDoc(string Id, string Text, string Source, string Title);
public record RetrievedDoc(string Text, string Source, string Title, float Score);

/// <summary>
/// Qdrant vector store via REST API.
/// Uses HttpClient directly to avoid gRPC SDK version compatibility issues.
/// </summary>
public class QdrantService
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;
    private readonly ILogger<QdrantService> _log;

    public QdrantService(IHttpClientFactory factory, IOptions<ChatOptions> opts, ILogger<QdrantService> log)
    {
        _opts = opts.Value;
        _log = log;
        _http = factory.CreateClient();
        _http.BaseAddress = new Uri(_opts.QdrantUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_opts.QdrantApiKey))
            _http.DefaultRequestHeaders.Add("api-key", _opts.QdrantApiKey);
    }

    public async Task EnsureCollectionAsync()
    {
        try
        {
            var check = await _http.GetAsync($"collections/{_opts.CollectionName}");
            if (check.IsSuccessStatusCode)
            {
                // Collection tồn tại — kiểm tra dim có khớp với cấu hình không
                var existingDim = await GetCollectionDimAsync(check);
                if (existingDim is not null && existingDim != _opts.EmbeddingDim)
                {
                    _log.LogWarning(
                        "Qdrant collection {Name} có dim={Existing} nhưng app cấu hình dim={Configured}. " +
                        "Auto-recreate collection để tránh upsert/search fail.",
                        _opts.CollectionName, existingDim, _opts.EmbeddingDim);

                    await _http.DeleteAsync($"collections/{_opts.CollectionName}");
                    await CreateCollectionAsync();
                }
                return;
            }

            await CreateCollectionAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to ensure Qdrant collection");
        }
    }

    private async Task CreateCollectionAsync()
    {
        var body = new
        {
            vectors = new
            {
                size = _opts.EmbeddingDim,
                distance = "Cosine"
            }
        };
        var res = await _http.PutAsJsonAsync($"collections/{_opts.CollectionName}", body);
        if (res.IsSuccessStatusCode)
            _log.LogInformation("Created Qdrant collection: {Name} (dim={Dim})", _opts.CollectionName, _opts.EmbeddingDim);
        else
        {
            var err = await res.Content.ReadAsStringAsync();
            _log.LogError("Failed to create collection {Name}: {Error}", _opts.CollectionName, err);
        }
    }

    private static async Task<int?> GetCollectionDimAsync(HttpResponseMessage check)
    {
        try
        {
            var json = await check.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            // GET /collections/{name} → { result: { config: { params: { vectors: { size, distance } } } } }
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("config", out var config)) return null;
            if (!config.TryGetProperty("params", out var p)) return null;
            if (!p.TryGetProperty("vectors", out var vectors)) return null;
            if (vectors.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
                return size.GetInt32();
        }
        catch { /* non-fatal */ }
        return null;
    }

    public async Task UpsertAsync(IEnumerable<(KnowledgeDoc Doc, float[] Vector)> items)
    {
        await EnsureCollectionAsync();

        var points = items.Select(item => new
        {
            id = StringToUuid(item.Doc.Id),
            vector = item.Vector,
            payload = new
            {
                text = item.Doc.Text,
                source = item.Doc.Source,
                title = item.Doc.Title
            }
        }).ToList();

        var body = new { points };
        var res = await _http.PutAsJsonAsync($"collections/{_opts.CollectionName}/points", body);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            _log.LogError("Qdrant upsert failed: {Error}", err);
        }
        else
        {
            _log.LogInformation("Upserted {Count} points to Qdrant", points.Count);
        }
    }

    public async Task<List<RetrievedDoc>> SearchAsync(float[] queryVector, int topK = 10)
    {
        try
        {
            var body = new
            {
                vector = queryVector,
                limit = topK,
                with_payload = true
            };
            var res = await _http.PostAsJsonAsync(
                $"collections/{_opts.CollectionName}/points/query", body);

            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync();
                _log.LogWarning("Qdrant search returned {Status}: {Body}", res.StatusCode, errBody);
                return [];
            }

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var results = new List<RetrievedDoc>();
            if (!doc.RootElement.TryGetProperty("result", out var resultEl)) return [];
            if (!resultEl.TryGetProperty("points", out var points)) return [];

            foreach (var point in points.EnumerateArray())
            {
                var score = point.TryGetProperty("score", out var s) ? s.GetSingle() : 0f;
                // Không filter theo threshold: Qdrant đã trả top theo similarity giảm dần.
                // Threshold cứng dễ loại chunks liên quan khi model embedding khác calibration cosine khác nhau.
                var payload = point.TryGetProperty("payload", out var p) ? p : default;
                results.Add(new RetrievedDoc(
                    Text: GetStr(payload, "text"),
                    Source: GetStr(payload, "source"),
                    Title: GetStr(payload, "title"),
                    Score: score));
            }
            return results;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Qdrant search failed (non-fatal)");
            return [];
        }
    }

    public async Task DeleteCollectionAsync()
    {
        try { await _http.DeleteAsync($"collections/{_opts.CollectionName}"); }
        catch { /* ignore */ }
    }

    // ---- TaiLieu chunks ----

    /// <summary>
    /// Upsert nhiều chunk của 1 tài liệu. Mỗi point chứa payload `tailieuId` để có thể xóa theo filter.
    /// </summary>
    public async Task UpsertTaiLieuChunksAsync(
        Guid taiLieuId, string tieuDe, IEnumerable<(string Text, float[] Vector)> chunks)
    {
        await EnsureCollectionAsync();

        var idx = 0;
        var points = chunks.Select(c => new
        {
            id = StringToUuid($"tailieu-{taiLieuId}-{idx++}"),
            vector = c.Vector,
            payload = new
            {
                text = c.Text,
                source = "tailieu",
                title = tieuDe,
                tailieuId = taiLieuId.ToString()
            }
        }).ToList();

        if (points.Count == 0) return;

        var body = new { points };
        var res = await _http.PutAsJsonAsync($"collections/{_opts.CollectionName}/points", body);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            _log.LogError("Qdrant upsert TaiLieu chunks failed: {Error}", err);
            throw new Exception($"Qdrant upsert error: {err}");
        }
        _log.LogInformation("Upserted {Count} chunks for TaiLieu {Id}", points.Count, taiLieuId);
    }

    /// <summary>
    /// Xóa toàn bộ chunks của 1 tài liệu khỏi Qdrant qua filter payload.tailieuId.
    /// </summary>
    public async Task DeleteTaiLieuChunksAsync(Guid taiLieuId)
    {
        try
        {
            var body = new
            {
                filter = new
                {
                    must = new[]
                    {
                        new { key = "tailieuId", match = new { value = taiLieuId.ToString() } }
                    }
                }
            };
            var res = await _http.PostAsJsonAsync(
                $"collections/{_opts.CollectionName}/points/delete?wait=true", body);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                _log.LogWarning("Qdrant delete TaiLieu chunks returned {Status}: {Body}",
                    res.StatusCode, err);
            }
            else
            {
                _log.LogInformation("Deleted Qdrant chunks for TaiLieu {Id}", taiLieuId);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Qdrant delete chunks failed (non-fatal)");
        }
    }

    private static string GetStr(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string StringToUuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash).ToString();
    }
}
