using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record TaoDanhGiaRequest(
  [Required] Guid LichHenId,
  [Range(1, 5)] int SoSao,
  [MaxLength(2000)] string? NoiDung);

public sealed record DanhGiaResponse(
  Guid Id,
  Guid LichHenId,
  Guid DichVuId,
  string TenDichVu,
  Guid TaiKhoanId,
  string HoTen,
  int SoSao,
  string? NoiDung,
  DateTime NgayTao);

