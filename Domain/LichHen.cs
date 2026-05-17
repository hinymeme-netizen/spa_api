using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class LichHen
{
  public Guid Id { get; set; }

  public Guid TaiKhoanId { get; set; }
  public TaiKhoan TaiKhoan { get; set; } = default!;

  public Guid DichVuId { get; set; }
  public DichVu DichVu { get; set; } = default!;

  public Guid? NhanVienId { get; set; }
  public NhanVien? NhanVien { get; set; }

  public DateTime ThoiGianBatDau { get; set; }
  public DateTime ThoiGianKetThuc { get; set; }

  [MaxLength(500)]
  public string? GhiChu { get; set; }

  public TrangThaiLichHen TrangThai { get; set; } = TrangThaiLichHen.ChoXacNhan;

  [MaxLength(500)]
  public string? LyDoTuChoi { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }

  public DanhGia? DanhGia { get; set; }
}

