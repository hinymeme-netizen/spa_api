using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record NhanVienResponse(
  Guid Id,
  string HoTen,
  string? SoDienThoai,
  string? ChuyenMon,
  bool DangLamViec,
  Guid? TaiKhoanId = null,
  string? Email = null);

public sealed record UpsertNhanVienRequest(
  [Required, MaxLength(128)] string HoTen,
  [MaxLength(32)] string? SoDienThoai,
  [MaxLength(256)] string? ChuyenMon,
  bool DangLamViec = true,
  // Tạo / liên kết tài khoản đăng nhập role NhanVien.
  // Chỉ có hiệu lực khi tạo mới hoặc khi NhanVien chưa có TaiKhoanId.
  [EmailAddress, MaxLength(256)] string? Email = null,
  [MinLength(6), MaxLength(200)] string? MatKhau = null);

public sealed record CaLamViecResponse(
  Guid Id,
  int ThuTrongTuan,
  string GioBatDau,
  string GioKetThuc,
  DateOnly? Ngay = null,
  DateOnly? HieuLucTuNgay = null,
  DateOnly? HieuLucDenNgay = null,
  bool LaCaNghi = false);

public sealed record MyCaLamViecResponse(Guid NhanVienId, string HoTen, List<CaLamViecResponse> Items);

public sealed record UpsertCaLamViecRequest(
  [Range(0, 6)] int ThuTrongTuan,
  [Required, MaxLength(8)] string GioBatDau,
  [Required, MaxLength(8)] string GioKetThuc,
  DateOnly? Ngay = null);

public sealed record AdminCaLamViecItem(
  Guid Id,
  Guid NhanVienId,
  string HoTen,
  string? ChuyenMon,
  int ThuTrongTuan,
  string GioBatDau,
  string GioKetThuc,
  DateOnly? Ngay,
  DateOnly? HieuLucTuNgay,
  DateOnly? HieuLucDenNgay,
  bool LaCaNghi);

/// <summary>
/// Request xóa ca recurring cho 1 ngày cụ thể.
/// Backend sẽ tạo specific Ca với LaCaNghi=true cho ngày đó (override recurring).
/// </summary>
public sealed record SkipRecurringCaRequest(
  [Required] DateOnly Ngay);

/// <summary>
/// Admin: cập nhật / reset thông tin tài khoản đăng nhập của 1 NhanVien.
/// Tất cả field optional — chỉ field nào có giá trị mới được áp dụng.
/// </summary>
public sealed record UpdateNhanVienTaiKhoanRequest(
  [EmailAddress, MaxLength(256)] string? Email = null,
  [MinLength(6), MaxLength(200)] string? MatKhauMoi = null,
  bool? KichHoat = null);
