using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record DichVuResponse(
  Guid Id,
  string Ten,
  string? MoTa,
  decimal Gia,
  int ThoiLuongPhut,
  bool HienThi,
  string? HinhAnhUrl = null,
  Guid? DanhMucId = null,
  string? TenDanhMuc = null);

public sealed record UpsertDichVuRequest(
  [Required, MaxLength(200)] string Ten,
  [MaxLength(2000)] string? MoTa,
  [Range(0, 999999999)] decimal Gia,
  [Range(5, 600)] int ThoiLuongPhut,
  bool HienThi = true,
  [MaxLength(1000)] string? HinhAnhUrl = null,
  Guid? DanhMucId = null);
