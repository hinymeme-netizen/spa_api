using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class TaiKhoan
{
  public Guid Id { get; set; }

  [MaxLength(256)]
  public string Email { get; set; } = default!;

  [MaxLength(32)]
  public string? SoDienThoai { get; set; }

  [MaxLength(128)]
  public string HoTen { get; set; } = default!;

  [MaxLength(512)]
  public string MatKhauHash { get; set; } = default!;

  /// <summary>
  /// Google sub claim (unique). Null khi tài khoản đăng ký kiểu local (email/password).
  /// </summary>
  [MaxLength(64)]
  public string? GoogleSub { get; set; }

  public VaiTroTaiKhoan VaiTro { get; set; } = VaiTroTaiKhoan.User;

  public bool KichHoat { get; set; } = true;

  /// <summary>
  /// Hash của reset token (SHA-256). Null = không có yêu cầu reset đang chờ.
  /// Lưu hash thay vì plaintext để token bị leak DB không dùng được.
  /// </summary>
  [MaxLength(128)]
  public string? PasswordResetTokenHash { get; set; }

  /// <summary>Hạn sử dụng token (UTC). Sau thời điểm này token vô hiệu.</summary>
  public DateTime? PasswordResetExpires { get; set; }

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }

  public List<LichHen> LichHens { get; set; } = new();
  public List<DonHang> DonHangs { get; set; } = new();
}

