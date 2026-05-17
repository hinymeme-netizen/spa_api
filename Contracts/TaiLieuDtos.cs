using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record TaiLieuResponse(
  Guid Id,
  string TieuDe,
  string NoiDung,
  string? Nguon,
  int SoChunk,
  string TrangThai,
  DateTime NgayTao,
  DateTime? CapNhatLuc);

public sealed record TaiLieuListItemResponse(
  Guid Id,
  string TieuDe,
  string? Nguon,
  int SoChunk,
  string TrangThai,
  int DoDaiNoiDung,
  DateTime NgayTao,
  DateTime? CapNhatLuc);

public sealed record UpsertTaiLieuRequest(
  [Required, MaxLength(300)] string TieuDe,
  [Required] string NoiDung,
  [MaxLength(100)] string? Nguon);
