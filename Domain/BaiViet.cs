namespace SpaApi.Domain;

public sealed class BaiViet
{
  public Guid Id { get; set; }
  public string TieuDe { get; set; } = string.Empty;
  public string MoTaNgan { get; set; } = string.Empty;
  public string NoiDungHtml { get; set; } = string.Empty;
  public string? HinhAnhUrl { get; set; }
  public string? TacGia { get; set; }
  public DateTime NgayTao { get; set; }
  public bool HienThi { get; set; }
}
