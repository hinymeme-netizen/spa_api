using System.Security.Claims;
using SpaApi.Domain;

namespace SpaApi.Security;

public interface IJwtTokenService
{
  string CreateAccessToken(TaiKhoan taiKhoan);

  /// <summary>
  /// Validate JWT (signature + lifetime + issuer/audience). Trả ClaimsPrincipal nếu hợp lệ, null nếu không.
  /// </summary>
  ClaimsPrincipal? Validate(string token);
}
