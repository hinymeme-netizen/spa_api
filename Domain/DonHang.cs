using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class DonHang
{
  public Guid Id { get; set; }

  public Guid TaiKhoanId { get; set; }
  public TaiKhoan TaiKhoan { get; set; } = default!;

  public decimal TongTien { get; set; }

  public TrangThaiDonHang TrangThai { get; set; } = TrangThaiDonHang.ChoXacNhan;

  [MaxLength(200)]
  public string TenNguoiNhan { get; set; } = default!;

  [MaxLength(32)]
  public string SoDienThoaiNhan { get; set; } = default!;

  [MaxLength(500)]
  public string DiaChiGiao { get; set; } = default!;

  [MaxLength(500)]
  public string? GhiChu { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;

  public List<DonHangChiTiet> ChiTiets { get; set; } = new();
}

