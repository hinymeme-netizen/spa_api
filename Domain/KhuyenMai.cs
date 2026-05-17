using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class KhuyenMai
{
  public Guid Id { get; set; }

  [MaxLength(200)]
  public string Ten { get; set; } = default!;

  [MaxLength(2000)]
  public string? MoTa { get; set; }

  public decimal? PhanTramGiam { get; set; }
  public decimal? SoTienGiam { get; set; }

  public decimal? DieuKienToiThieu { get; set; }

  public DateTime TuNgay { get; set; }
  public DateTime DenNgay { get; set; }

  public bool HienThi { get; set; } = true;

  // null = áp dụng cho tất cả dịch vụ
  public Guid? DichVuId { get; set; }
  public DichVu? DichVu { get; set; }
}

