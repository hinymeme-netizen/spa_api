using System.Data;
using Microsoft.EntityFrameworkCore;
using SpaApi.Domain;
using SpaApi.Security;

namespace SpaApi.Data;

public static class DbInitializer
{
  public static async Task InitializeAsync(IServiceProvider services)
  {
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SpaDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    // Với MySQL nên ưu tiên migrations. Nếu chưa tạo migrations, lệnh này vẫn tạo schema dựa trên model.
    await db.Database.EnsureCreatedAsync();

    // Patch idempotent cho các thay đổi schema xảy ra sau khi bảng đã tồn tại.
    // EnsureCreated KHÔNG sửa cột trên bảng đã có nên cần kiểm tra & ALTER tay.
    await ApplyAdHocMigrationsAsync(db);

    var hasAdmin = await db.TaiKhoans.AnyAsync(x => x.VaiTro == VaiTroTaiKhoan.Admin);
    if (!hasAdmin)
    {
      var admin = new TaiKhoan
      {
        Id = Guid.NewGuid(),
        Email = "admin@spa.local",
        HoTen = "Admin",
        MatKhauHash = hasher.Hash("Admin@12345"),
        VaiTro = VaiTroTaiKhoan.Admin,
        KichHoat = true
      };
      var user = new TaiKhoan
      {
        Id = Guid.NewGuid(),
        Email = "user@spa.local",
        HoTen = "Nguyen Van A",
        MatKhauHash = hasher.Hash("User@12345"),
        VaiTro = VaiTroTaiKhoan.User,
        KichHoat = true
      };
      db.TaiKhoans.AddRange(admin, user);
      await db.SaveChangesAsync();

      // Dịch vụ mẫu
      var dichVu1 = new DichVu
      {
        Id = Guid.NewGuid(),
        Ten = "Massage thư giãn",
        MoTa = "Massage toàn thân giúp thư giãn và giảm stress.",
        Gia = 300000,
        ThoiLuongPhut = 60,
        HienThi = true
      };
      var dichVu2 = new DichVu
      {
        Id = Guid.NewGuid(),
        Ten = "Chăm sóc da mặt",
        MoTa = "Liệu trình chăm sóc da mặt chuyên sâu.",
        Gia = 400000,
        ThoiLuongPhut = 45,
        HienThi = true
      };
      db.DichVus.AddRange(dichVu1, dichVu2);

      // Sản phẩm mẫu
      var sanPham1 = new SanPham
      {
        Id = Guid.NewGuid(),
        Ten = "Sữa rửa mặt Spa",
        MoTa = "Sữa rửa mặt dịu nhẹ cho mọi loại da.",
        Gia = 120000,
        TonKho = 50,
        DangBan = true
      };
      var sanPham2 = new SanPham
      {
        Id = Guid.NewGuid(),
        Ten = "Kem dưỡng ẩm Spa",
        MoTa = "Kem dưỡng ẩm giúp da mềm mịn.",
        Gia = 250000,
        TonKho = 30,
        DangBan = true
      };
      db.SanPhams.AddRange(sanPham1, sanPham2);

      // Nhân viên mẫu
      var nhanVien1 = new NhanVien
      {
        Id = Guid.NewGuid(),
        HoTen = "Tran Thi B",
        SoDienThoai = "0901234567",
        ChuyenMon = "Massage",
        DangLamViec = true
      };
      var nhanVien2 = new NhanVien
      {
        Id = Guid.NewGuid(),
        HoTen = "Le Van C",
        SoDienThoai = "0907654321",
        ChuyenMon = "Chăm sóc da",
        DangLamViec = true
      };
      db.NhanViens.AddRange(nhanVien1, nhanVien2);

      // Khuyến mãi mẫu
      var km1 = new KhuyenMai
      {
        Id = Guid.NewGuid(),
        Ten = "Giảm 10% dịch vụ",
        MoTa = "Giảm giá cho tất cả dịch vụ trong tháng 3.",
        PhanTramGiam = 10,
        TuNgay = DateTime.UtcNow.AddDays(-5),
        DenNgay = DateTime.UtcNow.AddDays(25),
        HienThi = true
      };
      db.KhuyenMais.Add(km1);

      // Ca làm việc mẫu
      var ca1 = new CaLamViec
      {
        Id = Guid.NewGuid(),
        NhanVienId = nhanVien1.Id,
        ThuTrongTuan = 1,
        GioBatDau = "08:00",
        GioKetThuc = "16:00"
      };
      var ca2 = new CaLamViec
      {
        Id = Guid.NewGuid(),
        NhanVienId = nhanVien2.Id,
        ThuTrongTuan = 3,
        GioBatDau = "10:00",
        GioKetThuc = "18:00"
      };
      db.CaLamViecs.AddRange(ca1, ca2);

      await db.SaveChangesAsync();
    }
  }

  // ---- Ad-hoc schema patches ----
  // Mỗi patch tự kiểm tra trạng thái hiện tại trước khi ALTER nên an toàn khi chạy lại.
  private static async Task ApplyAdHocMigrationsAsync(SpaDbContext db)
  {
    await EnsureKhuyenMaiDichVuIdAsync(db);
    await EnsureDichVuHinhAnhUrlAsync(db);
    await EnsureTaiLieuTableAsync(db);
    await DropTrangNoiDungIfExistsAsync(db);
    await EnsureNhanVienTaiKhoanIdAsync(db);
    await EnsureCaLamViecNgayAsync(db);
    await EnsureCaLamViecHieuLucAsync(db);
    await EnsureTaiKhoanGoogleSubAsync(db);
    await EnsureTaiKhoanPasswordResetAsync(db);
    await EnsureDanhMucDichVuTableAsync(db);
    await EnsureDichVuDanhMucIdAsync(db);
    await SeedDefaultDanhMucAsync(db);
  }

  private static async Task EnsureDanhMucDichVuTableAsync(SpaDbContext db)
  {
    if (await TableExistsAsync(db, "DanhMucDichVu")) return;

    Console.WriteLine("[Migration] Creating DanhMucDichVu table...");
    await db.Database.ExecuteSqlRawAsync(@"
      CREATE TABLE DanhMucDichVu (
        Id char(36) NOT NULL PRIMARY KEY,
        Ten varchar(128) NOT NULL,
        Slug varchar(64) NULL,
        MoTa varchar(500) NULL,
        Icon varchar(64) NULL,
        ThuTu int NOT NULL DEFAULT 0,
        HienThi tinyint(1) NOT NULL DEFAULT 1,
        NgayTao datetime(6) NOT NULL,
        UNIQUE INDEX IX_DanhMucDichVu_Slug (Slug),
        INDEX IX_DanhMucDichVu_ThuTu (ThuTu)
      ) CHARSET=utf8mb4");
    Console.WriteLine("[Migration] DanhMucDichVu created.");
  }

  private static async Task EnsureDichVuDanhMucIdAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "DichVu", "DanhMucId")) return;

    Console.WriteLine("[Migration] Adding DichVu.DanhMucId column...");
    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE DichVu ADD COLUMN DanhMucId char(36) NULL");

    if (!await IndexExistsAsync(db, "DichVu", "IX_DichVu_DanhMucId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE DichVu ADD INDEX IX_DichVu_DanhMucId (DanhMucId)");
    }

    if (!await ConstraintExistsAsync(db, "DichVu", "FK_DichVu_DanhMucDichVu_DanhMucId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE DichVu ADD CONSTRAINT FK_DichVu_DanhMucDichVu_DanhMucId " +
        "FOREIGN KEY (DanhMucId) REFERENCES DanhMucDichVu(Id) ON DELETE SET NULL");
    }
    Console.WriteLine("[Migration] DichVu.DanhMucId added.");
  }

  private static async Task SeedDefaultDanhMucAsync(SpaDbContext db)
  {
    if (await db.DanhMucDichVus.AnyAsync()) return;

    Console.WriteLine("[Seed] Inserting default DanhMucDichVu...");
    db.DanhMucDichVus.AddRange(
      new DanhMucDichVu { Id = Guid.NewGuid(), Ten = "Massage", Slug = "massage",
        MoTa = "Các liệu trình massage thư giãn, trị liệu cơ thể.", Icon = "Hand", ThuTu = 1, HienThi = true,
        NgayTao = DateTime.UtcNow },
      new DanhMucDichVu { Id = Guid.NewGuid(), Ten = "Trị liệu da", Slug = "tri-lieu-da",
        MoTa = "Liệu trình điều trị các vấn đề về da: mụn, nám, sẹo, lão hoá.", Icon = "Stethoscope", ThuTu = 2, HienThi = true,
        NgayTao = DateTime.UtcNow },
      new DanhMucDichVu { Id = Guid.NewGuid(), Ten = "Chăm sóc dưỡng da", Slug = "cham-soc-duong-da",
        MoTa = "Chăm sóc da định kỳ, dưỡng ẩm, làm sáng và phục hồi.", Icon = "Sparkles", ThuTu = 3, HienThi = true,
        NgayTao = DateTime.UtcNow }
    );
    await db.SaveChangesAsync();
    Console.WriteLine("[Seed] Default DanhMucDichVu inserted.");
  }

  private static async Task EnsureTaiKhoanPasswordResetAsync(SpaDbContext db)
  {
    if (!await ColumnExistsAsync(db, "TaiKhoan", "PasswordResetTokenHash"))
    {
      Console.WriteLine("[Migration] Adding TaiKhoan.PasswordResetTokenHash column...");
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE TaiKhoan ADD COLUMN PasswordResetTokenHash varchar(128) NULL");
    }
    if (!await ColumnExistsAsync(db, "TaiKhoan", "PasswordResetExpires"))
    {
      Console.WriteLine("[Migration] Adding TaiKhoan.PasswordResetExpires column...");
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE TaiKhoan ADD COLUMN PasswordResetExpires datetime(6) NULL");
    }
  }

  private static async Task EnsureTaiKhoanGoogleSubAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "TaiKhoan", "GoogleSub")) return;

    Console.WriteLine("[Migration] Adding TaiKhoan.GoogleSub column...");
    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE TaiKhoan ADD COLUMN GoogleSub varchar(64) NULL");
    if (!await IndexExistsAsync(db, "TaiKhoan", "IX_TaiKhoan_GoogleSub"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE TaiKhoan ADD UNIQUE INDEX IX_TaiKhoan_GoogleSub (GoogleSub)");
    }
    Console.WriteLine("[Migration] TaiKhoan.GoogleSub added.");
  }

  private static async Task EnsureCaLamViecHieuLucAsync(SpaDbContext db)
  {
    if (!await ColumnExistsAsync(db, "CaLamViec", "HieuLucTuNgay"))
    {
      Console.WriteLine("[Migration] Adding CaLamViec.HieuLucTuNgay column...");
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE CaLamViec ADD COLUMN HieuLucTuNgay DATE NULL");
    }
    if (!await ColumnExistsAsync(db, "CaLamViec", "HieuLucDenNgay"))
    {
      Console.WriteLine("[Migration] Adding CaLamViec.HieuLucDenNgay column...");
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE CaLamViec ADD COLUMN HieuLucDenNgay DATE NULL");
    }
    if (!await ColumnExistsAsync(db, "CaLamViec", "LaCaNghi"))
    {
      Console.WriteLine("[Migration] Adding CaLamViec.LaCaNghi column...");
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE CaLamViec ADD COLUMN LaCaNghi tinyint(1) NOT NULL DEFAULT 0");
    }
  }

  private static async Task EnsureCaLamViecNgayAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "CaLamViec", "Ngay")) return;

    Console.WriteLine("[Migration] Adding CaLamViec.Ngay column...");
    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE CaLamViec ADD COLUMN Ngay DATE NULL");
    if (!await IndexExistsAsync(db, "CaLamViec", "IX_CaLamViec_NhanVienId_Ngay"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE CaLamViec ADD INDEX IX_CaLamViec_NhanVienId_Ngay (NhanVienId, Ngay)");
    }
    Console.WriteLine("[Migration] CaLamViec.Ngay added.");
  }

  private static async Task EnsureNhanVienTaiKhoanIdAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "NhanVien", "TaiKhoanId")) return;

    Console.WriteLine("[Migration] Adding NhanVien.TaiKhoanId column...");
    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE NhanVien ADD COLUMN TaiKhoanId char(36) NULL");

    if (!await IndexExistsAsync(db, "NhanVien", "IX_NhanVien_TaiKhoanId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE NhanVien ADD UNIQUE INDEX IX_NhanVien_TaiKhoanId (TaiKhoanId)");
    }

    if (!await ConstraintExistsAsync(db, "NhanVien", "FK_NhanVien_TaiKhoan_TaiKhoanId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE NhanVien ADD CONSTRAINT FK_NhanVien_TaiKhoan_TaiKhoanId " +
        "FOREIGN KEY (TaiKhoanId) REFERENCES TaiKhoan(Id) ON DELETE SET NULL");
    }

    Console.WriteLine("[Migration] NhanVien.TaiKhoanId added.");
  }

  private static async Task EnsureTaiLieuTableAsync(SpaDbContext db)
  {
    if (await TableExistsAsync(db, "TaiLieu")) return;

    Console.WriteLine("[Migration] Creating TaiLieu table...");
    await db.Database.ExecuteSqlRawAsync(@"
      CREATE TABLE TaiLieu (
        Id char(36) NOT NULL PRIMARY KEY,
        TieuDe varchar(300) NOT NULL,
        NoiDung longtext NOT NULL,
        Nguon varchar(100) NULL,
        SoChunk int NOT NULL DEFAULT 0,
        TrangThai varchar(20) NOT NULL DEFAULT 'ChoXuLy',
        NgayTao datetime(6) NOT NULL,
        CapNhatLuc datetime(6) NULL,
        INDEX IX_TaiLieu_NgayTao (NgayTao)
      ) CHARSET=utf8mb4");
    Console.WriteLine("[Migration] TaiLieu table created.");
  }

  private static async Task DropTrangNoiDungIfExistsAsync(SpaDbContext db)
  {
    if (!await TableExistsAsync(db, "TrangNoiDung")) return;

    Console.WriteLine("[Migration] Dropping legacy TrangNoiDung table...");
    await db.Database.ExecuteSqlRawAsync("DROP TABLE TrangNoiDung");
    Console.WriteLine("[Migration] TrangNoiDung dropped.");
  }

  private static async Task<bool> TableExistsAsync(SpaDbContext db, string table)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = @t";
    AddParam(cmd, "@t", table);
    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    return count > 0;
  }

  private static async Task EnsureDichVuHinhAnhUrlAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "DichVu", "HinhAnhUrl")) return;

    Console.WriteLine("[Migration] Adding DichVu.HinhAnhUrl column...");
    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE DichVu ADD COLUMN HinhAnhUrl varchar(1000) NULL");
    Console.WriteLine("[Migration] DichVu.HinhAnhUrl added.");
  }

  private static async Task EnsureKhuyenMaiDichVuIdAsync(SpaDbContext db)
  {
    if (await ColumnExistsAsync(db, "KhuyenMai", "DichVuId")) return;

    Console.WriteLine("[Migration] Adding KhuyenMai.DichVuId column...");

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE KhuyenMai ADD COLUMN DichVuId char(36) NULL");

    if (!await IndexExistsAsync(db, "KhuyenMai", "IX_KhuyenMai_DichVuId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE KhuyenMai ADD INDEX IX_KhuyenMai_DichVuId (DichVuId)");
    }

    if (!await ConstraintExistsAsync(db, "KhuyenMai", "FK_KhuyenMai_DichVu_DichVuId"))
    {
      await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE KhuyenMai ADD CONSTRAINT FK_KhuyenMai_DichVu_DichVuId " +
        "FOREIGN KEY (DichVuId) REFERENCES DichVu(Id) ON DELETE SET NULL");
    }

    Console.WriteLine("[Migration] KhuyenMai.DichVuId added.");
  }

  private static async Task<bool> ColumnExistsAsync(SpaDbContext db, string table, string column)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.columns
                        WHERE table_schema = DATABASE()
                          AND table_name = @t
                          AND column_name = @c";
    AddParam(cmd, "@t", table);
    AddParam(cmd, "@c", column);
    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    return count > 0;
  }

  private static async Task<bool> IndexExistsAsync(SpaDbContext db, string table, string indexName)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.statistics
                        WHERE table_schema = DATABASE()
                          AND table_name = @t
                          AND index_name = @i";
    AddParam(cmd, "@t", table);
    AddParam(cmd, "@i", indexName);
    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    return count > 0;
  }

  private static async Task<bool> ConstraintExistsAsync(SpaDbContext db, string table, string constraintName)
  {
    var conn = db.Database.GetDbConnection();
    if (conn.State != ConnectionState.Open) await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.table_constraints
                        WHERE table_schema = DATABASE()
                          AND table_name = @t
                          AND constraint_name = @c";
    AddParam(cmd, "@t", table);
    AddParam(cmd, "@c", constraintName);
    var count = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    return count > 0;
  }

  private static void AddParam(System.Data.Common.DbCommand cmd, string name, string value)
  {
    var p = cmd.CreateParameter();
    p.ParameterName = name;
    p.Value = value;
    cmd.Parameters.Add(p);
  }
}

