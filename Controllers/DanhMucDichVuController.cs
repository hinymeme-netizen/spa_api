using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/danh-muc-dich-vu")]
public sealed class DanhMucDichVuController : ControllerBase
{
  private readonly SpaDbContext _db;
  public DanhMucDichVuController(SpaDbContext db) => _db = db;

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<List<DanhMucDichVuResponse>>> List(
    [FromQuery] bool? hienThi, CancellationToken ct)
  {
    var q = _db.DanhMucDichVus.AsNoTracking().AsQueryable();
    if (hienThi.HasValue) q = q.Where(x => x.HienThi == hienThi.Value);

    var items = await q
      .OrderBy(x => x.ThuTu).ThenBy(x => x.Ten)
      .Select(x => new DanhMucDichVuResponse(
        x.Id, x.Ten, x.Slug, x.MoTa, x.Icon, x.ThuTu, x.HienThi,
        _db.DichVus.Count(d => d.DanhMucId == x.Id)))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpGet("{id:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<DanhMucDichVuResponse>> Detail(Guid id, CancellationToken ct)
  {
    var x = await _db.DanhMucDichVus.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
    if (x is null) return NotFound();
    var soDichVu = await _db.DichVus.CountAsync(d => d.DanhMucId == id, ct);
    return Ok(new DanhMucDichVuResponse(x.Id, x.Ten, x.Slug, x.MoTa, x.Icon, x.ThuTu, x.HienThi, soDichVu));
  }

  [HttpPost]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<DanhMucDichVuResponse>> Create(UpsertDanhMucDichVuRequest req, CancellationToken ct)
  {
    var slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Ten) : req.Slug.Trim();
    if (await _db.DanhMucDichVus.AnyAsync(x => x.Slug == slug, ct))
      return BadRequest(new { message = $"Slug '{slug}' đã tồn tại." });

    var dm = new DanhMucDichVu
    {
      Id = Guid.NewGuid(),
      Ten = req.Ten.Trim(),
      Slug = slug,
      MoTa = req.MoTa?.Trim(),
      Icon = req.Icon?.Trim(),
      ThuTu = req.ThuTu,
      HienThi = req.HienThi,
      NgayTao = DateTime.UtcNow,
    };
    _db.DanhMucDichVus.Add(dm);
    await _db.SaveChangesAsync(ct);

    return Ok(new DanhMucDichVuResponse(dm.Id, dm.Ten, dm.Slug, dm.MoTa, dm.Icon, dm.ThuTu, dm.HienThi, 0));
  }

  [HttpPut("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<DanhMucDichVuResponse>> Update(Guid id, UpsertDanhMucDichVuRequest req, CancellationToken ct)
  {
    var dm = await _db.DanhMucDichVus.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dm is null) return NotFound();

    var slug = string.IsNullOrWhiteSpace(req.Slug) ? Slugify(req.Ten) : req.Slug.Trim();
    if (slug != dm.Slug && await _db.DanhMucDichVus.AnyAsync(x => x.Slug == slug && x.Id != id, ct))
      return BadRequest(new { message = $"Slug '{slug}' đã tồn tại." });

    dm.Ten = req.Ten.Trim();
    dm.Slug = slug;
    dm.MoTa = req.MoTa?.Trim();
    dm.Icon = req.Icon?.Trim();
    dm.ThuTu = req.ThuTu;
    dm.HienThi = req.HienThi;

    await _db.SaveChangesAsync(ct);
    var soDichVu = await _db.DichVus.CountAsync(d => d.DanhMucId == id, ct);
    return Ok(new DanhMucDichVuResponse(dm.Id, dm.Ten, dm.Slug, dm.MoTa, dm.Icon, dm.ThuTu, dm.HienThi, soDichVu));
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var dm = await _db.DanhMucDichVus.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dm is null) return NotFound();

    // Bỏ liên kết các dịch vụ trước khi xoá để FK SET NULL
    var dvList = await _db.DichVus.Where(d => d.DanhMucId == id).ToListAsync(ct);
    foreach (var d in dvList) d.DanhMucId = null;

    _db.DanhMucDichVus.Remove(dm);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  private static string Slugify(string s)
  {
    var t = s.Trim().ToLowerInvariant();
    var sb = new System.Text.StringBuilder(t.Length);
    foreach (var c in t.Normalize(System.Text.NormalizationForm.FormD))
    {
      var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
      if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
      if (char.IsLetterOrDigit(c)) sb.Append(c);
      else if (char.IsWhiteSpace(c) || c == '-' || c == '_') sb.Append('-');
    }
    var slug = sb.ToString();
    while (slug.Contains("--")) slug = slug.Replace("--", "-");
    return slug.Trim('-');
  }
}
