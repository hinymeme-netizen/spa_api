using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpaApi.Domain;
using SpaApi.Settings;

namespace SpaApi.Security;

public sealed class JwtTokenService : IJwtTokenService
{
  private readonly JwtOptions _opt;
  private readonly TokenValidationParameters _validationParams;

  public JwtTokenService(IOptions<JwtOptions> opt)
  {
    _opt = opt.Value;
    _validationParams = new TokenValidationParameters
    {
      ValidateIssuer = true,
      ValidateAudience = true,
      ValidateLifetime = true,
      ValidateIssuerSigningKey = true,
      ValidIssuer = _opt.Issuer,
      ValidAudience = _opt.Audience,
      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)),
      ClockSkew = TimeSpan.FromSeconds(30),
      NameClaimType = ClaimTypes.NameIdentifier,
      RoleClaimType = ClaimTypes.Role
    };
  }

  public string CreateAccessToken(TaiKhoan taiKhoan)
  {
    var claims = new List<Claim>
    {
      new(ClaimTypes.NameIdentifier, taiKhoan.Id.ToString()),
      new(ClaimTypes.Email, taiKhoan.Email),
      new(ClaimTypes.Name, taiKhoan.HoTen),
      new(ClaimTypes.Role, taiKhoan.VaiTro.ToString())
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
      issuer: _opt.Issuer,
      audience: _opt.Audience,
      claims: claims,
      notBefore: DateTime.UtcNow,
      expires: DateTime.UtcNow.AddMinutes(_opt.ExpiresMinutes),
      signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
  }

  public ClaimsPrincipal? Validate(string token)
  {
    if (string.IsNullOrWhiteSpace(token)) return null;
    try
    {
      var handler = new JwtSecurityTokenHandler();
      var principal = handler.ValidateToken(token, _validationParams, out _);
      return principal;
    }
    catch
    {
      return null;
    }
  }
}
