using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record KhuyenMaiResponse(
  Guid Id,
  string Ten,
  string? MoTa,
  decimal? PhanTramGiam,
  decimal? SoTienGiam,
  decimal? DieuKienToiThieu,
  DateTime TuNgay,
  DateTime DenNgay,
  bool HienThi,
  Guid? DichVuId = null,
  string? TenDichVu = null);

public sealed record UpsertKhuyenMaiRequest(
  [Required, MaxLength(200)] string Ten,
  [MaxLength(2000)] string? MoTa,
  decimal? PhanTramGiam,
  decimal? SoTienGiam,
  decimal? DieuKienToiThieu,
  [Required] DateTime TuNgay,
  [Required] DateTime DenNgay,
  bool HienThi = true,
  Guid? DichVuId = null);
