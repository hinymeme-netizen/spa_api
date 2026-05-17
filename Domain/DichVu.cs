using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class DichVu
{
  public Guid Id { get; set; }

  [MaxLength(200)]
  public string Ten { get; set; } = default!;

  [MaxLength(2000)]
  public string? MoTa { get; set; }

  public decimal Gia { get; set; }

  public int ThoiLuongPhut { get; set; }

  public bool HienThi { get; set; } = true;

  [MaxLength(1000)]
  public string? HinhAnhUrl { get; set; }

  public Guid? DanhMucId { get; set; }
  public DanhMucDichVu? DanhMuc { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }

  public List<LichHen> LichHens { get; set; } = new();
}

