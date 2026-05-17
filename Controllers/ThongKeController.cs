using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/thong-ke")]
public sealed class ThongKeController : ControllerBase
{
  private readonly SpaDbContext _db;

  public ThongKeController(SpaDbContext db)
  {
    _db = db;
  }

  [HttpGet("tong-quan")]
  [AllowAnonymous]
  public async Task<ActionResult<TongQuanResponse>> TongQuan(CancellationToken ct)
  {
    var soKhach = await _db.TaiKhoans.CountAsync(x => x.VaiTro == SpaApi.Domain.VaiTroTaiKhoan.User, ct);
    var soNhanVien = await _db.NhanViens.CountAsync(x => x.DangLamViec, ct);
    var soDichVu = await _db.DichVus.CountAsync(x => x.HienThi, ct);
    var soLich = await _db.LichHens.CountAsync(x => x.TrangThai == SpaApi.Domain.TrangThaiLichHen.HoanThanh, ct);
    var soDanhGia = await _db.DanhGias.CountAsync(ct);
    var diemTrungBinh = soDanhGia > 0
      ? Math.Round(await _db.DanhGias.AverageAsync(x => (double)x.SoSao, ct), 1)
      : 0d;
    return Ok(new TongQuanResponse(soKhach, soNhanVien, soDichVu, soLich, soDanhGia, diemTrungBinh));
  }

  [HttpGet("lich-hen")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<List<ThongKeSoLuongTheoNgayResponse>>> LichHenTheoNgay(
    [FromQuery] DateTime? tuNgay,
    [FromQuery] DateTime? denNgay,
    CancellationToken ct)
  {
    var (from, to) = ResolveRange(tuNgay, denNgay);

    // Pomelo MySQL không translate được GroupBy(DateOnly.FromDateTime(...)) → fallback sang client-side
    var raw = await _db.LichHens.AsNoTracking()
      .Where(x => x.ThoiGianBatDau >= from && x.ThoiGianBatDau <= to)
      .Where(x => x.TrangThai != TrangThaiLichHen.DaHuy && x.TrangThai != TrangThaiLichHen.TuChoi)
      .Select(x => x.ThoiGianBatDau)
      .ToListAsync(ct);

    var data = raw
      .GroupBy(t => DateOnly.FromDateTime(t))
      .Select(g => new ThongKeSoLuongTheoNgayResponse(g.Key, g.Count()))
      .OrderBy(x => x.Ngay)
      .ToList();

    return Ok(data);
  }

  [HttpGet("doanh-thu")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<ThongKeDoanhThuResponse>> DoanhThu(
    [FromQuery] DateTime? tuNgay,
    [FromQuery] DateTime? denNgay,
    CancellationToken ct)
  {
    var (from, to) = ResolveRange(tuNgay, denNgay);

    // Doanh thu từ lịch hẹn đã hoàn thành (giá dịch vụ tại thời điểm hoàn thành)
    var doanhThuDichVu = await _db.LichHens.AsNoTracking()
      .Where(x => x.ThoiGianBatDau >= from && x.ThoiGianBatDau <= to)
      .Where(x => x.TrangThai == TrangThaiLichHen.HoanThanh)
      .Join(_db.DichVus.AsNoTracking(), lh => lh.DichVuId, dv => dv.Id, (_, dv) => dv.Gia)
      .SumAsync(ct);

    // Doanh thu từ đơn hàng đã hoàn thành
    var doanhThuSanPham = await _db.DonHangs.AsNoTracking()
      .Where(x => x.NgayTao >= from && x.NgayTao <= to)
      .Where(x => x.TrangThai == TrangThaiDonHang.HoanThanh)
      .SumAsync(x => x.TongTien, ct);

    return Ok(new ThongKeDoanhThuResponse(doanhThuDichVu, doanhThuDichVu + doanhThuSanPham));
  }

  /// <summary>
  /// Default: 30 ngày qua → bây giờ. Nếu chỉ pass tuNgay → denNgay = now.
  /// FE thường gửi ISO 8601 với suffix Z (UTC). DB lưu local time → cần convert UTC→Local trước khi so sánh
  /// để khớp với NhanVienDashboard (dùng DateTime.Now local).
  /// </summary>
  private static (DateTime from, DateTime to) ResolveRange(DateTime? tuNgay, DateTime? denNgay)
  {
    var now = DateTime.Now;
    var to = denNgay ?? now;
    var from = tuNgay ?? to.AddDays(-30);

    if (to.Kind == DateTimeKind.Utc) to = to.ToLocalTime();
    if (from.Kind == DateTimeKind.Utc) from = from.ToLocalTime();

    if (from > to) (from, to) = (to, from);
    return (from, to);
  }

  // ---- Dashboard cho Nhân viên: chỉ thông số của chính NV đó ----

  [HttpGet("nhan-vien/me/tong-quan")]
  [Authorize(Roles = "NhanVien,Admin")]
  public async Task<ActionResult<NhanVienDashboardResponse>> NhanVienDashboard(CancellationToken ct)
  {
    var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(idStr, out var taiKhoanId))
      return BadRequest(new { message = "Token không hợp lệ." });

    var nv = await _db.NhanViens.AsNoTracking()
      .FirstOrDefaultAsync(x => x.TaiKhoanId == taiKhoanId, ct);
    if (nv is null)
      return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào." });

    var nvId = nv.Id;
    var now = DateTime.Now;
    var thirtyDaysAgo = now.AddDays(-30);

    var lichHensQ = _db.LichHens.AsNoTracking().Where(x => x.NhanVienId == nvId);

    var soLichHomNay = await lichHensQ
      .CountAsync(x =>
        x.ThoiGianBatDau >= now.Date &&
        x.ThoiGianBatDau < now.Date.AddDays(1) &&
        x.TrangThai != TrangThaiLichHen.DaHuy &&
        x.TrangThai != TrangThaiLichHen.TuChoi, ct);

    var soLichSapToi = await lichHensQ
      .CountAsync(x =>
        x.ThoiGianBatDau >= now &&
        (x.TrangThai == TrangThaiLichHen.ChoXacNhan || x.TrangThai == TrangThaiLichHen.DaXacNhan), ct);

    var soLichHoanThanh30Ngay = await lichHensQ
      .CountAsync(x =>
        x.TrangThai == TrangThaiLichHen.HoanThanh &&
        x.ThoiGianBatDau >= thirtyDaysAgo, ct);

    var doanhThu30Ngay = await _db.LichHens.AsNoTracking()
      .Where(x => x.NhanVienId == nvId)
      .Where(x => x.TrangThai == TrangThaiLichHen.HoanThanh)
      .Where(x => x.ThoiGianBatDau >= thirtyDaysAgo)
      .Join(_db.DichVus.AsNoTracking(), lh => lh.DichVuId, dv => dv.Id, (_, dv) => dv.Gia)
      .SumAsync(ct);

    // Đánh giá: của các lịch mà NV này phụ trách
    var reviews = await (from dg in _db.DanhGias.AsNoTracking()
                         join lh in _db.LichHens.AsNoTracking() on dg.LichHenId equals lh.Id
                         where lh.NhanVienId == nvId
                         select dg.SoSao).ToListAsync(ct);
    var soDanhGia = reviews.Count;
    var diemTrungBinh = reviews.Count == 0 ? 0d : Math.Round(reviews.Average(x => (double)x), 1);

    // Biểu đồ lịch theo ngày 30 ngày qua — group client-side (Pomelo không translate được DateOnly.FromDateTime)
    var rawTimes = await lichHensQ
      .Where(x => x.ThoiGianBatDau >= thirtyDaysAgo && x.ThoiGianBatDau <= now)
      .Where(x => x.TrangThai != TrangThaiLichHen.DaHuy && x.TrangThai != TrangThaiLichHen.TuChoi)
      .Select(x => x.ThoiGianBatDau)
      .ToListAsync(ct);
    var bieuDoLich = rawTimes
      .GroupBy(t => DateOnly.FromDateTime(t))
      .Select(g => new ThongKeSoLuongTheoNgayResponse(g.Key, g.Count()))
      .OrderBy(x => x.Ngay)
      .ToList();

    // Top dịch vụ NV này phụ trách nhiều nhất (30 ngày) — aggregate client-side
    var nvRawIds = await _db.LichHens.AsNoTracking()
      .Where(x => x.NhanVienId == nvId)
      .Where(x => x.TrangThai == TrangThaiLichHen.HoanThanh)
      .Where(x => x.ThoiGianBatDau >= thirtyDaysAgo)
      .Select(x => x.DichVuId)
      .ToListAsync(ct);

    var nvCounts = nvRawIds
      .GroupBy(id => id)
      .Select(g => new { DichVuId = g.Key, SoLan = g.Count() })
      .OrderByDescending(x => x.SoLan)
      .Take(5)
      .ToList();

    List<ThongKeTopDichVuResponse> topDichVu;
    if (nvCounts.Count == 0)
    {
      topDichVu = new List<ThongKeTopDichVuResponse>();
    }
    else
    {
      var nvTopIds = nvCounts.Select(c => c.DichVuId).ToList();
      var nvServices = await _db.DichVus.AsNoTracking()
        .Where(d => nvTopIds.Contains(d.Id))
        .Select(d => new { d.Id, d.Ten, d.Gia })
        .ToListAsync(ct);

      topDichVu = nvCounts
        .Join(nvServices, c => c.DichVuId, s => s.Id,
          (c, s) => new ThongKeTopDichVuResponse(s.Id, s.Ten, c.SoLan, s.Gia * c.SoLan))
        .OrderByDescending(x => x.SoLan)
        .ThenByDescending(x => x.DoanhThuUocTinh)
        .ToList();
    }

    return Ok(new NhanVienDashboardResponse(
      nv.Id,
      nv.HoTen,
      soLichHomNay,
      soLichSapToi,
      soLichHoanThanh30Ngay,
      doanhThu30Ngay,
      soDanhGia,
      diemTrungBinh,
      bieuDoLich,
      topDichVu));
  }

  [HttpGet("top-dich-vu")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<List<ThongKeTopDichVuResponse>>> TopDichVu(
    [FromQuery] DateTime? tuNgay,
    [FromQuery] DateTime? denNgay,
    [FromQuery] int top = 5,
    CancellationToken ct = default)
  {
    top = Math.Clamp(top, 1, 50);
    var (from, to) = ResolveRange(tuNgay, denNgay);

    // Client-side aggregation để tránh Pomelo MySQL không translate được GroupBy + Join
    var rawIds = await _db.LichHens.AsNoTracking()
      .Where(x => x.ThoiGianBatDau >= from && x.ThoiGianBatDau <= to)
      .Where(x => x.TrangThai == TrangThaiLichHen.HoanThanh)
      .Select(x => x.DichVuId)
      .ToListAsync(ct);

    var counts = rawIds
      .GroupBy(id => id)
      .Select(g => new { DichVuId = g.Key, SoLan = g.Count() })
      .OrderByDescending(x => x.SoLan)
      .Take(top)
      .ToList();

    if (counts.Count == 0) return Ok(new List<ThongKeTopDichVuResponse>());

    var topIds = counts.Select(c => c.DichVuId).ToList();
    var services = await _db.DichVus.AsNoTracking()
      .Where(d => topIds.Contains(d.Id))
      .Select(d => new { d.Id, d.Ten, d.Gia })
      .ToListAsync(ct);

    var data = counts
      .Join(services, c => c.DichVuId, s => s.Id,
        (c, s) => new ThongKeTopDichVuResponse(s.Id, s.Ten, c.SoLan, s.Gia * c.SoLan))
      .OrderByDescending(x => x.SoLan)
      .ThenByDescending(x => x.DoanhThuUocTinh)
      .ToList();

    return Ok(data);
  }
}
