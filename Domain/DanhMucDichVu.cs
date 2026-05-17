using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class DanhMucDichVu
{
  public Guid Id { get; set; }

  [MaxLength(128)]
  public string Ten { get; set; } = default!;

  [MaxLength(64)]
  public string? Slug { get; set; }

  [MaxLength(500)]
  public string? MoTa { get; set; }

  [MaxLength(64)]
  public string? Icon { get; set; }

  public int ThuTu { get; set; } = 0;

  public bool HienThi { get; set; } = true;

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;

  public List<DichVu> DichVus { get; set; } = new();
}
