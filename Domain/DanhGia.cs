using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class DanhGia
{
  public Guid Id { get; set; }

  public Guid LichHenId { get; set; }
  public LichHen LichHen { get; set; } = default!;

  public Guid TaiKhoanId { get; set; }
  public TaiKhoan TaiKhoan { get; set; } = default!;

  public Guid DichVuId { get; set; }
  public DichVu DichVu { get; set; } = default!;

  [Range(1, 5)]
  public int SoSao { get; set; }

  [MaxLength(2000)]
  public string? NoiDung { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
}

