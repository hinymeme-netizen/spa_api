using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Settings;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/lich-hen")]
public sealed class LichHenController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly GioLamViecOptions _gio;

  public LichHenController(SpaDbContext db, IOptions<SpaOptions> spaOpt)
  {
    _db = db;
    _gio = spaOpt.Value.GioLamViec;
  }

  // User: tạo lịch
  [HttpPost]
  [Authorize]
  public async Task<ActionResult<LichHenResponse>> Tao(TaoLichHenRequest req, CancellationToken ct)
  {
    var userId = GetUserId();

    var dv = await _db.DichVus.FirstOrDefaultAsync(x => x.Id == req.DichVuId && x.HienThi, ct);
    if (dv is null) return BadRequest(new { message = "Dịch vụ không tồn tại hoặc đang ẩn." });

    if (req.ThoiGianBatDau.Kind == DateTimeKind.Unspecified)
      req = req with { ThoiGianBatDau = DateTime.SpecifyKind(req.ThoiGianBatDau, DateTimeKind.Local) };

    if (req.ThoiGianBatDau < DateTime.Now.AddMinutes(-1))
      return BadRequest(new { message = "Thời gian đặt lịch phải nằm trong tương lai." });
    if (req.ThoiGianBatDau > DateTime.Now.AddMonths(6))
      return BadRequest(new { message = "Chỉ cho phép đặt lịch trong vòng 6 tháng tới." });

    if (!HopLeGioLamViec(req.ThoiGianBatDau))
      return BadRequest(new { message = $"Thời gian đặt lịch ngoài giờ làm việc ({_gio.MoCua} – {_gio.DongCua})." });
    if (!DungBuocPhut(req.ThoiGianBatDau))
      return BadRequest(new { message = $"Thời gian phải theo bước {_gio.BuocPhutDatLich} phút (vd: {_gio.MoCua}, …:30, …:00)." });

    var end = req.ThoiGianBatDau.AddMinutes(dv.ThoiLuongPhut);
    if (!HopLeGioLamViec(end.AddMinutes(-1)))
      return BadRequest(new { message = $"Lịch dự kiến kết thúc lúc {end:HH:mm}, vượt giờ đóng cửa ({_gio.DongCua})." });

    // Cấm trùng giờ với lịch đang hoạt động khác của cùng user
    var trungGio = await _db.LichHens.AnyAsync(x =>
      x.TaiKhoanId == userId &&
      x.TrangThai != TrangThaiLichHen.DaHuy &&
      x.TrangThai != TrangThaiLichHen.TuChoi &&
      x.ThoiGianBatDau < end &&
      x.ThoiGianKetThuc > req.ThoiGianBatDau, ct);
    if (trungGio)
      return BadRequest(new { message = "Bạn đã có lịch hẹn khác trùng khung giờ này. Vui lòng chọn giờ khác." });

    NhanVien? nv = null;
    if (req.NhanVienId is not null)
    {
      var err = await SpaApi.Services.BookingValidator.ValidateAssignNhanVienAsync(
        _db, req.NhanVienId.Value, req.ThoiGianBatDau, end, excludeLichId: null, ct);
      if (err is not null) return BadRequest(new { message = err });
      nv = await _db.NhanViens.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.NhanVienId, ct);
    }

    var lich = new LichHen
    {
      Id = Guid.NewGuid(),
      TaiKhoanId = userId,
      DichVuId = dv.Id,
      NhanVienId = req.NhanVienId,
      ThoiGianBatDau = req.ThoiGianBatDau,
      ThoiGianKetThuc = end,
      GhiChu = string.IsNullOrWhiteSpace(req.GhiChu) ? null : req.GhiChu.Trim(),
      TrangThai = TrangThaiLichHen.ChoXacNhan
    };

    _db.LichHens.Add(lich);
    await _db.SaveChangesAsync(ct);

    return Ok(new LichHenResponse(
      lich.Id, dv.Id, dv.Ten, nv?.Id, nv?.HoTen,
      lich.ThoiGianBatDau, lich.ThoiGianKetThuc,
      lich.TrangThai.ToString(), lich.GhiChu, lich.LyDoTuChoi,
      null, null, false));
  }

  // User: xem lịch của mình
  [HttpGet("me")]
  [Authorize]
  public async Task<ActionResult<List<LichHenResponse>>> LichCuaToi([FromQuery] TrangThaiLichHen? trangThai, CancellationToken ct)
  {
    var userId = GetUserId();
    var q = _db.LichHens.AsNoTracking()
      .Where(x => x.TaiKhoanId == userId)
      .OrderByDescending(x => x.ThoiGianBatDau)
      .Include(x => x.DichVu)
      .Include(x => x.NhanVien)
      .Include(x => x.DanhGia)
      .AsQueryable();

    if (trangThai is not null) q = q.Where(x => x.TrangThai == trangThai);

    var items = await q.Select(x => new LichHenResponse(
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
      x.DanhGia != null)).ToListAsync(ct);

    return Ok(items);
  }

  // User: chỉnh sửa lịch (trước giờ quy định, chỉ khi Chờ xác nhận/Đã xác nhận)
  [HttpPut("{id:guid}")]
  [Authorize]
  public async Task<ActionResult> CapNhat(Guid id, CapNhatLichHenRequest req, CancellationToken ct)
  {
    var userId = GetUserId();
    var lich = await _db.LichHens.Include(x => x.DichVu).FirstOrDefaultAsync(x => x.Id == id && x.TaiKhoanId == userId, ct);
    if (lich is null) return NotFound();
    if (lich.TrangThai is TrangThaiLichHen.DaHuy or TrangThaiLichHen.HoanThanh or TrangThaiLichHen.TuChoi)
      return BadRequest(new { message = "Không thể chỉnh sửa lịch ở trạng thái hiện tại." });
    if (!ConDuocDoiOrHuy(lich.ThoiGianBatDau)) return BadRequest(new { message = "Đã quá thời hạn cho phép chỉnh sửa/hủy." });

    if (req.ThoiGianBatDau is not null)
    {
      var start = req.ThoiGianBatDau.Value;
      if (start.Kind == DateTimeKind.Unspecified) start = DateTime.SpecifyKind(start, DateTimeKind.Local);
      if (start < DateTime.Now.AddMinutes(-1))
        return BadRequest(new { message = "Thời gian đặt lịch phải nằm trong tương lai." });
      if (!HopLeGioLamViec(start))
        return BadRequest(new { message = $"Thời gian ngoài giờ làm việc ({_gio.MoCua} – {_gio.DongCua})." });
      if (!DungBuocPhut(start))
        return BadRequest(new { message = $"Thời gian phải theo bước {_gio.BuocPhutDatLich} phút." });
      var end = start.AddMinutes(lich.DichVu.ThoiLuongPhut);
      if (!HopLeGioLamViec(end.AddMinutes(-1)))
        return BadRequest(new { message = $"Lịch dự kiến kết thúc lúc {end:HH:mm}, vượt giờ đóng cửa ({_gio.DongCua})." });
      lich.ThoiGianBatDau = start;
      lich.ThoiGianKetThuc = end;
    }

    if (req.NhanVienId is not null)
    {
      var err = await SpaApi.Services.BookingValidator.ValidateAssignNhanVienAsync(
        _db, req.NhanVienId.Value, lich.ThoiGianBatDau, lich.ThoiGianKetThuc, excludeLichId: lich.Id, ct);
      if (err is not null) return BadRequest(new { message = err });
      lich.NhanVienId = req.NhanVienId;
    }

    lich.GhiChu = string.IsNullOrWhiteSpace(req.GhiChu) ? null : req.GhiChu.Trim();
    lich.CapNhatLuc = DateTime.UtcNow;
    lich.TrangThai = TrangThaiLichHen.ChoXacNhan; // đổi thông tin -> quay về chờ xác nhận
    lich.LyDoTuChoi = null;

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  // User: hủy lịch
  [HttpPost("{id:guid}/huy")]
  [Authorize]
  public async Task<ActionResult> Huy(Guid id, CancellationToken ct)
  {
    var userId = GetUserId();
    var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == id && x.TaiKhoanId == userId, ct);
    if (lich is null) return NotFound();
    if (lich.TrangThai is TrangThaiLichHen.DaHuy or TrangThaiLichHen.HoanThanh) return NoContent();
    if (!ConDuocDoiOrHuy(lich.ThoiGianBatDau)) return BadRequest(new { message = "Đã quá thời hạn cho phép chỉnh sửa/hủy." });

    lich.TrangThai = TrangThaiLichHen.DaHuy;
    lich.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  // Admin xem tất cả lịch. NhanVien chỉ thấy lịch của mình (auto filter).
  [HttpGet("admin")]
  [Authorize(Roles = "Admin,NhanVien")]
  public async Task<ActionResult<List<LichHenResponse>>> AdminList(
    [FromQuery] DateTime? tuNgay,
    [FromQuery] DateTime? denNgay,
    [FromQuery] Guid? dichVuId,
    [FromQuery] TrangThaiLichHen? trangThai,
    CancellationToken ct)
  {
    Guid? nvFilter = null;
    if (User.IsInRole("NhanVien") && !User.IsInRole("Admin"))
    {
      var taiKhoanId = GetUserId();
      var nv = await _db.NhanViens.AsNoTracking().FirstOrDefaultAsync(x => x.TaiKhoanId == taiKhoanId, ct);
      if (nv is null)
        return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào. Vui lòng liên hệ quản trị viên." });
      nvFilter = nv.Id;
    }

    var q = _db.LichHens.AsNoTracking()
      .Include(x => x.DichVu)
      .Include(x => x.NhanVien)
      .Include(x => x.TaiKhoan)
      .Include(x => x.DanhGia)
      .OrderByDescending(x => x.ThoiGianBatDau)
      .AsQueryable();

    if (nvFilter.HasValue) q = q.Where(x => x.NhanVienId == nvFilter);
    if (tuNgay is not null) q = q.Where(x => x.ThoiGianBatDau >= tuNgay);
    if (denNgay is not null) q = q.Where(x => x.ThoiGianBatDau <= denNgay);
    if (dichVuId is not null) q = q.Where(x => x.DichVuId == dichVuId);
    if (trangThai is not null) q = q.Where(x => x.TrangThai == trangThai);

    var items = await q.Select(x => new LichHenResponse(
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
      x.TaiKhoanId,
      x.TaiKhoan != null ? x.TaiKhoan.HoTen : null,
      x.DanhGia != null)).ToListAsync(ct);

    return Ok(items);
  }

  [HttpPost("admin/{id:guid}/xac-nhan")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> AdminXacNhan(Guid id, AdminXacNhanRequest req, CancellationToken ct)
  {
    var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (lich is null) return NotFound();
    if (lich.TrangThai is TrangThaiLichHen.DaHuy or TrangThaiLichHen.HoanThanh) return BadRequest(new { message = "Không thể xác nhận." });

    if (req.NhanVienId is not null)
    {
      var err = await SpaApi.Services.BookingValidator.ValidateAssignNhanVienAsync(
        _db, req.NhanVienId.Value, lich.ThoiGianBatDau, lich.ThoiGianKetThuc, excludeLichId: lich.Id, ct);
      if (err is not null) return BadRequest(new { message = err });
      lich.NhanVienId = req.NhanVienId;
    }

    lich.TrangThai = TrangThaiLichHen.DaXacNhan;
    lich.LyDoTuChoi = null;
    lich.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpPost("admin/{id:guid}/tu-choi")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> AdminTuChoi(Guid id, AdminTuChoiRequest req, CancellationToken ct)
  {
    var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (lich is null) return NotFound();
    if (lich.TrangThai is TrangThaiLichHen.DaHuy or TrangThaiLichHen.HoanThanh) return BadRequest(new { message = "Không thể từ chối." });

    lich.TrangThai = TrangThaiLichHen.TuChoi;
    lich.LyDoTuChoi = req.LyDoTuChoi.Trim();
    lich.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpPost("admin/{id:guid}/gan-nhan-vien")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> AdminGanNhanVien(Guid id, AdminGanNhanVienRequest req, CancellationToken ct)
  {
    var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (lich is null) return NotFound();

    var err = await SpaApi.Services.BookingValidator.ValidateAssignNhanVienAsync(
      _db, req.NhanVienId, lich.ThoiGianBatDau, lich.ThoiGianKetThuc, excludeLichId: lich.Id, ct);
    if (err is not null) return BadRequest(new { message = err });

    lich.NhanVienId = req.NhanVienId;
    lich.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpPost("admin/{id:guid}/hoan-thanh")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> AdminHoanThanh(Guid id, CancellationToken ct)
  {
    var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (lich is null) return NotFound();
    lich.TrangThai = TrangThaiLichHen.HoanThanh;
    lich.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  private Guid GetUserId()
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.Parse(raw!);
  }

  private bool HopLeGioLamViec(DateTime localTime)
  {
    // localTime expected as Local time; we compare only time-of-day
    var mo = TimeOnly.ParseExact(_gio.MoCua, "HH:mm", CultureInfo.InvariantCulture);
    var dong = TimeOnly.ParseExact(_gio.DongCua, "HH:mm", CultureInfo.InvariantCulture);
    var t = TimeOnly.FromDateTime(localTime);
    return t >= mo && t <= dong;
  }

  private bool DungBuocPhut(DateTime localTime)
  {
    return (localTime.Minute % _gio.BuocPhutDatLich) == 0;
  }

  private bool ConDuocDoiOrHuy(DateTime localStart)
  {
    var now = DateTime.Now;
    var diff = localStart - now;
    return diff.TotalHours >= _gio.SoGioToiThieuDeHuy;
  }
}

