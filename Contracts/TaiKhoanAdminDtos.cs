using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record TaiKhoanAdminResponse(
  Guid Id,
  string Email,
  string HoTen,
  string? SoDienThoai,
  string VaiTro,
  bool KichHoat,
  DateTime NgayTao);

public sealed record AdminCapNhatTaiKhoanRequest(
  [Required, MaxLength(128)] string HoTen,
  [MaxLength(32)] string? SoDienThoai,
  [Required] string VaiTro,
  bool KichHoat);

