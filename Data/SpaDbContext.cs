using Microsoft.EntityFrameworkCore;
using SpaApi.Domain;

namespace SpaApi.Data;

public sealed class SpaDbContext : DbContext
{
  public SpaDbContext(DbContextOptions<SpaDbContext> options) : base(options) { }

  public DbSet<TaiKhoan> TaiKhoans => Set<TaiKhoan>();
  public DbSet<DanhMucDichVu> DanhMucDichVus => Set<DanhMucDichVu>();
  public DbSet<DichVu> DichVus => Set<DichVu>();
  public DbSet<NhanVien> NhanViens => Set<NhanVien>();
  public DbSet<CaLamViec> CaLamViecs => Set<CaLamViec>();
  public DbSet<LichHen> LichHens => Set<LichHen>();
  public DbSet<DanhGia> DanhGias => Set<DanhGia>();
  public DbSet<KhuyenMai> KhuyenMais => Set<KhuyenMai>();
  public DbSet<SanPham> SanPhams => Set<SanPham>();
  public DbSet<DonHang> DonHangs => Set<DonHang>();
  public DbSet<DonHangChiTiet> DonHangChiTiets => Set<DonHangChiTiet>();
  public DbSet<BaiViet> BaiViets => Set<BaiViet>();
  public DbSet<TaiLieu> TaiLieus => Set<TaiLieu>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<TaiKhoan>(b =>
    {
      b.ToTable("TaiKhoan");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.Email).IsUnique();
      b.HasIndex(x => x.SoDienThoai);
      b.HasIndex(x => x.GoogleSub).IsUnique().HasFilter("GoogleSub IS NOT NULL");
    });

    modelBuilder.Entity<DanhMucDichVu>(b =>
    {
      b.ToTable("DanhMucDichVu");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.Slug).IsUnique();
      b.HasIndex(x => x.ThuTu);
    });

    modelBuilder.Entity<DichVu>(b =>
    {
      b.ToTable("DichVu");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.Ten);
      b.HasIndex(x => x.DanhMucId);
      b.Property(x => x.Gia).HasPrecision(18, 2);
      b.HasOne(x => x.DanhMuc).WithMany(x => x.DichVus).HasForeignKey(x => x.DanhMucId)
        .OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<NhanVien>(b =>
    {
      b.ToTable("NhanVien");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.SoDienThoai);
      b.HasIndex(x => x.TaiKhoanId).IsUnique().HasFilter("TaiKhoanId IS NOT NULL");
      b.HasOne(x => x.TaiKhoan).WithMany().HasForeignKey(x => x.TaiKhoanId)
        .OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<CaLamViec>(b =>
    {
      b.ToTable("CaLamViec");
      b.HasKey(x => x.Id);
      b.HasIndex(x => new { x.NhanVienId, x.ThuTrongTuan });
      b.HasOne(x => x.NhanVien).WithMany(x => x.CaLamViecs).HasForeignKey(x => x.NhanVienId);
    });

    modelBuilder.Entity<LichHen>(b =>
    {
      b.ToTable("LichHen");
      b.HasKey(x => x.Id);
      b.HasIndex(x => new { x.TaiKhoanId, x.ThoiGianBatDau });
      b.HasIndex(x => new { x.NhanVienId, x.ThoiGianBatDau });
      b.HasOne(x => x.TaiKhoan).WithMany(x => x.LichHens).HasForeignKey(x => x.TaiKhoanId);
      b.HasOne(x => x.DichVu).WithMany(x => x.LichHens).HasForeignKey(x => x.DichVuId);
      b.HasOne(x => x.NhanVien).WithMany(x => x.LichHens).HasForeignKey(x => x.NhanVienId);
    });

    modelBuilder.Entity<DanhGia>(b =>
    {
      b.ToTable("DanhGia");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.LichHenId).IsUnique();
      b.HasOne(x => x.LichHen).WithOne(x => x.DanhGia).HasForeignKey<DanhGia>(x => x.LichHenId);
      b.HasOne(x => x.TaiKhoan).WithMany().HasForeignKey(x => x.TaiKhoanId);
      b.HasOne(x => x.DichVu).WithMany().HasForeignKey(x => x.DichVuId);
    });

    modelBuilder.Entity<KhuyenMai>(b =>
    {
      b.ToTable("KhuyenMai");
      b.HasKey(x => x.Id);
      b.Property(x => x.PhanTramGiam).HasPrecision(18, 2);
      b.Property(x => x.SoTienGiam).HasPrecision(18, 2);
      b.Property(x => x.DieuKienToiThieu).HasPrecision(18, 2);
      b.HasIndex(x => x.DichVuId);
      b.HasOne(x => x.DichVu).WithMany().HasForeignKey(x => x.DichVuId)
        .OnDelete(DeleteBehavior.SetNull);
    });

    modelBuilder.Entity<SanPham>(b =>
    {
      b.ToTable("SanPham");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.Ten);
      b.Property(x => x.Gia).HasPrecision(18, 2);
    });

    modelBuilder.Entity<DonHang>(b =>
    {
      b.ToTable("DonHang");
      b.HasKey(x => x.Id);
      b.HasIndex(x => new { x.TaiKhoanId, x.NgayTao });
      b.Property(x => x.TongTien).HasPrecision(18, 2);
      b.HasOne(x => x.TaiKhoan).WithMany(x => x.DonHangs).HasForeignKey(x => x.TaiKhoanId);
    });

    modelBuilder.Entity<DonHangChiTiet>(b =>
    {
      b.ToTable("DonHangChiTiet");
      b.HasKey(x => x.Id);
      b.HasIndex(x => new { x.DonHangId, x.SanPhamId });
      b.Property(x => x.DonGia).HasPrecision(18, 2);
      b.Property(x => x.ThanhTien).HasPrecision(18, 2);
      b.HasOne(x => x.DonHang).WithMany(x => x.ChiTiets).HasForeignKey(x => x.DonHangId);
      b.HasOne(x => x.SanPham).WithMany(x => x.DonHangChiTiets).HasForeignKey(x => x.SanPhamId);
    });

    modelBuilder.Entity<BaiViet>(b =>
    {
      b.ToTable("BaiViet");
      b.HasKey(x => x.Id);
    });

    modelBuilder.Entity<TaiLieu>(b =>
    {
      b.ToTable("TaiLieu");
      b.HasKey(x => x.Id);
      b.HasIndex(x => x.NgayTao);
    });
  }
}

