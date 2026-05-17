using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Domain;

namespace SpaApi.Services;

/// <summary>
/// Validate khi gán nhân viên vào lịch hẹn — dùng chung giữa LichHenController và ChatOrchestratorService.
/// </summary>
public static class BookingValidator
{
  /// <summary>
  /// Validate nhân viên có thể nhận lịch trong khung [start, end).
  /// Trả null nếu hợp lệ; ngược lại trả message tiếng Việt cụ thể.
  /// </summary>
  /// <param name="excludeLichId">Khi update/xác nhận: bỏ qua chính lịch này khi check trùng.</param>
  public static async Task<string?> ValidateAssignNhanVienAsync(
    SpaDbContext db,
    Guid nhanVienId,
    DateTime start,
    DateTime end,
    Guid? excludeLichId = null,
    CancellationToken ct = default)
  {
    // 1. NV tồn tại + đang làm việc
    var nv = await db.NhanViens.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nhanVienId, ct);
    if (nv is null)
      return "Nhân viên không tồn tại.";
    if (!nv.DangLamViec)
      return $"Nhân viên {nv.HoTen} hiện không làm việc.";

    // 2. NV phải có ca làm việc cover khung [start, end)
    //    Ưu tiên ca cụ thể của ngày đó (Ngay = ngày). Nếu không có → fallback ca lặp hàng tuần (Ngay = null + ThuTrongTuan match).
    var thu = (int)start.DayOfWeek; // 0=CN..6=T7
    var ngay = DateOnly.FromDateTime(start);
    var startStr = start.ToString("HH:mm");
    var endInclusiveStr = end.AddMinutes(-1).ToString("HH:mm");

    var allCa = await db.CaLamViecs.AsNoTracking()
      .Where(c => c.NhanVienId == nhanVienId)
      .Where(c => c.Ngay == ngay
                  || (c.Ngay == null
                      && c.ThuTrongTuan == thu
                      && (c.HieuLucTuNgay == null || c.HieuLucTuNgay <= ngay)
                      && (c.HieuLucDenNgay == null || c.HieuLucDenNgay >= ngay)))
      .ToListAsync(ct);

    var specificCa = allCa.Where(c => c.Ngay == ngay).ToList();

    // Nếu specific Ca tồn tại + LaCaNghi → NV nghỉ ngày đó
    if (specificCa.Any(c => c.LaCaNghi))
      return $"Nhân viên {nv.HoTen} đã đăng ký nghỉ vào {VnDay(thu)} ({start:dd/MM}). Vui lòng chọn nhân viên khác hoặc đổi ngày.";

    var effectiveCa = specificCa.Count > 0
      ? specificCa.Where(c => !c.LaCaNghi).ToList()
      : allCa.Where(c => c.Ngay == null).ToList();

    if (effectiveCa.Count == 0)
      return $"Nhân viên {nv.HoTen} không có ca làm việc vào {VnDay(thu)} ({start:dd/MM}). Vui lòng chọn nhân viên khác hoặc đổi ngày.";

    var inShift = effectiveCa.Any(c =>
      string.Compare(c.GioBatDau, startStr, StringComparison.Ordinal) <= 0 &&
      string.Compare(c.GioKetThuc, endInclusiveStr, StringComparison.Ordinal) >= 0);
    if (!inShift)
    {
      var caStr = string.Join(", ", effectiveCa.Select(c => $"{c.GioBatDau}–{c.GioKetThuc}"));
      return $"Nhân viên {nv.HoTen} {VnDay(thu)} ({start:dd/MM}) chỉ làm ca {caStr}, không cover khung giờ {startStr}–{end:HH:mm}.";
    }

    // 3. Không trùng lịch khác (đã ChoXacNhan / DaXacNhan / HoanThanh) của cùng NV
    var trungQuery = db.LichHens.AsNoTracking()
      .Where(x => x.NhanVienId == nhanVienId)
      .Where(x => x.TrangThai != TrangThaiLichHen.DaHuy && x.TrangThai != TrangThaiLichHen.TuChoi)
      .Where(x => x.ThoiGianBatDau < end && x.ThoiGianKetThuc > start);
    if (excludeLichId.HasValue)
      trungQuery = trungQuery.Where(x => x.Id != excludeLichId.Value);

    var trung = await trungQuery
      .Select(x => new { x.Id, x.ThoiGianBatDau, x.ThoiGianKetThuc })
      .FirstOrDefaultAsync(ct);
    if (trung is not null)
      return $"Nhân viên {nv.HoTen} đã có lịch hẹn khác trong khung giờ này ({trung.ThoiGianBatDau:HH:mm}–{trung.ThoiGianKetThuc:HH:mm} {trung.ThoiGianBatDau:dd/MM}). Vui lòng chọn giờ hoặc nhân viên khác.";

    return null;
  }

  private static string VnDay(int thu) => thu switch
  {
    0 => "Chủ nhật",
    1 => "Thứ Hai",
    2 => "Thứ Ba",
    3 => "Thứ Tư",
    4 => "Thứ Năm",
    5 => "Thứ Sáu",
    6 => "Thứ Bảy",
    _ => $"thứ {thu}"
  };
}
