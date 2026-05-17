using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Security;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/nhan-vien")]
public sealed class NhanVienController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly IPasswordHasher _hasher;

  public NhanVienController(SpaDbContext db, IPasswordHasher hasher)
  {
    _db = db;
    _hasher = hasher;
  }

  [HttpGet]
  [AllowAnonymous]
  public async Task<ActionResult<List<NhanVienResponse>>> List([FromQuery] bool? dangLamViec, CancellationToken ct)
  {
    var q = _db.NhanViens.AsNoTracking().Include(x => x.TaiKhoan).OrderBy(x => x.HoTen).AsQueryable();
    if (dangLamViec is not null) q = q.Where(x => x.DangLamViec == dangLamViec);

    var items = await q.Select(x => new NhanVienResponse(
        x.Id, x.HoTen, x.SoDienThoai, x.ChuyenMon, x.DangLamViec,
        x.TaiKhoanId, x.TaiKhoan != null ? x.TaiKhoan.Email : null))
      .ToListAsync(ct);
    return Ok(items);
  }

  [HttpPost]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<NhanVienResponse>> Create(UpsertNhanVienRequest req, CancellationToken ct)
  {
    Guid? taiKhoanId = null;
    string? linkedEmail = null;

    // Nếu admin nhập email + matKhau → tạo TaiKhoan role NhanVien, link với NhanVien sắp tạo.
    if (!string.IsNullOrWhiteSpace(req.Email) || !string.IsNullOrWhiteSpace(req.MatKhau))
    {
      if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.MatKhau))
        return BadRequest(new { message = "Cần nhập đủ Email và Mật khẩu để tạo tài khoản đăng nhập cho nhân viên." });

      var email = req.Email.Trim().ToLowerInvariant();
      var exists = await _db.TaiKhoans.AnyAsync(x => x.Email.ToLower() == email, ct);
      if (exists) return Conflict(new { message = "Email đã được sử dụng cho tài khoản khác." });

      var tk = new TaiKhoan
      {
        Id = Guid.NewGuid(),
        Email = email,
        HoTen = req.HoTen.Trim(),
        SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim(),
        MatKhauHash = _hasher.Hash(req.MatKhau),
        VaiTro = VaiTroTaiKhoan.NhanVien,
        KichHoat = true
      };
      _db.TaiKhoans.Add(tk);
      taiKhoanId = tk.Id;
      linkedEmail = email;
    }

    var nv = new NhanVien
    {
      Id = Guid.NewGuid(),
      HoTen = req.HoTen.Trim(),
      SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim(),
      ChuyenMon = string.IsNullOrWhiteSpace(req.ChuyenMon) ? null : req.ChuyenMon.Trim(),
      DangLamViec = req.DangLamViec,
      TaiKhoanId = taiKhoanId
    };
    _db.NhanViens.Add(nv);
    await _db.SaveChangesAsync(ct);
    return Ok(new NhanVienResponse(nv.Id, nv.HoTen, nv.SoDienThoai, nv.ChuyenMon, nv.DangLamViec, nv.TaiKhoanId, linkedEmail));
  }

  [HttpPut("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Update(Guid id, UpsertNhanVienRequest req, CancellationToken ct)
  {
    var nv = await _db.NhanViens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (nv is null) return NotFound();

    nv.HoTen = req.HoTen.Trim();
    nv.SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim();
    nv.ChuyenMon = string.IsNullOrWhiteSpace(req.ChuyenMon) ? null : req.ChuyenMon.Trim();
    nv.DangLamViec = req.DangLamViec;
    nv.CapNhatLuc = DateTime.UtcNow;

    // Nếu NhanVien chưa có account và admin nhập Email + MatKhau → tạo + link.
    if (nv.TaiKhoanId is null
      && !string.IsNullOrWhiteSpace(req.Email)
      && !string.IsNullOrWhiteSpace(req.MatKhau))
    {
      var email = req.Email.Trim().ToLowerInvariant();
      var exists = await _db.TaiKhoans.AnyAsync(x => x.Email.ToLower() == email, ct);
      if (exists) return Conflict(new { message = "Email đã được sử dụng cho tài khoản khác." });

      var tk = new TaiKhoan
      {
        Id = Guid.NewGuid(),
        Email = email,
        HoTen = nv.HoTen,
        SoDienThoai = nv.SoDienThoai,
        MatKhauHash = _hasher.Hash(req.MatKhau),
        VaiTro = VaiTroTaiKhoan.NhanVien,
        KichHoat = true
      };
      _db.TaiKhoans.Add(tk);
      nv.TaiKhoanId = tk.Id;
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// Admin: sửa email / reset mật khẩu / khoá tài khoản đăng nhập của nhân viên đã link.
  /// </summary>
  [HttpPut("{id:guid}/tai-khoan")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> UpdateTaiKhoan(Guid id, UpdateNhanVienTaiKhoanRequest req, CancellationToken ct)
  {
    var nv = await _db.NhanViens.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (nv is null) return NotFound();
    if (nv.TaiKhoanId is null)
      return BadRequest(new { message = "Nhân viên này chưa có tài khoản đăng nhập. Hãy tạo trước." });

    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Id == nv.TaiKhoanId, ct);
    if (tk is null) return NotFound(new { message = "Không tìm thấy tài khoản liên kết." });

    if (!string.IsNullOrWhiteSpace(req.Email))
    {
      var newEmail = req.Email.Trim().ToLowerInvariant();
      if (newEmail != tk.Email.ToLower())
      {
        var dup = await _db.TaiKhoans.AnyAsync(x => x.Email.ToLower() == newEmail && x.Id != tk.Id, ct);
        if (dup) return Conflict(new { message = "Email đã được sử dụng cho tài khoản khác." });
        tk.Email = newEmail;
      }
    }

    if (!string.IsNullOrWhiteSpace(req.MatKhauMoi))
    {
      tk.MatKhauHash = _hasher.Hash(req.MatKhauMoi);
    }

    if (req.KichHoat.HasValue)
    {
      // Bảo toàn ít nhất 1 Admin: nếu định khoá 1 Admin active → cần Admin khác còn active
      if (!req.KichHoat.Value && tk.VaiTro == VaiTroTaiKhoan.Admin && tk.KichHoat)
      {
        var otherActiveAdmins = await _db.TaiKhoans
          .CountAsync(t => t.Id != tk.Id && t.VaiTro == VaiTroTaiKhoan.Admin && t.KichHoat, ct);
        if (otherActiveAdmins == 0)
          return BadRequest(new
          {
            message = "Không thể khoá Admin duy nhất của hệ thống. Hãy cấp quyền Admin cho tài khoản khác trước."
          });
      }
      tk.KichHoat = req.KichHoat.Value;
    }

    tk.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpDelete("{id:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
  {
    var nv = await _db.NhanViens.Include(x => x.TaiKhoan).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (nv is null) return NotFound();

    // Bảo toàn ít nhất 1 Admin: nếu NV này được link với TaiKhoan role Admin
    // và là Admin active duy nhất → chặn xoá.
    if (nv.TaiKhoan is not null && nv.TaiKhoan.VaiTro == VaiTroTaiKhoan.Admin && nv.TaiKhoan.KichHoat)
    {
      var otherActiveAdmins = await _db.TaiKhoans
        .CountAsync(t => t.Id != nv.TaiKhoan.Id
                         && t.VaiTro == VaiTroTaiKhoan.Admin
                         && t.KichHoat, ct);
      if (otherActiveAdmins == 0)
        return BadRequest(new
        {
          message = "Không thể xoá nhân viên này — tài khoản đính kèm đang là Admin duy nhất của hệ thống. Hãy cấp quyền Admin cho tài khoản khác trước."
        });
    }

    // Xóa cả TaiKhoan đi kèm (nếu có) — tránh tài khoản orphan có role NhanVien.
    if (nv.TaiKhoan is not null)
      _db.TaiKhoans.Remove(nv.TaiKhoan);

    _db.NhanViens.Remove(nv);
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  // Ca làm việc
  [HttpGet("{nhanVienId:guid}/ca-lam-viec")]
  [AllowAnonymous]
  public async Task<ActionResult<List<CaLamViecResponse>>> ListCa(Guid nhanVienId, CancellationToken ct)
  {
    var items = await _db.CaLamViecs.AsNoTracking()
      .Where(x => x.NhanVienId == nhanVienId)
      .OrderBy(x => x.ThuTrongTuan)
      .ThenBy(x => x.GioBatDau)
      .Select(x => new CaLamViecResponse(x.Id, x.ThuTrongTuan, x.GioBatDau, x.GioKetThuc,
        x.Ngay, x.HieuLucTuNgay, x.HieuLucDenNgay, x.LaCaNghi))
      .ToListAsync(ct);
    return Ok(items);
  }

  [HttpPost("{nhanVienId:guid}/ca-lam-viec")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<CaLamViecResponse>> CreateCa(Guid nhanVienId, UpsertCaLamViecRequest req, CancellationToken ct)
  {
    var nv = await _db.NhanViens.FirstOrDefaultAsync(x => x.Id == nhanVienId, ct);
    if (nv is null) return NotFound(new { message = "Nhân viên không tồn tại." });

    if (string.Compare(req.GioBatDau, req.GioKetThuc, StringComparison.Ordinal) >= 0)
      return BadRequest(new { message = "Giờ bắt đầu phải trước giờ kết thúc." });

    int thuTrongTuan = req.ThuTrongTuan;
    if (req.Ngay.HasValue)
    {
      var today = DateOnly.FromDateTime(DateTime.Now);
      if (req.Ngay.Value < today)
        return BadRequest(new { message = "Không thể xếp ca cho ngày đã qua." });
      thuTrongTuan = (int)req.Ngay.Value.ToDateTime(TimeOnly.MinValue).DayOfWeek;
    }
    else if (req.ThuTrongTuan < 0 || req.ThuTrongTuan > 6)
    {
      return BadRequest(new { message = "Thứ trong tuần phải từ 0 (CN) đến 6 (T7)." });
    }

    var existing = req.Ngay.HasValue
      ? await _db.CaLamViecs.AsNoTracking()
          .Where(x => x.NhanVienId == nhanVienId)
          .Where(x => x.Ngay == req.Ngay || (x.Ngay == null && x.ThuTrongTuan == thuTrongTuan))
          .ToListAsync(ct)
      : await _db.CaLamViecs.AsNoTracking()
          .Where(x => x.NhanVienId == nhanVienId)
          .Where(x => x.ThuTrongTuan == thuTrongTuan && x.Ngay == null)
          .ToListAsync(ct);

    var overlap = existing.Any(c =>
      string.Compare(req.GioBatDau, c.GioKetThuc, StringComparison.Ordinal) < 0 &&
      string.Compare(req.GioKetThuc, c.GioBatDau, StringComparison.Ordinal) > 0);
    if (overlap)
      return BadRequest(new { message = $"Nhân viên {nv.HoTen} đã có ca chồng giờ ({string.Join(", ", existing.Select(c => $"{c.GioBatDau}–{c.GioKetThuc}"))})." });

    var ca = new CaLamViec
    {
      Id = Guid.NewGuid(),
      NhanVienId = nhanVienId,
      ThuTrongTuan = thuTrongTuan,
      GioBatDau = req.GioBatDau.Trim(),
      GioKetThuc = req.GioKetThuc.Trim(),
      Ngay = req.Ngay,
      // Recurring: hiệu lực từ hôm nay (không backfill quá khứ)
      HieuLucTuNgay = req.Ngay.HasValue ? null : DateOnly.FromDateTime(DateTime.Now),
    };
    _db.CaLamViecs.Add(ca);
    await _db.SaveChangesAsync(ct);
    return Ok(new CaLamViecResponse(ca.Id, ca.ThuTrongTuan, ca.GioBatDau, ca.GioKetThuc,
      ca.Ngay, ca.HieuLucTuNgay, ca.HieuLucDenNgay, ca.LaCaNghi));
  }

  [HttpDelete("ca-lam-viec/{caId:guid}")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> DeleteCa(Guid caId, CancellationToken ct)
  {
    var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.Id == caId, ct);
    if (ca is null) return NotFound();

    // Specific past dates: không cho xóa (giữ lịch sử)
    if (ca.Ngay.HasValue && ca.Ngay.Value < DateOnly.FromDateTime(DateTime.Now))
      return BadRequest(new { message = "Không thể xóa ca specific của ngày đã qua." });

    if (ca.Ngay == null)
    {
      // Recurring: thay vì DELETE → set HieuLucDenNgay = hôm qua → giữ lịch sử
      var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
      if (ca.HieuLucTuNgay.HasValue && ca.HieuLucTuNgay.Value > yesterday)
      {
        // Recurring chưa từng có hiệu lực → DELETE thật
        _db.CaLamViecs.Remove(ca);
      }
      else
      {
        ca.HieuLucDenNgay = yesterday;
      }
    }
    else
    {
      _db.CaLamViecs.Remove(ca);
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// Admin: bỏ ca recurring CHỈ cho 1 ngày cụ thể (tạo specific với LaCaNghi=true).
  /// Phù hợp khi NV xin nghỉ 1 ngày nhưng vẫn giữ ca recurring cho các tuần khác.
  /// </summary>
  [HttpPost("ca-lam-viec/{caId:guid}/skip-ngay")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> SkipRecurringForDay(Guid caId, SkipRecurringCaRequest req, CancellationToken ct)
  {
    var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.Id == caId, ct);
    if (ca is null) return NotFound();
    if (ca.Ngay != null) return BadRequest(new { message = "Endpoint này chỉ áp dụng cho ca recurring." });

    var today = DateOnly.FromDateTime(DateTime.Now);
    if (req.Ngay < today)
      return BadRequest(new { message = "Không thể bỏ ca cho ngày đã qua." });
    if ((int)req.Ngay.ToDateTime(TimeOnly.MinValue).DayOfWeek != ca.ThuTrongTuan)
      return BadRequest(new { message = "Ngày được chọn không phải ngày recurring của ca này." });

    // Đã có specific Ca (override) cho ngày đó? — chỉ set LaCaNghi
    var existing = await _db.CaLamViecs
      .FirstOrDefaultAsync(x => x.NhanVienId == ca.NhanVienId && x.Ngay == req.Ngay, ct);
    if (existing is not null)
    {
      existing.LaCaNghi = true;
      existing.GioBatDau = ca.GioBatDau;
      existing.GioKetThuc = ca.GioKetThuc;
    }
    else
    {
      _db.CaLamViecs.Add(new CaLamViec
      {
        Id = Guid.NewGuid(),
        NhanVienId = ca.NhanVienId,
        ThuTrongTuan = ca.ThuTrongTuan,
        GioBatDau = ca.GioBatDau,
        GioKetThuc = ca.GioKetThuc,
        Ngay = req.Ngay,
        LaCaNghi = true,
      });
    }
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// Admin: lấy toàn bộ ca làm việc của tất cả nhân viên (recurring + specific) trong khoảng [from, to].
  /// Frontend tự diễn giải recurring sang ngày cụ thể nếu cần.
  /// </summary>
  [HttpGet("admin/ca-lam-viec")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult<List<AdminCaLamViecItem>>> AdminListCa(
    [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
  {
    var q = _db.CaLamViecs.AsNoTracking().AsQueryable();
    if (from.HasValue)
      q = q.Where(x => x.Ngay == null || x.Ngay >= from);
    if (to.HasValue)
      q = q.Where(x => x.Ngay == null || x.Ngay <= to);

    var items = await q
      .OrderBy(x => x.Ngay).ThenBy(x => x.ThuTrongTuan).ThenBy(x => x.GioBatDau)
      .Select(x => new AdminCaLamViecItem(
        x.Id, x.NhanVienId, x.NhanVien!.HoTen, x.NhanVien.ChuyenMon,
        x.ThuTrongTuan, x.GioBatDau, x.GioKetThuc, x.Ngay,
        x.HieuLucTuNgay, x.HieuLucDenNgay, x.LaCaNghi))
      .ToListAsync(ct);

    return Ok(items);
  }

  // ---- NhanVien self-manage ca làm việc của chính mình ----

  [HttpGet("me/ca-lam-viec")]
  [Authorize(Roles = "NhanVien,Admin")]
  public async Task<ActionResult<MyCaLamViecResponse>> GetMyCa(CancellationToken ct)
  {
    var nv = await ResolveCurrentNhanVien(ct);
    if (nv is null) return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào. Vui lòng liên hệ quản trị viên." });

    var items = await _db.CaLamViecs.AsNoTracking()
      .Where(x => x.NhanVienId == nv.Id)
      .OrderBy(x => x.Ngay).ThenBy(x => x.ThuTrongTuan).ThenBy(x => x.GioBatDau)
      .Select(x => new CaLamViecResponse(x.Id, x.ThuTrongTuan, x.GioBatDau, x.GioKetThuc,
        x.Ngay, x.HieuLucTuNgay, x.HieuLucDenNgay, x.LaCaNghi))
      .ToListAsync(ct);

    return Ok(new MyCaLamViecResponse(nv.Id, nv.HoTen, items));
  }

  [HttpPost("me/ca-lam-viec")]
  [Authorize(Roles = "NhanVien,Admin")]
  public async Task<ActionResult<CaLamViecResponse>> CreateMyCa(UpsertCaLamViecRequest req, CancellationToken ct)
  {
    var nv = await ResolveCurrentNhanVien(ct);
    if (nv is null) return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào." });

    if (string.Compare(req.GioBatDau, req.GioKetThuc, StringComparison.Ordinal) >= 0)
      return BadRequest(new { message = "Giờ bắt đầu phải trước giờ kết thúc." });

    var today = DateOnly.FromDateTime(DateTime.Now);
    int thuTrongTuan = req.ThuTrongTuan;

    if (req.Ngay.HasValue)
    {
      if (req.Ngay.Value < today)
        return BadRequest(new { message = "Không thể xếp ca cho ngày đã qua." });
      // Tự suy ra ThuTrongTuan từ Ngay để đảm bảo nhất quán
      thuTrongTuan = (int)req.Ngay.Value.ToDateTime(TimeOnly.MinValue).DayOfWeek;
    }
    else if (req.ThuTrongTuan < 0 || req.ThuTrongTuan > 6)
    {
      return BadRequest(new { message = "Thứ trong tuần phải từ 0 (CN) đến 6 (T7)." });
    }

    // Check overlap: trong cùng ngày (specific Ngay) hoặc cùng ThuTrongTuan (recurring)
    var existing = req.Ngay.HasValue
      ? await _db.CaLamViecs.AsNoTracking()
          .Where(x => x.NhanVienId == nv.Id)
          .Where(x => x.Ngay == req.Ngay || (x.Ngay == null && x.ThuTrongTuan == thuTrongTuan))
          .ToListAsync(ct)
      : await _db.CaLamViecs.AsNoTracking()
          .Where(x => x.NhanVienId == nv.Id)
          .Where(x => x.ThuTrongTuan == thuTrongTuan && x.Ngay == null)
          .ToListAsync(ct);

    var overlap = existing.Any(c =>
      string.Compare(req.GioBatDau, c.GioKetThuc, StringComparison.Ordinal) < 0 &&
      string.Compare(req.GioKetThuc, c.GioBatDau, StringComparison.Ordinal) > 0);
    if (overlap)
      return BadRequest(new { message = $"Ca này chồng với ca khác đã có ({string.Join(", ", existing.Select(c => $"{c.GioBatDau}–{c.GioKetThuc}"))}). Vui lòng chỉnh lại." });

    var ca = new CaLamViec
    {
      Id = Guid.NewGuid(),
      NhanVienId = nv.Id,
      ThuTrongTuan = thuTrongTuan,
      GioBatDau = req.GioBatDau.Trim(),
      GioKetThuc = req.GioKetThuc.Trim(),
      Ngay = req.Ngay,
      HieuLucTuNgay = req.Ngay.HasValue ? null : DateOnly.FromDateTime(DateTime.Now),
    };
    _db.CaLamViecs.Add(ca);
    await _db.SaveChangesAsync(ct);
    return Ok(new CaLamViecResponse(ca.Id, ca.ThuTrongTuan, ca.GioBatDau, ca.GioKetThuc,
      ca.Ngay, ca.HieuLucTuNgay, ca.HieuLucDenNgay, ca.LaCaNghi));
  }

  [HttpDelete("me/ca-lam-viec/{caId:guid}")]
  [Authorize(Roles = "NhanVien,Admin")]
  public async Task<ActionResult> DeleteMyCa(Guid caId, CancellationToken ct)
  {
    var nv = await ResolveCurrentNhanVien(ct);
    if (nv is null) return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào." });

    var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.Id == caId, ct);
    if (ca is null) return NotFound();
    if (ca.NhanVienId != nv.Id) return Forbid();

    // Không cho xóa ca specific quá khứ — giữ lịch sử
    if (ca.Ngay.HasValue && ca.Ngay.Value < DateOnly.FromDateTime(DateTime.Now))
      return BadRequest(new { message = "Không thể xóa ca của ngày đã qua." });

    if (ca.Ngay == null)
    {
      // Recurring: thay vì DELETE → set HieuLucDenNgay = hôm qua → giữ lịch sử
      var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
      if (ca.HieuLucTuNgay.HasValue && ca.HieuLucTuNgay.Value > yesterday)
      {
        _db.CaLamViecs.Remove(ca);
      }
      else
      {
        ca.HieuLucDenNgay = yesterday;
      }
    }
    else
    {
      _db.CaLamViecs.Remove(ca);
    }

    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// NV: bỏ ca recurring CHỈ cho 1 ngày cụ thể (xin nghỉ 1 buổi).
  /// </summary>
  [HttpPost("me/ca-lam-viec/{caId:guid}/skip-ngay")]
  [Authorize(Roles = "NhanVien,Admin")]
  public async Task<ActionResult> SkipMyRecurringForDay(Guid caId, SkipRecurringCaRequest req, CancellationToken ct)
  {
    var nv = await ResolveCurrentNhanVien(ct);
    if (nv is null) return BadRequest(new { message = "Tài khoản chưa được liên kết với hồ sơ nhân viên nào." });

    var ca = await _db.CaLamViecs.FirstOrDefaultAsync(x => x.Id == caId, ct);
    if (ca is null) return NotFound();
    if (ca.NhanVienId != nv.Id) return Forbid();
    if (ca.Ngay != null) return BadRequest(new { message = "Endpoint này chỉ áp dụng cho ca recurring." });

    var today = DateOnly.FromDateTime(DateTime.Now);
    if (req.Ngay < today)
      return BadRequest(new { message = "Không thể bỏ ca cho ngày đã qua." });
    if ((int)req.Ngay.ToDateTime(TimeOnly.MinValue).DayOfWeek != ca.ThuTrongTuan)
      return BadRequest(new { message = "Ngày được chọn không phải ngày recurring của ca này." });

    var existing = await _db.CaLamViecs
      .FirstOrDefaultAsync(x => x.NhanVienId == ca.NhanVienId && x.Ngay == req.Ngay, ct);
    if (existing is not null)
    {
      existing.LaCaNghi = true;
      existing.GioBatDau = ca.GioBatDau;
      existing.GioKetThuc = ca.GioKetThuc;
    }
    else
    {
      _db.CaLamViecs.Add(new CaLamViec
      {
        Id = Guid.NewGuid(),
        NhanVienId = ca.NhanVienId,
        ThuTrongTuan = ca.ThuTrongTuan,
        GioBatDau = ca.GioBatDau,
        GioKetThuc = ca.GioKetThuc,
        Ngay = req.Ngay,
        LaCaNghi = true,
      });
    }
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  private async Task<NhanVien?> ResolveCurrentNhanVien(CancellationToken ct)
  {
    var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(idStr, out var taiKhoanId)) return null;
    return await _db.NhanViens.FirstOrDefaultAsync(x => x.TaiKhoanId == taiKhoanId, ct);
  }
}
