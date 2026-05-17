using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record DanhMucDichVuResponse(
  Guid Id,
  string Ten,
  string? Slug,
  string? MoTa,
  string? Icon,
  int ThuTu,
  bool HienThi,
  int SoDichVu);

public sealed record UpsertDanhMucDichVuRequest(
  [Required, MaxLength(128)] string Ten,
  [MaxLength(64)] string? Slug,
  [MaxLength(500)] string? MoTa,
  [MaxLength(64)] string? Icon,
  int ThuTu = 0,
  bool HienThi = true);
