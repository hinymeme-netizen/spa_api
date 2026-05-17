using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Services;

namespace SpaApi.Controllers;

internal static class TaiLieuLimits
{
  public const int MaxNoiDungLength = 200_000;
  public const int MinNoiDungLength = 30;
}

[ApiController]
[Route("api/tai-lieu")]
[Authorize(Roles = "Admin")]
public sealed class TaiLieuController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly EmbeddingService _embedder;

  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public TaiLieuController(SpaDbContext db, EmbeddingService embedder)
  {
    _db = db;
    _embedder = embedder;
  }

  [HttpGet]
  public async Task<ActionResult<List<TaiLieuListItemResponse>>> List(CancellationToken ct)
  {
    var items = await _db.TaiLieus.AsNoTracking()
      .OrderByDescending(x => x.NgayTao)
      .Select(x => new TaiLieuListItemResponse(
        x.Id, x.TieuDe, x.Nguon, x.SoChunk, x.TrangThai,
        x.NoiDung.Length, x.NgayTao, x.CapNhatLuc))
      .ToListAsync(ct);
    return Ok(items);
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<TaiLieuResponse>> Get(Guid id, CancellationToken ct)
  {
    var x = await _db.TaiLieus.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();
    return Ok(new TaiLieuResponse(x.Id, x.TieuDe, x.NoiDung, x.Nguon, x.SoChunk, x.TrangThai, x.NgayTao, x.CapNhatLuc));
  }

  [HttpPost]
  public async Task<ActionResult<TaiLieuResponse>> Create(UpsertTaiLieuRequest req, CancellationToken ct)
  {
    var validation = ValidateTaiLieu(req);
    if (validation is not null) return BadRequest(new { message = validation });

    var tl = new TaiLieu
    {
      Id = Guid.NewGuid(),
      TieuDe = req.TieuDe.Trim(),
      NoiDung = req.NoiDung,
      Nguon = string.IsNullOrWhiteSpace(req.Nguon) ? null : req.Nguon.Trim(),
      TrangThai = "ChoXuLy",
      SoChunk = 0,
      NgayTao = DateTime.UtcNow
    };
    _db.TaiLieus.Add(tl);
    await _db.SaveChangesAsync(ct);

    return CreatedAtAction(nameof(Get), new { id = tl.Id },
      new TaiLieuResponse(tl.Id, tl.TieuDe, tl.NoiDung, tl.Nguon, tl.SoChunk, tl.TrangThai, tl.NgayTao, tl.CapNhatLuc));
  }

  [HttpPut("{id:guid}")]
  public async Task<ActionResult> Update(Guid id, UpsertTaiLieuRequest req, CancellationToken ct)
  {
    var tl = await _db.TaiLieus.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (tl is null) return NotFound();

    var validation = ValidateTaiLieu(req);
    if (validation is not null) return BadRequest(new { message = validation });

    var contentChanged = tl.NoiDung != req.NoiDung;

    tl.TieuDe = req.TieuDe.Trim();
    tl.NoiDung = req.NoiDung;
    tl.Nguon = string.IsNullOrWhiteSpace(req.Nguon) ? null : req.Nguon.Trim();
    tl.CapNhatLuc = DateTime.UtcNow;

    // Nếu nội dung thay đổi → reset trạng thái để admin re-embed
    if (contentChanged)
    {
      tl.TrangThai = "ChoXuLy";
      tl.SoChunk = 0;
      // Xóa chunks cũ ngay (Qdrant) — không cần đợi re-embed
      await _embedder.DeleteTaiLieuChunksAsync(tl.Id);
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpDelete("{id:guid}")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var tl = await _db.TaiLieus.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (tl is null) return NotFound();

    // Xóa chunks Qdrant trước
    await _embedder.DeleteTaiLieuChunksAsync(tl.Id);

    _db.TaiLieus.Remove(tl);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// Stream tiến trình embed dạng Server-Sent Events.
  /// FE dùng `fetch` + ReadableStream để giữ Authorization header.
  /// </summary>
  [HttpPost("{id:guid}/embed")]
  public async Task EmbedStream(Guid id, CancellationToken ct)
  {
    Response.Headers["Content-Type"] = "text/event-stream";
    Response.Headers["Cache-Control"] = "no-cache, no-transform";
    Response.Headers["X-Accel-Buffering"] = "no"; // tắt buffering proxy

    await foreach (var ev in _embedder.EmbedTaiLieuStreamAsync(id, ct))
    {
      var json = JsonSerializer.Serialize(ev, JsonOpts);
      await Response.WriteAsync($"data: {json}\n\n", ct);
      await Response.Body.FlushAsync(ct);
    }
  }

  private static string? ValidateTaiLieu(UpsertTaiLieuRequest req)
  {
    if (string.IsNullOrWhiteSpace(req.TieuDe))
      return "Tiêu đề tài liệu không được để trống.";
    if (string.IsNullOrWhiteSpace(req.NoiDung))
      return "Nội dung tài liệu không được để trống.";
    if (req.NoiDung.Length < TaiLieuLimits.MinNoiDungLength)
      return $"Nội dung phải có ít nhất {TaiLieuLimits.MinNoiDungLength} ký tự để embed có ý nghĩa.";
    if (req.NoiDung.Length > TaiLieuLimits.MaxNoiDungLength)
      return $"Nội dung quá dài (tối đa {TaiLieuLimits.MaxNoiDungLength:N0} ký tự, hiện tại {req.NoiDung.Length:N0}).";
    return null;
  }
}
