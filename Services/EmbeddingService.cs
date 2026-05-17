using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SpaApi.Data;

namespace SpaApi.Services;

public record EmbedEvent(string Type, object? Data = null);

public class EmbeddingService
{
  private readonly SpaDbContext _db;
  private readonly GeminiService _gemini;
  private readonly QdrantService _qdrant;
  private readonly ILogger<EmbeddingService> _log;

  public EmbeddingService(
    SpaDbContext db,
    GeminiService gemini,
    QdrantService qdrant,
    ILogger<EmbeddingService> log)
  {
    _db = db;
    _gemini = gemini;
    _qdrant = qdrant;
    _log = log;
  }

  /// <summary>
  /// Stream toàn bộ tiến trình embed 1 tài liệu: cleanup chunks cũ → chunk text → embed từng chunk → upsert.
  /// </summary>
  public async IAsyncEnumerable<EmbedEvent> EmbedTaiLieuStreamAsync(
    Guid id,
    [EnumeratorCancellation] CancellationToken ct = default)
  {
    var tl = await _db.TaiLieus.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (tl is null)
    {
      yield return new EmbedEvent("error", new { message = "Không tìm thấy tài liệu." });
      yield break;
    }

    yield return new EmbedEvent("start", new { id = tl.Id, tieuDe = tl.TieuDe });

    // 1. Xóa chunks cũ (nếu có) khỏi Qdrant
    yield return new EmbedEvent("cleanup", new { message = "Xóa chunks cũ trên Qdrant..." });
    await _qdrant.DeleteTaiLieuChunksAsync(tl.Id);

    // 2. Đánh dấu đang xử lý
    tl.TrangThai = "DangXuLy";
    tl.SoChunk = 0;
    await _db.SaveChangesAsync(ct);

    // 3. Chunk text
    var chunks = ChunkingHelper.ChunkText(tl.NoiDung);
    yield return new EmbedEvent("chunked", new { totalChunks = chunks.Count });

    if (chunks.Count == 0)
    {
      tl.TrangThai = "HoanThanh";
      tl.SoChunk = 0;
      tl.CapNhatLuc = DateTime.UtcNow;
      await _db.SaveChangesAsync(ct);
      yield return new EmbedEvent("done", new { totalChunks = 0, message = "Tài liệu rỗng, không có gì để embed." });
      yield break;
    }

    // 4. Embed từng chunk + report
    var pairs = new List<(string Text, float[] Vector)>();
    for (int i = 0; i < chunks.Count; i++)
    {
      ct.ThrowIfCancellationRequested();

      var (vec, embedError) = await TryEmbedAsync(chunks[i], i, tl.TieuDe);
      if (embedError is not null)
      {
        tl.TrangThai = "Loi";
        await _db.SaveChangesAsync(ct);
        yield return embedError;
        yield break;
      }

      pairs.Add((chunks[i], vec!));

      var preview = chunks[i].Length > 80 ? chunks[i][..80] + "..." : chunks[i];
      yield return new EmbedEvent("chunk", new
      {
        index = i + 1,
        total = chunks.Count,
        preview
      });
    }

    // 5. Upsert lên Qdrant
    yield return new EmbedEvent("upsert", new { message = "Đang lưu vector lên Qdrant..." });
    var upsertError = await TryUpsertAsync(tl.Id, tl.TieuDe, pairs);
    if (upsertError is not null)
    {
      tl.TrangThai = "Loi";
      await _db.SaveChangesAsync(ct);
      yield return upsertError;
      yield break;
    }

    // 6. Cập nhật trạng thái
    tl.TrangThai = "HoanThanh";
    tl.SoChunk = chunks.Count;
    tl.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    yield return new EmbedEvent("done", new { totalChunks = chunks.Count });
  }

  public async Task DeleteTaiLieuChunksAsync(Guid id)
  {
    await _qdrant.DeleteTaiLieuChunksAsync(id);
  }

  // ---- Helpers tách riêng để tránh "yield in catch" (CS1631) ----

  private async Task<(float[]? Vector, EmbedEvent? Error)> TryEmbedAsync(string text, int idx, string? title = null)
  {
    try
    {
      // RETRIEVAL_DOCUMENT cho asymmetric retrieval với query lúc search.
      var vec = await _gemini.EmbedAsync(text, taskType: "RETRIEVAL_DOCUMENT", title: title);
      return (vec, null);
    }
    catch (Exception ex)
    {
      _log.LogError(ex, "Embed chunk {Idx} failed", idx);
      return (null, new EmbedEvent("error", new
      {
        message = $"Lỗi embed chunk {idx + 1}: {ex.Message}",
        atIndex = idx
      }));
    }
  }

  private async Task<EmbedEvent?> TryUpsertAsync(Guid id, string tieuDe, List<(string Text, float[] Vector)> pairs)
  {
    try
    {
      await _qdrant.UpsertTaiLieuChunksAsync(id, tieuDe, pairs);
      return null;
    }
    catch (Exception ex)
    {
      _log.LogError(ex, "Upsert TaiLieu {Id} failed", id);
      return new EmbedEvent("error", new { message = $"Lỗi upsert Qdrant: {ex.Message}" });
    }
  }
}
