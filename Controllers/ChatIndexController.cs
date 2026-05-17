using Microsoft.AspNetCore.Mvc;
using SpaApi.Data;
using SpaApi.Services;

namespace SpaApi.Controllers;

/// <summary>
/// POST /api/chat/index  — re-index dữ liệu DichVu vào Qdrant (mặc định).
/// GET  /api/chat/index  — health-check.
///
/// Dữ liệu tài liệu free-text được quản qua /admin/tai-lieu (CRUD + embed riêng từng tài liệu).
/// Endpoint này chỉ phụ trách re-sync DichVu (giá, mô tả) lên vector store.
/// </summary>
[ApiController]
[Route("api/chat/index")]
public class ChatIndexController : ControllerBase
{
    private readonly GeminiService _gemini;
    private readonly QdrantService _qdrant;
    private readonly SpaDbContext _db;
    private readonly ILogger<ChatIndexController> _log;

    public ChatIndexController(GeminiService gemini, QdrantService qdrant, SpaDbContext db, ILogger<ChatIndexController> log)
    {
        _gemini = gemini;
        _qdrant = qdrant;
        _db = db;
        _log = log;
    }

    [HttpGet]
    public IActionResult HealthCheck() => Ok(new { status = "ok", message = "POST /api/chat/index để re-index DichVu." });

    [HttpPost]
    public async Task<IActionResult> Index([FromQuery] bool rebuild = false)
    {
        if (rebuild)
            await _qdrant.DeleteCollectionAsync();

        var docs = new List<KnowledgeDoc>();

        try
        {
            var services = _db.DichVus.Where(d => d.HienThi).ToList();
            foreach (var svc in services)
            {
                var text = string.Join("\n", new[]
                {
                    $"Dịch vụ: {svc.Ten}",
                    !string.IsNullOrEmpty(svc.MoTa) ? $"Mô tả: {svc.MoTa}" : null,
                    $"Giá: {svc.Gia:N0}đ",
                    $"Thời lượng: {svc.ThoiLuongPhut} phút"
                }.OfType<string>());
                docs.Add(new KnowledgeDoc($"service-{svc.Id}", text, "service", svc.Ten));
            }
            _log.LogInformation("Prepared {Count} service docs", services.Count);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to index services"); }

        if (docs.Count == 0)
            return Ok(new { message = "Không có dịch vụ để index." });

        var pairs = new List<(KnowledgeDoc Doc, float[] Vector)>();
        const int batchSize = 20;
        for (int i = 0; i < docs.Count; i += batchSize)
        {
            var batch = docs.Skip(i).Take(batchSize).ToList();
            // RETRIEVAL_DOCUMENT để bot tìm chính xác hơn khi user query.
            var embeddings = await Task.WhenAll(batch.Select(d =>
                _gemini.EmbedAsync(d.Text, taskType: "RETRIEVAL_DOCUMENT", title: d.Title)));
            pairs.AddRange(batch.Zip(embeddings, (d, v) => (d, v)));
        }

        await _qdrant.UpsertAsync(pairs);

        return Ok(new
        {
            message = $"Đã index {docs.Count} dịch vụ.",
            services = docs.Count
        });
    }
}
