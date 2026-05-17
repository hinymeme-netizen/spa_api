using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/khuyen-mai")]
public sealed class KhuyenMaiController : ControllerBase
{
  private readonly SpaDbContext _db;

  public KhuyenMaiController(SpaDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<List<KhuyenMaiResponse>>> List([FromQuery] bool? conHieuLuc, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var q = _db.KhuyenMais.AsNoTracking()
      .Include(x => x.DichVu)
      .OrderByDescending(x => x.TuNgay)
      .AsQueryable();
    if (conHieuLuc == true) q = q.Where(x => x.HienThi && x.TuNgay <= now && x.DenNgay >= now);
    var items = await q.Select(x => new KhuyenMaiResponse(
      x.Id, x.Ten, x.MoTa, x.PhanTramGiam, x.SoTienGiam, x.DieuKienToiThieu,
      x.TuNgay, x.DenNgay, x.HienThi,
      x.DichVuId, x.DichVu != null ? x.DichVu.Ten : null)).ToListAsync(ct);
    return Ok(items);
  }

  [HttpPost]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<KhuyenMaiResponse>> Create(UpsertKhuyenMaiRequest req, CancellationToken ct)
  {
    var validation = ValidateKhuyenMai(req);
    if (validation is not null) return BadRequest(new { message = validation });

    if (req.DichVuId is not null)
    {
      var exists = await _db.DichVus.AnyAsync(x => x.Id == req.DichVuId, ct);
      if (!exists) return BadRequest(new { message = "Dịch vụ không tồn tại." });
    }

    var km = new KhuyenMai
    {
      Id = Guid.NewGuid(),
      Ten = req.Ten.Trim(),
      MoTa = string.IsNullOrWhiteSpace(req.MoTa) ? null : req.MoTa.Trim(),
      PhanTramGiam = req.PhanTramGiam,
      SoTienGiam = req.SoTienGiam,
      DieuKienToiThieu = req.DieuKienToiThieu,
      TuNgay = req.TuNgay,
      DenNgay = req.DenNgay,
      HienThi = req.HienThi,
      DichVuId = req.DichVuId
    };
    _db.KhuyenMais.Add(km);
    await _db.SaveChangesAsync(ct);

    string? tenDichVu = null;
    if (km.DichVuId is not null)
      tenDichVu = await _db.DichVus.AsNoTracking()
        .Where(x => x.Id == km.DichVuId)
        .Select(x => x.Ten).FirstOrDefaultAsync(ct);

    return Ok(new KhuyenMaiResponse(km.Id, km.Ten, km.MoTa, km.PhanTramGiam, km.SoTienGiam, km.DieuKienToiThieu,
      km.TuNgay, km.DenNgay, km.HienThi, km.DichVuId, tenDichVu));
  }

  [HttpPut("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Update(Guid id, UpsertKhuyenMaiRequest req, CancellationToken ct)
  {
    var km = await _db.KhuyenMais.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (km is null) return NotFound();

    var validation = ValidateKhuyenMai(req);
    if (validation is not null) return BadRequest(new { message = validation });

    if (req.DichVuId is not null)
    {
      var exists = await _db.DichVus.AnyAsync(x => x.Id == req.DichVuId, ct);
      if (!exists) return BadRequest(new { message = "Dịch vụ không tồn tại." });
    }

    km.Ten = req.Ten.Trim();
    km.MoTa = string.IsNullOrWhiteSpace(req.MoTa) ? null : req.MoTa.Trim();
    km.PhanTramGiam = req.PhanTramGiam;
    km.SoTienGiam = req.SoTienGiam;
    km.DieuKienToiThieu = req.DieuKienToiThieu;
    km.TuNgay = req.TuNgay;
    km.DenNgay = req.DenNgay;
    km.HienThi = req.HienThi;
    km.DichVuId = req.DichVuId;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var km = await _db.KhuyenMais.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (km is null) return NotFound();
    _db.KhuyenMais.Remove(km);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>Trả null nếu hợp lệ, ngược lại trả message lỗi.</summary>
  private static string? ValidateKhuyenMai(UpsertKhuyenMaiRequest req)
  {
    if (req.TuNgay >= req.DenNgay)
      return "Ngày bắt đầu phải trước ngày kết thúc.";

    var hasPhanTram = req.PhanTramGiam.HasValue && req.PhanTramGiam.Value > 0;
    var hasSoTien = req.SoTienGiam.HasValue && req.SoTienGiam.Value > 0;
    if (!hasPhanTram && !hasSoTien)
      return "Phải nhập % giảm hoặc số tiền giảm (lớn hơn 0).";

    if (req.PhanTramGiam.HasValue && (req.PhanTramGiam.Value < 0 || req.PhanTramGiam.Value > 100))
      return "% giảm phải nằm trong khoảng 0–100.";

    if (req.SoTienGiam.HasValue && req.SoTienGiam.Value < 0)
      return "Số tiền giảm không được âm.";

    if (req.DieuKienToiThieu.HasValue && req.DieuKienToiThieu.Value < 0)
      return "Đơn tối thiểu không được âm.";

    return null;
  }
}
