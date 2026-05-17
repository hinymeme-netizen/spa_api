using System.ComponentModel.DataAnnotations;

namespace SpaApi.Contracts;

public sealed record RegisterRequest(
  [Required, EmailAddress, MaxLength(256)] string Email,
  [Required, MinLength(6), MaxLength(200)] string MatKhau,
  [Required, MaxLength(128)] string HoTen,
  [MaxLength(32)] string? SoDienThoai);

public sealed record LoginRequest(
  [Required, EmailAddress, MaxLength(256)] string Email,
  [Required] string MatKhau);

public sealed record GoogleSignInRequest(
  [Required] string IdToken);

public sealed record ForgotPasswordRequest(
  [Required, EmailAddress, MaxLength(256)] string Email);

public sealed record ResetPasswordRequest(
  [Required, MaxLength(256)] string Token,
  [Required, MinLength(6), MaxLength(200)] string MatKhauMoi);

public sealed record AuthResponse(
  Guid Id,
  string Email,
  string HoTen,
  string? SoDienThoai,
  string VaiTro,
  string AccessToken);

public sealed record UpdateProfileRequest(
  [Required, MaxLength(128)] string HoTen,
  [MaxLength(32)] string? SoDienThoai);

public sealed record ChangePasswordRequest(
  [Required] string MatKhauCu,
  [Required, MinLength(6), MaxLength(200)] string MatKhauMoi);

