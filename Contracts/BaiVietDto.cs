namespace SpaApi.Contracts;

public record BaiVietResponse(
  Guid Id,
  string TieuDe,
  string MoTaNgan,
  string NoiDungHtml,
  string? HinhAnhUrl,
  string? TacGia,
  DateTime NgayTao,
  bool HienThi
);

public record UploadHinhAnhResponse(string Url);

public record UpsertBaiVietRequest(
  string TieuDe,
  string MoTaNgan,
  string NoiDungHtml,
  string? HinhAnhUrl,
  string? TacGia,
  bool HienThi = true
);
