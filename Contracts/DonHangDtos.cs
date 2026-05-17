using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record TaoDonHangChiTietRequest(
  [Required] Guid SanPhamId,
  [Range(1, 999999)] int SoLuong);

public sealed record TaoDonHangRequest(
  [Required, MaxLength(200)] string TenNguoiNhan,
  [Required, MaxLength(32)] string SoDienThoaiNhan,
  [Required, MaxLength(500)] string DiaChiGiao,
  [MaxLength(500)] string? GhiChu,
  [Required, MinLength(1)] List<TaoDonHangChiTietRequest> ChiTiets);

public sealed record DonHangChiTietResponse(
  Guid SanPhamId,
  string TenSanPham,
  int SoLuong,
  decimal DonGia,
  decimal ThanhTien);

public sealed record DonHangResponse(
  Guid Id,
  decimal TongTien,
  string TrangThai,
  string TenNguoiNhan,
  string SoDienThoaiNhan,
  string DiaChiGiao,
  string? GhiChu,
  DateTime NgayTao,
  List<DonHangChiTietResponse> ChiTiets);

public sealed record AdminCapNhatTrangThaiDonHangRequest([Required] string TrangThai);

