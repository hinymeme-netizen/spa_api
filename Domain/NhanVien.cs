using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class NhanVien
{
  public Guid Id { get; set; }

  [MaxLength(128)]
  public string HoTen { get; set; } = default!;

  [MaxLength(32)]
  public string? SoDienThoai { get; set; }

  [MaxLength(256)]
  public string? ChuyenMon { get; set; }

  public bool DangLamViec { get; set; } = true;

  // Liên kết với TaiKhoan (role NhanVien) nếu nhân viên có account đăng nhập
  public Guid? TaiKhoanId { get; set; }
  public TaiKhoan? TaiKhoan { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }

  public List<CaLamViec> CaLamViecs { get; set; } = new();
  public List<LichHen> LichHens { get; set; } = new();
}

