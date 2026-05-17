using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class SanPham
{
  public Guid Id { get; set; }

  [MaxLength(200)]
  public string Ten { get; set; } = default!;

  [MaxLength(2000)]
  public string? MoTa { get; set; }

  public decimal Gia { get; set; }

  public int TonKho { get; set; }

  [MaxLength(1000)]
  public string? HinhAnhUrl { get; set; }

  public bool DangBan { get; set; } = true;

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }

  public List<DonHangChiTiet> DonHangChiTiets { get; set; } = new();
}

