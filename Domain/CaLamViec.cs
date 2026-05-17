using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

public sealed class CaLamViec
{
  public Guid Id { get; set; }

  public Guid NhanVienId { get; set; }
  public NhanVien NhanVien { get; set; } = default!;

  /// <summary>0=ChuNhat ... 6=ThuBay (chỉ dùng khi Ngay == null = ca lặp hàng tuần).</summary>
  [Range(0, 6)]
  public int ThuTrongTuan { get; set; }

  [MaxLength(8)]
  public string GioBatDau { get; set; } = "08:00";

  [MaxLength(8)]
  public string GioKetThuc { get; set; } = "20:00";

  /// <summary>
  /// Nếu set → ca CỤ THỂ ngày này (override recurring). Nếu null → ca lặp hàng tuần theo ThuTrongTuan.
  /// </summary>
  public DateOnly? Ngay { get; set; }

  /// <summary>
  /// Recurring (Ngay = null): chỉ có hiệu lực từ ngày này trở đi. Mặc định = ngày tạo.
  /// Specific (Ngay != null): không dùng (để null).
  /// </summary>
  public DateOnly? HieuLucTuNgay { get; set; }

  /// <summary>
  /// Recurring (Ngay = null): chỉ có hiệu lực ĐẾN ngày này. Null = không giới hạn.
  /// Dùng để "xóa ca recurring từ ngày X trở đi" mà vẫn giữ lịch sử trước đó.
  /// </summary>
  public DateOnly? HieuLucDenNgay { get; set; }

  /// <summary>
  /// Specific (Ngay != null) + LaCaNghi = true → đánh dấu NV nghỉ ngày đó (override recurring).
  /// BookingValidator sẽ coi như NV không có ca cover.
  /// </summary>
  public bool LaCaNghi { get; set; } = false;
}

