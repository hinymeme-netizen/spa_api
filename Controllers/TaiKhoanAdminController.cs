using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/admin/tai-khoan")]
[Authorize(Roles = "Admin")]
public sealed class TaiKhoanAdminController : ControllerBase
{
  private readonly SpaDbContext _db;

  public TaiKhoanAdminController(SpaDbContext db)
  {
    _db = db;
  }

  [HttpGet]
  public async Task<ActionResult<List<TaiKhoanAdminResponse>>> List(CancellationToken ct)
  {
    var items = await _db.TaiKhoans.AsNoTracking()
      .OrderByDescending(x => x.NgayTao)
      .Select(x => new TaiKhoanAdminResponse(x.Id, x.Email, x.HoTen, x.SoDienThoai, x.VaiTro.ToString(), x.KichHoat, x.NgayTao))
      .ToListAsync(ct);
    return Ok(items);
  }

  [HttpGet("{id:guid}")]
  public async Task<ActionResult<TaiKhoanAdminResponse>> Get(Guid id, CancellationToken ct)
  {
    var x = await _db.TaiKhoans.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();
    return Ok(new TaiKhoanAdminResponse(x.Id, x.Email, x.HoTen, x.SoDienThoai, x.VaiTro.ToString(), x.KichHoat, x.NgayTao));
  }

  [HttpGet("{id:guid}/lich-hen")]
  public async Task<ActionResult<List<LichHenResponse>>> LichHenCuaKhach(Guid id, CancellationToken ct)
  {
    var exists = await _db.TaiKhoans.AsNoTracking().AnyAsync(t => t.Id == id, ct);
    if (!exists) return NotFound(new { message = "Tài khoản không tồn tại." });

    var items = await _db.LichHens.AsNoTracking()
      .Where(x => x.TaiKhoanId == id)
      .OrderByDescending(x => x.ThoiGianBatDau)
      .Select(x => new LichHenResponse(
        x.Id,
        x.DichVuId,
        x.DichVu.Ten,
        x.NhanVienId,
        x.NhanVien != null ? x.NhanVien.HoTen : null,
        x.ThoiGianBatDau,
        x.ThoiGianKetThuc,
        x.TrangThai.ToString(),
        x.GhiChu,
        x.LyDoTuChoi,
        null,
        null,
        x.DanhGia != null))
      .ToListAsync(ct);

    return Ok(items);
  }

  [HttpPut("{id:guid}")]
  public async Task<ActionResult> Update(Guid id, AdminCapNhatTaiKhoanRequest req, CancellationToken ct)
  {
    var x = await _db.TaiKhoans.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (x is null) return NotFound();

    if (!Enum.TryParse<VaiTroTaiKhoan>(req.VaiTro, ignoreCase: true, out var role))
      return BadRequest(new { message = "VaiTro không hợp lệ (Admin/NhanVien/User)." });

    // Bảo toàn ít nhất 1 Admin đang hoạt động trong hệ thống.
    // Áp dụng khi: (a) target hiện là Admin, (b) hành động này sẽ làm họ KHÔNG còn là Admin
    //   - đổi role sang khác Admin, HOẶC
    //   - khoá tài khoản (KichHoat = false)
    var willLoseAdmin = x.VaiTro == VaiTroTaiKhoan.Admin
                        && (role != VaiTroTaiKhoan.Admin || !req.KichHoat);

    if (willLoseAdmin)
    {
      var otherActiveAdmins = await _db.TaiKhoans
        .CountAsync(t => t.Id != x.Id && t.VaiTro == VaiTroTaiKhoan.Admin && t.KichHoat, ct);
      if (otherActiveAdmins == 0)
        return BadRequest(new
        {
          message = "Đây là Admin đang hoạt động duy nhất. Hệ thống phải có ít nhất 1 Admin — vui lòng cấp quyền Admin cho tài khoản khác trước khi đổi/khoá tài khoản này."
        });
    }

    x.HoTen = req.HoTen.Trim();
    x.SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim();
    x.VaiTro = role;
    x.KichHoat = req.KichHoat;
    x.CapNhatLuc = DateTime.UtcNow;

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }
}
