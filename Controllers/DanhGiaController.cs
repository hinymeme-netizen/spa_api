using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/danh-gia")]
public sealed class DanhGiaController : ControllerBase
{
  private readonly SpaDbContext _db;

  public DanhGiaController(SpaDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<List<DanhGiaResponse>>> GetAll(CancellationToken ct)
  {
    var items = await _db.DanhGias.AsNoTracking()
      .OrderByDescending(x => x.NgayTao)
      .Include(x => x.DichVu)
      .Include(x => x.TaiKhoan)
      .Select(x => new DanhGiaResponse(
        x.Id,
        x.LichHenId,
        x.DichVuId,
        x.DichVu.Ten,
        x.TaiKhoanId,
        x.TaiKhoan.HoTen,
        x.SoSao,
        x.NoiDung,
        x.NgayTao))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var dg = await _db.DanhGias.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dg is null) return NotFound();
    _db.DanhGias.Remove(dg);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpGet("thong-ke-dich-vu")]
  [AllowAnonymous]
  public async Task<ActionResult<List<ThongKeDichVuItem>>> ThongKeDichVu(CancellationToken ct)
  {
    var data = await _db.DanhGias.AsNoTracking()
      .GroupBy(x => x.DichVuId)
      .Select(g => new ThongKeDichVuItem(
        g.Key,
        g.Count(),
        Math.Round(g.Average(x => (double)x.SoSao), 2)
      ))
      .ToListAsync(ct);
    return Ok(data);
  }

  public sealed record ThongKeDichVuItem(Guid DichVuId, int SoLuong, double TrungBinh);

  [HttpGet("noi-bat")]
  [AllowAnonymous]
  public async Task<ActionResult<List<DanhGiaResponse>>> NoiBat(
    [FromQuery] int top = 6,
    CancellationToken ct = default)
  {
    top = Math.Clamp(top, 1, 20);

    var items = await _db.DanhGias.AsNoTracking()
      .Where(x => x.NoiDung != null && x.NoiDung != "")
      .OrderByDescending(x => x.SoSao)
      .ThenByDescending(x => x.NgayTao)
      .Take(top)
      .Include(x => x.DichVu)
      .Include(x => x.TaiKhoan)
      .Select(x => new DanhGiaResponse(
        x.Id,
        x.LichHenId,
        x.DichVuId,
        x.DichVu.Ten,
        x.TaiKhoanId,
        x.TaiKhoan.HoTen,
        x.SoSao,
        x.NoiDung,
        x.NgayTao))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpGet("dich-vu/{dichVuId:guid}")]
  [AllowAnonymous]
  public async Task<ActionResult<List<DanhGiaResponse>>> ListByDichVu(Guid dichVuId, CancellationToken ct)
  {
    var items = await _db.DanhGias.AsNoTracking()
      .Where(x => x.DichVuId == dichVuId)
      .OrderByDescending(x => x.NgayTao)
      .Include(x => x.DichVu)
      .Include(x => x.TaiKhoan)
      .Select(x => new DanhGiaResponse(
        x.Id,
        x.LichHenId,
        x.DichVuId,
        x.DichVu.Ten,
        x.TaiKhoanId,
        x.TaiKhoan.HoTen,
        x.SoSao,
        x.NoiDung,
        x.NgayTao))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpPost]
  [Authorize]
  public async Task<ActionResult<DanhGiaResponse>> Tao(TaoDanhGiaRequest req, CancellationToken ct)
  {
    var userId = GetUserId();

    var lich = await _db.LichHens
      .Include(x => x.DichVu)
      .FirstOrDefaultAsync(x => x.Id == req.LichHenId && x.TaiKhoanId == userId, ct);

    if (lich is null) return NotFound(new { message = "Lịch hẹn không tồn tại." });
    if (lich.TrangThai != TrangThaiLichHen.HoanThanh) return BadRequest(new { message = "Chỉ đánh giá khi lịch đã hoàn thành." });

    var existed = await _db.DanhGias.AnyAsync(x => x.LichHenId == lich.Id, ct);
    if (existed) return Conflict(new { message = "Lịch hẹn này đã được đánh giá." });

    var tk = await _db.TaiKhoans.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
    var dg = new DanhGia
    {
      Id = Guid.NewGuid(),
      LichHenId = lich.Id,
      TaiKhoanId = userId,
      DichVuId = lich.DichVuId,
      SoSao = req.SoSao,
      NoiDung = string.IsNullOrWhiteSpace(req.NoiDung) ? null : req.NoiDung.Trim()
    };
    _db.DanhGias.Add(dg);
    await _db.SaveChangesAsync(ct);

    return Ok(new DanhGiaResponse(dg.Id, dg.LichHenId, dg.DichVuId, lich.DichVu.Ten, dg.TaiKhoanId, tk.HoTen, dg.SoSao, dg.NoiDung, dg.NgayTao));
  }

  private Guid GetUserId()
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.Parse(raw!);
  }
}

