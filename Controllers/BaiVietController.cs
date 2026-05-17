using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Services;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/bai-viet")]
public sealed class BaiVietController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly IImageStorageService _storage;

  public BaiVietController(SpaDbContext db, IImageStorageService storage)
  {
    _db = db;
    _storage = storage;
  }

  [HttpPost("upload-hinh-anh")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<UploadHinhAnhResponse>> UploadHinhAnh(IFormFile file, CancellationToken ct)
  {
    if (file is null || file.Length == 0)
      return BadRequest(new { message = "Không có file được gửi lên." });

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    if (!allowed.Contains(ext))
      return BadRequest(new { message = "Chỉ chấp nhận ảnh jpg, png, webp, gif." });

    if (file.Length > 10 * 1024 * 1024)
      return BadRequest(new { message = "Kích thước ảnh tối đa 10MB." });

    try
    {
      var url = await _storage.UploadAsync(file, "bai-viet", ct);
      return Ok(new UploadHinhAnhResponse(url));
    }
    catch (Exception ex)
    {
      return StatusCode(500, new { message = ex.Message });
    }
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<List<BaiVietResponse>>> GetAll([FromQuery] bool? hienThi, CancellationToken ct)
  {
    var q = _db.BaiViets.AsNoTracking();
    if (hienThi.HasValue) q = q.Where(x => x.HienThi == hienThi.Value);

    var items = await q
      .OrderByDescending(x => x.NgayTao)
      .Select(x => new BaiVietResponse(x.Id, x.TieuDe, x.MoTaNgan, x.NoiDungHtml, x.HinhAnhUrl, x.TacGia, x.NgayTao, x.HienThi))
      .ToListAsync(ct);
      
    return Ok(items);
  }

  [HttpGet("{id:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<BaiVietResponse>> Get(Guid id, CancellationToken ct)
  {
    var x = await _db.BaiViets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();
    return Ok(new BaiVietResponse(x.Id, x.TieuDe, x.MoTaNgan, x.NoiDungHtml, x.HinhAnhUrl, x.TacGia, x.NgayTao, x.HienThi));
  }

  [HttpPost]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<BaiVietResponse>> Create(UpsertBaiVietRequest req, CancellationToken ct)
  {
    var x = new BaiViet
    {
      Id = Guid.NewGuid(),
      TieuDe = req.TieuDe.Trim(),
      MoTaNgan = req.MoTaNgan.Trim(),
      NoiDungHtml = req.NoiDungHtml,
      HinhAnhUrl = req.HinhAnhUrl,
      TacGia = req.TacGia,
      NgayTao = DateTime.UtcNow,
      HienThi = req.HienThi
    };
    _db.BaiViets.Add(x);
    await _db.SaveChangesAsync(ct);
    var res = new BaiVietResponse(x.Id, x.TieuDe, x.MoTaNgan, x.NoiDungHtml, x.HinhAnhUrl, x.TacGia, x.NgayTao, x.HienThi);
    return CreatedAtAction(nameof(Get), new { id = x.Id }, res);
  }

  [HttpPut("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Update(Guid id, UpsertBaiVietRequest req, CancellationToken ct)
  {
    var x = await _db.BaiViets.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();

    x.TieuDe = req.TieuDe.Trim();
    x.MoTaNgan = req.MoTaNgan.Trim();
    x.NoiDungHtml = req.NoiDungHtml;
    x.HinhAnhUrl = req.HinhAnhUrl;
    x.TacGia = req.TacGia;
    x.HienThi = req.HienThi;

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var x = await _db.BaiViets.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();

    _db.BaiViets.Remove(x);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }
}
