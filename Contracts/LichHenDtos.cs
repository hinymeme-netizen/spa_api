using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record LichHenResponse(
  Guid Id,
  Guid DichVuId,
  string TenDichVu,
  Guid? NhanVienId,
  string? TenNhanVien,
  DateTime ThoiGianBatDau,
  DateTime ThoiGianKetThuc,
  string TrangThai,
  string? GhiChu,
  string? LyDoTuChoi,
  Guid? TaiKhoanId = null,
  string? TenKhachHang = null,
  bool DaDanhGia = false);

public sealed record TaoLichHenRequest(
  [Required] Guid DichVuId,
  Guid? NhanVienId,
  [Required] DateTime ThoiGianBatDau,
  [MaxLength(500)] string? GhiChu);

public sealed record CapNhatLichHenRequest(
  Guid? NhanVienId,
  DateTime? ThoiGianBatDau,
  [MaxLength(500)] string? GhiChu);

public sealed record AdminXacNhanRequest(Guid? NhanVienId);
public sealed record AdminTuChoiRequest([Required, MaxLength(500)] string LyDoTuChoi);
public sealed record AdminGanNhanVienRequest([Required] Guid NhanVienId);

