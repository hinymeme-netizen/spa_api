using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaApi.Contracts;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Security;
using SpaApi.Services;

namespace SpaApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
  private readonly SpaDbContext _db;
  private readonly IPasswordHasher _hasher;
  private readonly IJwtTokenService _jwt;
  private readonly IConfiguration _config;
  private readonly IEmailSender _email;
  private readonly ILogger<AuthController> _logger;

  public AuthController(SpaDbContext db, IPasswordHasher hasher, IJwtTokenService jwt,
    IConfiguration config, IEmailSender email, ILogger<AuthController> logger)
  {
    _db = db;
    _hasher = hasher;
    _jwt = jwt;
    _config = config;
    _email = email;
    _logger = logger;
  }

  [HttpPost("register")]
  [AllowAnonymous]
  public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req, CancellationToken ct)
  {
    var email = req.Email.Trim().ToLowerInvariant();
    var exists = await _db.TaiKhoans.AnyAsync(x => x.Email.ToLower() == email, ct);
    if (exists) return Conflict(new { message = "Email đã tồn tại." });

    var tk = new TaiKhoan
    {
      Id = Guid.NewGuid(),
      Email = email,
      HoTen = req.HoTen.Trim(),
      SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim(),
      MatKhauHash = _hasher.Hash(req.MatKhau),
      VaiTro = VaiTroTaiKhoan.User,
      KichHoat = true
    };

    _db.TaiKhoans.Add(tk);
    await _db.SaveChangesAsync(ct);

    var token = _jwt.CreateAccessToken(tk);
    return Ok(new AuthResponse(tk.Id, tk.Email, tk.HoTen, tk.SoDienThoai, tk.VaiTro.ToString(), token));
  }

  [HttpPost("google")]
  [AllowAnonymous]
  public async Task<ActionResult<AuthResponse>> GoogleSignIn(GoogleSignInRequest req, CancellationToken ct)
  {
    var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
                   ?? _config["Google:ClientId"];
    if (string.IsNullOrWhiteSpace(clientId))
      return StatusCode(500, new { message = "Server chưa cấu hình GOOGLE_CLIENT_ID." });

    GoogleJsonWebSignature.Payload payload;
    try
    {
      payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken, new GoogleJsonWebSignature.ValidationSettings
      {
        Audience = new[] { clientId }
      });
    }
    catch (InvalidJwtException ex)
    {
      return Unauthorized(new { message = $"Token Google không hợp lệ: {ex.Message}" });
    }

    if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
      return Unauthorized(new { message = "Email Google chưa xác minh." });

    var googleSub = payload.Subject;
    var email = payload.Email.Trim().ToLowerInvariant();

    // 1) Tìm theo GoogleSub trước (đã link Google)
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.GoogleSub == googleSub, ct);
    if (tk is null)
    {
      // 2) Theo email — link Google vào tài khoản local đã có
      tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);
      if (tk is not null)
      {
        tk.GoogleSub = googleSub;
        tk.CapNhatLuc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
      }
      else
      {
        // 3) Tạo tài khoản mới
        tk = new TaiKhoan
        {
          Id = Guid.NewGuid(),
          Email = email,
          HoTen = string.IsNullOrWhiteSpace(payload.Name) ? email.Split('@')[0] : payload.Name,
          // Tạo password random — user vẫn có thể "Quên mật khẩu" để set sau
          MatKhauHash = _hasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
          GoogleSub = googleSub,
          VaiTro = VaiTroTaiKhoan.User,
          KichHoat = true,
          NgayTao = DateTime.UtcNow,
        };
        _db.TaiKhoans.Add(tk);
        await _db.SaveChangesAsync(ct);
      }
    }

    if (!tk.KichHoat) return Unauthorized(new { message = "Tài khoản đã bị khóa." });

    var token = _jwt.CreateAccessToken(tk);
    return Ok(new AuthResponse(tk.Id, tk.Email, tk.HoTen, tk.SoDienThoai, tk.VaiTro.ToString(), token));
  }

  [HttpPost("login")]
  [AllowAnonymous]
  public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
  {
    var email = req.Email.Trim().ToLowerInvariant();
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);
    if (tk is null) return Unauthorized(new { message = "Sai email hoặc mật khẩu." });
    if (!tk.KichHoat) return Unauthorized(new { message = "Tài khoản đã bị khóa." });
    if (!_hasher.Verify(req.MatKhau, tk.MatKhauHash)) return Unauthorized(new { message = "Sai email hoặc mật khẩu." });

    var token = _jwt.CreateAccessToken(tk);
    return Ok(new AuthResponse(tk.Id, tk.Email, tk.HoTen, tk.SoDienThoai, tk.VaiTro.ToString(), token));
  }

  [HttpGet("me")]
  [Authorize]
  public async Task<ActionResult<object>> Me(CancellationToken ct)
  {
    var userId = GetUserId();
    var tk = await _db.TaiKhoans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
    if (tk is null) return NotFound();
    return Ok(new
    {
      tk.Id,
      tk.Email,
      tk.HoTen,
      tk.SoDienThoai,
      VaiTro = tk.VaiTro.ToString(),
      tk.KichHoat,
      tk.NgayTao
    });
  }

  [HttpPut("me")]
  [Authorize]
  public async Task<ActionResult> UpdateProfile(UpdateProfileRequest req, CancellationToken ct)
  {
    var userId = GetUserId();
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Id == userId, ct);
    if (tk is null) return NotFound();

    tk.HoTen = req.HoTen.Trim();
    tk.SoDienThoai = string.IsNullOrWhiteSpace(req.SoDienThoai) ? null : req.SoDienThoai.Trim();
    tk.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  [HttpPost("change-password")]
  [Authorize]
  public async Task<ActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
  {
    if (string.Equals(req.MatKhauCu, req.MatKhauMoi, StringComparison.Ordinal))
      return BadRequest(new { message = "Mật khẩu mới phải khác mật khẩu cũ." });

    var userId = GetUserId();
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Id == userId, ct);
    if (tk is null) return NotFound(new { message = "Tài khoản không tồn tại." });
    if (!_hasher.Verify(req.MatKhauCu, tk.MatKhauHash)) return BadRequest(new { message = "Mật khẩu cũ không đúng." });

    tk.MatKhauHash = _hasher.Hash(req.MatKhauMoi);
    tk.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return NoContent();
  }

  /// <summary>
  /// Bước 1 của flow quên MK: nhận email → tạo token → gửi mail link reset.
  /// Luôn trả 200 OK dù email tồn tại hay không (tránh enumeration attack).
  /// </summary>
  [HttpPost("forgot-password")]
  [AllowAnonymous]
  public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
  {
    var email = req.Email.Trim().ToLowerInvariant();
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);

    if (tk is not null && tk.KichHoat)
    {
      // Tạo token random 32 byte → URL-safe base64
      var rawBytes = RandomNumberGenerator.GetBytes(32);
      var rawToken = Convert.ToBase64String(rawBytes)
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

      tk.PasswordResetTokenHash = HashToken(rawToken);
      tk.PasswordResetExpires = DateTime.UtcNow.AddHours(1); // hết hạn sau 1 giờ
      tk.CapNhatLuc = DateTime.UtcNow;
      await _db.SaveChangesAsync(ct);

      var feUrl = (_config["Frontend:BaseUrl"]
        ?? Environment.GetEnvironmentVariable("FRONTEND_BASE_URL")
        ?? "http://localhost:3000").TrimEnd('/');
      var resetLink = $"{feUrl}/reset-mat-khau?token={Uri.EscapeDataString(rawToken)}";

      var html = $@"
<div style=""font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#222;"">
  <h2 style=""color:#C8569A;margin:0 0 16px;"">Hin' Y Spa — Đặt lại mật khẩu</h2>
  <p>Xin chào <b>{System.Net.WebUtility.HtmlEncode(tk.HoTen)}</b>,</p>
  <p>Chúng tôi vừa nhận yêu cầu đặt lại mật khẩu cho tài khoản <b>{tk.Email}</b>.</p>
  <p>Nhấn vào nút dưới đây để chọn mật khẩu mới (link có hiệu lực <b>1 giờ</b>):</p>
  <p style=""text-align:center;margin:24px 0;"">
    <a href=""{resetLink}"" style=""display:inline-block;background:#C8569A;color:#fff;padding:12px 28px;border-radius:999px;text-decoration:none;font-weight:600;"">Đặt lại mật khẩu</a>
  </p>
  <p style=""font-size:13px;color:#666;"">Hoặc copy link sau vào trình duyệt:<br/><span style=""word-break:break-all;"">{resetLink}</span></p>
  <hr style=""border:none;border-top:1px solid #eee;margin:24px 0;""/>
  <p style=""font-size:12px;color:#999;"">Nếu không phải bạn yêu cầu, hãy bỏ qua email này. Mật khẩu của bạn không thay đổi cho đến khi nhấn link.</p>
</div>";

      // Luôn log link ra console — backup cho trường hợp gửi email fail (vd Railway block port SMTP).
      // Admin có thể đọc link từ Railway logs để gửi tay cho user.
      _logger.LogInformation(
        "[ForgotPassword] Reset link generated for {Email}: {Link} (expires in 1h)",
        tk.Email, resetLink);

      // Fire-and-forget: KHÔNG await → endpoint trả 200 ngay, email gửi background.
      // Tránh trường hợp SMTP timeout làm FE treo.
      var emailSender = _email;
      var logger = _logger;
      var emailTo = tk.Email;
      _ = Task.Run(async () =>
      {
        try
        {
          await emailSender.SendAsync(emailTo, "Đặt lại mật khẩu Hin' Y Spa", html, CancellationToken.None);
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "[ForgotPassword] Background email send failed for {Email}", emailTo);
        }
      });
    }
    else
    {
      // Nếu email không tồn tại / bị khoá: vẫn delay nhỏ tránh timing attack
      await Task.Delay(200, ct);
    }

    return Ok(new { message = "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi link đặt lại mật khẩu." });
  }

  /// <summary>DEBUG: kiểm tra cấu hình email provider hiện tại (env vars nào được set).</summary>
  [HttpGet("email-config")]
  [Authorize(Roles = "Admin")]
  public ActionResult EmailConfig()
  {
    var resendKey = Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? _config["Resend:ApiKey"];
    var resendFrom = Environment.GetEnvironmentVariable("RESEND_FROM") ?? _config["Resend:From"];
    var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? _config["Smtp:Host"];
    var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER") ?? _config["Smtp:User"];
    var smtpFrom = Environment.GetEnvironmentVariable("SMTP_FROM") ?? _config["Smtp:From"];

    var provider = !string.IsNullOrWhiteSpace(resendKey) ? "Resend (HTTP)"
      : !string.IsNullOrWhiteSpace(smtpHost) ? "SMTP"
      : "Console-only (logs)";

    var effectiveFrom = !string.IsNullOrWhiteSpace(resendKey)
      ? (string.IsNullOrWhiteSpace(resendFrom) || resendFrom.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
          ? "onboarding@resend.dev (default — Gmail không verify được)"
          : resendFrom)
      : smtpFrom ?? smtpUser;

    return Ok(new
    {
      provider,
      resendApiKeySet = !string.IsNullOrWhiteSpace(resendKey),
      resendFromSet = !string.IsNullOrWhiteSpace(resendFrom),
      smtpHostSet = !string.IsNullOrWhiteSpace(smtpHost),
      smtpUserSet = !string.IsNullOrWhiteSpace(smtpUser),
      effectiveFrom,
      hint = !string.IsNullOrWhiteSpace(resendKey)
        ? "Đang dùng Resend HTTP API. Free tier chỉ gửi được tới email đăng ký Resend account (trừ khi đã verify domain riêng)."
        : "Đang dùng SMTP. Trên Railway port 587 bị block — set RESEND_API_KEY để fix.",
    });
  }

  /// <summary>
  /// DEBUG: Admin test SMTP — gửi email thật ngay, return success/error message thật.
  /// Khác với /forgot-password (luôn 200 OK), endpoint này trả luôn lỗi SMTP để debug.
  /// </summary>
  [HttpPost("test-email")]
  [Authorize(Roles = "Admin")]
  public async Task<ActionResult> TestEmail([FromQuery] string to, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(to))
      return BadRequest(new { message = "Thiếu query param 'to'." });

    var html = $@"
<div style=""font-family:Arial,sans-serif;padding:24px;"">
  <h2>Test email từ Hin' Y Spa</h2>
  <p>Đây là email test SMTP. Gửi lúc <b>{DateTime.UtcNow:O}</b>.</p>
  <p>Nếu bạn thấy email này → cấu hình SMTP đã đúng ✅</p>
</div>";

    var ok = await _email.SendAsync(to, "[Test] SMTP Hin' Y Spa", html, ct);
    if (ok)
      return Ok(new { message = $"Đã gửi email test tới {to}. Kiểm tra inbox + Spam.", success = true });
    return StatusCode(500, new
    {
      message = "Không gửi được email — kiểm tra logs Railway để xem lỗi cụ thể.",
      success = false,
    });
  }

  /// <summary>Bước 2: nhận token + mật khẩu mới → verify token → cập nhật password.</summary>
  [HttpPost("reset-password")]
  [AllowAnonymous]
  public async Task<ActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
  {
    var hash = HashToken(req.Token);
    var tk = await _db.TaiKhoans.FirstOrDefaultAsync(x => x.PasswordResetTokenHash == hash, ct);

    if (tk is null || tk.PasswordResetExpires is null || tk.PasswordResetExpires < DateTime.UtcNow)
      return BadRequest(new { message = "Link đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });

    if (!tk.KichHoat)
      return BadRequest(new { message = "Tài khoản đã bị khoá." });

    tk.MatKhauHash = _hasher.Hash(req.MatKhauMoi);
    tk.PasswordResetTokenHash = null;
    tk.PasswordResetExpires = null;
    tk.CapNhatLuc = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);

    return Ok(new { message = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập với mật khẩu mới." });
  }

  private static string HashToken(string raw)
  {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes);
  }

  private Guid GetUserId()
  {
    var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.Parse(raw!);
  }
}

