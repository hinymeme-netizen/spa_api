# Danh sách API (input/output)

Base URL: `/`

## 1) Auth / Tài khoản

### POST `api/auth/register`
- **Input (JSON)**:
  - `email` (string, required)
  - `matKhau` (string, required)
  - `hoTen` (string, required)
  - `soDienThoai` (string, optional)
- **Output 200 (JSON)**: `AuthResponse`
  - `id` (guid)
  - `email` (string)
  - `hoTen` (string)
  - `soDienThoai` (string|null)
  - `vaiTro` ("User"|"Admin")
  - `accessToken` (string JWT)
- **Lỗi**: 409 nếu email tồn tại.

### POST `api/auth/login`
- **Input**: `email`, `matKhau`
- **Output 200**: `AuthResponse`
- **Lỗi**: 401 sai thông tin/khóa tài khoản.

### GET `api/auth/me` (Bearer JWT)
- **Output 200**:
  - `id`, `email`, `hoTen`, `soDienThoai`, `vaiTro`, `kichHoat`, `ngayTao`

### PUT `api/auth/me` (Bearer JWT)
- **Input**: `hoTen`, `soDienThoai`
- **Output**: 204

### POST `api/auth/change-password` (Bearer JWT)
- **Input**: `matKhauCu`, `matKhauMoi`
- **Output**: 204
- **Lỗi**: 400 nếu mật khẩu cũ sai

## 2) Dịch vụ / Bảng giá

### GET `api/dich-vu`
- **Query**: `hienThi` (bool, optional)
- **Output 200**: `DichVuResponse[]`

### GET `api/dich-vu/{id}`
- **Output 200**: `DichVuResponse`

### POST `api/dich-vu` (Admin)
- **Input**: `ten`, `moTa`, `gia`, `thoiLuongPhut`, `hienThi`
- **Output 201/200**: `DichVuResponse`

### PUT `api/dich-vu/{id}` (Admin)
- **Input**: như create
- **Output**: 204

### DELETE `api/dich-vu/{id}` (Admin)
- **Output**: 204

## 3) Nhân viên & Phân ca

### GET `api/nhan-vien`
- **Query**: `dangLamViec` (bool, optional)
- **Output**: `NhanVienResponse[]`

### POST `api/nhan-vien` (Admin)
- **Input**: `hoTen`, `soDienThoai`, `chuyenMon`, `dangLamViec`
- **Output**: `NhanVienResponse`

### PUT `api/nhan-vien/{id}` (Admin) → 204
### DELETE `api/nhan-vien/{id}` (Admin) → 204

### GET `api/nhan-vien/{nhanVienId}/ca-lam-viec`
- **Output**: `CaLamViecResponse[]`

### POST `api/nhan-vien/{nhanVienId}/ca-lam-viec` (Admin)
- **Input**: `thuTrongTuan` (0..6), `gioBatDau` ("HH:mm"), `gioKetThuc` ("HH:mm")
- **Output**: `CaLamViecResponse`

### DELETE `api/nhan-vien/ca-lam-viec/{caId}` (Admin) → 204

## 4) Đặt lịch & Quản lý lịch hẹn

### POST `api/lich-hen` (User)
- **Input**:
  - `dichVuId` (guid)
  - `nhanVienId` (guid|null)
  - `thoiGianBatDau` (datetime)
  - `ghiChu` (string|null)
- **Output 200**: `LichHenResponse`
- **Rule**:
  - Trong giờ làm việc (`Spa:GioLamViec` trong `appsettings.json`)
  - Theo bước phút `buocPhutDatLich`

### GET `api/lich-hen/me` (User)
- **Query**: `trangThai` (enum, optional)
- **Output**: `LichHenResponse[]`

### PUT `api/lich-hen/{id}` (User)
- **Input**: `nhanVienId?`, `thoiGianBatDau?`, `ghiChu?`
- **Output**: 204
- **Rule**: chỉ khi chưa quá hạn `soGioToiThieuDeHuy` và trạng thái chưa kết thúc.

### POST `api/lich-hen/{id}/huy` (User) → 204

### GET `api/lich-hen/admin` (Admin)
- **Query**: `tuNgay`, `denNgay`, `dichVuId`, `trangThai`
- **Output**: `LichHenResponse[]`

### POST `api/lich-hen/admin/{id}/xac-nhan` (Admin) → 204
- **Input**: `nhanVienId` (guid|null)

### POST `api/lich-hen/admin/{id}/tu-choi` (Admin) → 204
- **Input**: `lyDoTuChoi` (string)

### POST `api/lich-hen/admin/{id}/gan-nhan-vien` (Admin) → 204
- **Input**: `nhanVienId` (guid)

### POST `api/lich-hen/admin/{id}/hoan-thanh` (Admin) → 204

## 5) Đánh giá dịch vụ

### GET `api/danh-gia/dich-vu/{dichVuId}`
- **Output**: `DanhGiaResponse[]`

### POST `api/danh-gia` (User)
- **Input**: `lichHenId`, `soSao` (1..5), `noiDung`
- **Output**: `DanhGiaResponse`
- **Rule**: chỉ khi lịch hẹn `HoanThanh` và chưa đánh giá.

## 6) Khuyến mãi

### GET `api/khuyen-mai`
- **Query**: `conHieuLuc=true` để lọc theo thời gian + `hienThi`
- **Output**: `KhuyenMaiResponse[]`

### POST/PUT/DELETE `api/khuyen-mai` (Admin)
- **Input**: `ten`, `moTa`, `phanTramGiam`, `soTienGiam`, `dieuKienToiThieu`, `tuNgay`, `denNgay`, `hienThi`

## 7) Bán mỹ phẩm (Sản phẩm & Đơn hàng)

### GET `api/san-pham`
- **Query**: `dangBan` (bool, optional)
- **Output**: `SanPhamResponse[]`

### GET `api/san-pham/{id}` → `SanPhamResponse`
### POST/PUT/DELETE `api/san-pham` (Admin)

### POST `api/don-hang` (User)
- **Input**:
  - `tenNguoiNhan`, `soDienThoaiNhan`, `diaChiGiao`, `ghiChu?`
  - `chiTiets`: `[{ sanPhamId, soLuong }]`
- **Output**: `DonHangResponse`
- **Rule**: trừ tồn kho theo số lượng đặt.

### GET `api/don-hang/me` (User) → `DonHangResponse[]`
### GET `api/don-hang/admin` (Admin) → `DonHangResponse[]`
- **Query**: `taiKhoanId` (optional)

### POST `api/don-hang/admin/{id}/trang-thai` (Admin) → 204
- **Input**: `trangThai` ("ChoXacNhan"|"DangXuLy"|"DangGiao"|"HoanThanh"|"DaHuy")

## 8) Quản lý nội dung website

### GET `api/noi-dung/{maTrang}`
- **Output**: `TrangNoiDungResponse`

### PUT `api/noi-dung` (Admin) → 204
- **Input**: `maTrang`, `tieuDe`, `noiDungHtml`

## 9) Thống kê – báo cáo (Admin)

### GET `api/thong-ke/lich-hen`
- **Query**: `tuNgay`, `denNgay`
- **Output**: `ThongKeSoLuongTheoNgayResponse[]`

### GET `api/thong-ke/doanh-thu`
- **Query**: `tuNgay`, `denNgay`
- **Output**: `ThongKeDoanhThuResponse`
  - `doanhThuDichVu` (sum lịch hoàn thành * giá dịch vụ)
  - `doanhThuDonHang` (sum đơn hàng hoàn thành)
  - `tongDoanhThu`

### GET `api/thong-ke/top-dich-vu`
- **Query**: `tuNgay`, `denNgay`, `top` (default 5)
- **Output**: `ThongKeTopDichVuResponse[]`

## 10) Quản lý khách hàng (Admin)

### GET `api/admin/tai-khoan`
- **Output**: `TaiKhoanAdminResponse[]`

### GET `api/admin/tai-khoan/{id}`
- **Output**: `TaiKhoanAdminResponse`

### PUT `api/admin/tai-khoan/{id}` → 204
- **Input**: `hoTen`, `soDienThoai?`, `vaiTro` ("Admin"|"User"), `kichHoat` (bool)

