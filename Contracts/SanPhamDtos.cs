using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record SanPhamResponse(
  Guid Id,
  string Ten,
  string? MoTa,
  decimal Gia,
  int TonKho,
  string? HinhAnhUrl,
  bool DangBan);

public sealed record UpsertSanPhamRequest(
  [Required, MaxLength(200)] string Ten,
  [MaxLength(2000)] string? MoTa,
  [Range(0, 999999999)] decimal Gia,
  [Range(0, 999999999)] int TonKho,
  [MaxLength(1000)] string? HinhAnhUrl,
  bool DangBan = true);

