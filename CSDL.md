# CSDL (tiếng Việt) – bảng & cột

Hệ thống dùng **MySQL** (qua EF Core / Pomelo).

## 1) `TaiKhoan` (tài khoản User/Admin)
- `Id` (GUID, PK)
- `Email` (varchar(256), unique)
- `SoDienThoai` (varchar(32), null)
- `HoTen` (varchar(128))
- `MatKhauHash` (varchar(512))
- `VaiTro` (int) — 0: User, 1: Admin
- `KichHoat` (bit)
- `NgayTao` (datetime)
- `CapNhatLuc` (datetime, null)

## 2) `DichVu` (dịch vụ + bảng giá)
- `Id` (GUID, PK)
- `Ten` (varchar(200))
- `MoTa` (varchar(2000), null)
- `Gia` (decimal(18,2))
- `ThoiLuongPhut` (int)
- `HienThi` (bit)
- `NgayTao` (datetime)
- `CapNhatLuc` (datetime, null)

## 3) `NhanVien` (nhân viên)
- `Id` (GUID, PK)
- `HoTen` (varchar(128))
- `SoDienThoai` (varchar(32), null)
- `ChuyenMon` (varchar(256), null)
- `DangLamViec` (bit)
- `NgayTao` (datetime)
- `CapNhatLuc` (datetime, null)

## 4) `CaLamViec` (phân ca theo thứ)
- `Id` (GUID, PK)
- `NhanVienId` (GUID, FK → `NhanVien.Id`)
- `ThuTrongTuan` (int, 0..6) — 0=Chủ nhật … 6=Thứ bảy
- `GioBatDau` (varchar(8)) — "HH:mm"
- `GioKetThuc` (varchar(8)) — "HH:mm"

## 5) `LichHen` (đặt lịch)
- `Id` (GUID, PK)
- `TaiKhoanId` (GUID, FK → `TaiKhoan.Id`)
- `DichVuId` (GUID, FK → `DichVu.Id`)
- `NhanVienId` (GUID, null, FK → `NhanVien.Id`)
- `ThoiGianBatDau` (datetime)
- `ThoiGianKetThuc` (datetime)
- `GhiChu` (varchar(500), null)
- `TrangThai` (int)
  - 0: Chờ xác nhận
  - 1: Đã xác nhận
  - 2: Hoàn thành
  - 3: Đã hủy
  - 4: Từ chối
- `LyDoTuChoi` (varchar(500), null)
- `NgayTao` (datetime)
- `CapNhatLuc` (datetime, null)

## 6) `DanhGia` (đánh giá dịch vụ)
- `Id` (GUID, PK)
- `LichHenId` (GUID, unique, FK → `LichHen.Id`) — 1 lịch hẹn chỉ 1 đánh giá
- `TaiKhoanId` (GUID, FK → `TaiKhoan.Id`)
- `DichVuId` (GUID, FK → `DichVu.Id`)
- `SoSao` (int, 1..5)
- `NoiDung` (varchar(2000), null)
- `NgayTao` (datetime)

## 7) `KhuyenMai` (khuyến mãi)
- `Id` (GUID, PK)
- `Ten` (varchar(200))
- `MoTa` (varchar(2000), null)
- `PhanTramGiam` (decimal(18,2), null)
- `SoTienGiam` (decimal(18,2), null)
- `DieuKienToiThieu` (decimal(18,2), null)
- `TuNgay` (datetime)
- `DenNgay` (datetime)
- `HienThi` (bit)

## 8) `SanPham` (mỹ phẩm)
- `Id` (GUID, PK)
- `Ten` (varchar(200))
- `MoTa` (varchar(2000), null)
- `Gia` (decimal(18,2))
- `TonKho` (int)
- `HinhAnhUrl` (varchar(1000), null)
- `DangBan` (bit)
- `NgayTao` (datetime)
- `CapNhatLuc` (datetime, null)

## 9) `DonHang` (đơn hàng)
- `Id` (GUID, PK)
- `TaiKhoanId` (GUID, FK → `TaiKhoan.Id`)
- `TongTien` (decimal(18,2))
- `TrangThai` (int)
  - 0: Chờ xác nhận
  - 1: Đang xử lý
  - 2: Đang giao
  - 3: Hoàn thành
  - 4: Đã hủy
- `TenNguoiNhan` (varchar(200))
- `SoDienThoaiNhan` (varchar(32))
- `DiaChiGiao` (varchar(500))
- `GhiChu` (varchar(500), null)
- `NgayTao` (datetime)

## 10) `DonHangChiTiet` (chi tiết đơn)
- `Id` (GUID, PK)
- `DonHangId` (GUID, FK → `DonHang.Id`)
- `SanPhamId` (GUID, FK → `SanPham.Id`)
- `SoLuong` (int)
- `DonGia` (decimal(18,2))
- `ThanhTien` (decimal(18,2))

## 11) `TrangNoiDung` (quản lý nội dung website)
- `Id` (GUID, PK)
- `MaTrang` (varchar(100), unique) — vd: `TRANG_CHU`, `LIEN_HE`
- `TieuDe` (varchar(200))
- `NoiDungHtml` (longtext)
- `CapNhatLuc` (datetime)

