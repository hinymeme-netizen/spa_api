namespace SpaApi.Contracts;

public sealed record ThongKeSoLuongTheoNgayResponse(DateOnly Ngay, int SoLuong);
public sealed record ThongKeTopDichVuResponse(Guid DichVuId, string TenDichVu, int SoLan, decimal DoanhThuUocTinh);
public sealed record ThongKeDoanhThuResponse(decimal DoanhThuDichVu, decimal TongDoanhThu);
public sealed record TongQuanResponse(
  int SoKhachHang,
  int SoNhanVien,
  int SoDichVu,
  int SoLichHenHoanThanh,
  int SoDanhGia,
  double DiemTrungBinh);

public sealed record NhanVienDashboardResponse(
  Guid NhanVienId,
  string HoTen,
  int SoLichHomNay,
  int SoLichSapToi,
  int SoLichHoanThanh30Ngay,
  decimal DoanhThu30Ngay,
  int SoDanhGia,
  double DiemTrungBinh,
  List<ThongKeSoLuongTheoNgayResponse> BieuDoLich30Ngay,
  List<ThongKeTopDichVuResponse> TopDichVu);
