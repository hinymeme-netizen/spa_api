using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Services;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/dich-vu")]
public sealed class DichVuController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly IImageStorageService _storage;

  public DichVuController(SpaDbContext db, IImageStorageService storage)
  {
    _db = db;
    _storage = storage;
  }

  // Public: danh sách dịch vụ + bảng giá
  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<List<DichVuResponse>>> List(
    [FromQuery] bool? hienThi,
    [FromQuery] Guid? danhMucId,
    [FromQuery] string? danhMucSlug,
    CancellationToken ct)
  {
    var q = _db.DichVus.AsNoTracking().Include(x => x.DanhMuc).OrderBy(x => x.Ten).AsQueryable();
    if (hienThi is not null) q = q.Where(x => x.HienThi == hienThi);
    if (danhMucId is not null) q = q.Where(x => x.DanhMucId == danhMucId);
    if (!string.IsNullOrWhiteSpace(danhMucSlug))
      q = q.Where(x => x.DanhMuc != null && x.DanhMuc.Slug == danhMucSlug);

    var items = await q
      .Select(x => new DichVuResponse(x.Id, x.Ten, x.MoTa, x.Gia, x.ThoiLuongPhut, x.HienThi,
        x.HinhAnhUrl, x.DanhMucId, x.DanhMuc != null ? x.DanhMuc.Ten : null))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpGet("{id:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<DichVuResponse>> Get(Guid id, CancellationToken ct)
  {
    var x = await _db.DichVus.AsNoTracking().Include(d => d.DanhMuc).FirstOrDefaultAsync(s => s.Id == id, ct);
    if (x is null) return NotFound();
    return Ok(new DichVuResponse(x.Id, x.Ten, x.MoTa, x.Gia, x.ThoiLuongPhut, x.HienThi,
      x.HinhAnhUrl, x.DanhMucId, x.DanhMuc?.Ten));
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
      var url = await _storage.UploadAsync(file, "dich-vu", ct);
      return Ok(new UploadHinhAnhResponse(url));
    }
    catch (Exception ex)
    {
      return StatusCode(500, new { message = ex.Message });
    }
  }

  // Admin CRUD
  [HttpPost]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<DichVuResponse>> Create(UpsertDichVuRequest req, CancellationToken ct)
  {
    var dv = new DichVu
    {
      Id = Guid.NewGuid(),
      Ten = req.Ten.Trim(),
      MoTa = string.IsNullOrWhiteSpace(req.MoTa) ? null : req.MoTa.Trim(),
      Gia = req.Gia,
      ThoiLuongPhut = req.ThoiLuongPhut,
      HienThi = req.HienThi,
      HinhAnhUrl = string.IsNullOrWhiteSpace(req.HinhAnhUrl) ? null : req.HinhAnhUrl,
      DanhMucId = req.DanhMucId,
    };

    _db.DichVus.Add(dv);
    await _db.SaveChangesAsync(ct);

    string? tenDm = null;
    if (dv.DanhMucId.HasValue)
      tenDm = await _db.DanhMucDichVus.Where(d => d.Id == dv.DanhMucId).Select(d => d.Ten).FirstOrDefaultAsync(ct);

    return CreatedAtAction(nameof(Get), new { id = dv.Id },
      new DichVuResponse(dv.Id, dv.Ten, dv.MoTa, dv.Gia, dv.ThoiLuongPhut, dv.HienThi, dv.HinhAnhUrl, dv.DanhMucId, tenDm));
  }

  [HttpPut("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Update(Guid id, UpsertDichVuRequest req, CancellationToken ct)
  {
    var dv = await _db.DichVus.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dv is null) return NotFound();

    dv.Ten = req.Ten.Trim();
    dv.MoTa = string.IsNullOrWhiteSpace(req.MoTa) ? null : req.MoTa.Trim();
    dv.Gia = req.Gia;
    dv.ThoiLuongPhut = req.ThoiLuongPhut;
    dv.HienThi = req.HienThi;
    dv.HinhAnhUrl = string.IsNullOrWhiteSpace(req.HinhAnhUrl) ? null : req.HinhAnhUrl;
    dv.DanhMucId = req.DanhMucId;
    dv.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var dv = await _db.DichVus.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dv is null) return NotFound();

    _db.DichVus.Remove(dv);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }
}
