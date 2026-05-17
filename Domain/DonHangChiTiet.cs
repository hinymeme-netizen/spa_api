namespace SpaApi.Domain;

public sealed class DonHangChiTiet
{
  public Guid Id { get; set; }

  public Guid DonHangId { get; set; }
  public DonHang DonHang { get; set; } = default!;

  public Guid SanPhamId { get; set; }
  public SanPham SanPham { get; set; } = default!;

  public int SoLuong { get; set; }
  public decimal DonGia { get; set; }
  public decimal ThanhTien { get; set; }
}

