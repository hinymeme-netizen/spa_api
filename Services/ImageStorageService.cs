using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SpaApi.Settings;

namespace SpaApi.Services;

public interface IImageStorageService
{
  /// <summary>
  /// Upload 1 ảnh và trả về public URL (HTTPS, secure).
  /// </summary>
  Task<string> UploadAsync(IFormFile file, string subFolder, CancellationToken ct = default);
}

public sealed class CloudinaryImageStorageService : IImageStorageService
{
  private readonly Cloudinary _cloudinary;
  private readonly CloudinaryOptions _opts;
  private readonly ILogger<CloudinaryImageStorageService> _log;

  public CloudinaryImageStorageService(
    IOptions<CloudinaryOptions> opts,
    ILogger<CloudinaryImageStorageService> log)
  {
    _opts = opts.Value;
    _log = log;

    if (string.IsNullOrWhiteSpace(_opts.CloudName))
      throw new InvalidOperationException(
        "Cloudinary chưa được cấu hình. Set CLOUDINARY_CLOUD_NAME / CLOUDINARY_API_KEY / CLOUDINARY_API_SECRET trong env.");

    _cloudinary = new Cloudinary(new Account(_opts.CloudName, _opts.ApiKey, _opts.ApiSecret))
    {
      Api = { Secure = true }
    };
  }

  public async Task<string> UploadAsync(IFormFile file, string subFolder, CancellationToken ct = default)
  {
    if (file is null || file.Length == 0)
      throw new ArgumentException("Không có file để upload.");

    await using var stream = file.OpenReadStream();
    var folder = string.IsNullOrWhiteSpace(_opts.RootFolder)
      ? subFolder
      : $"{_opts.RootFolder}/{subFolder}";

    var uploadParams = new ImageUploadParams
    {
      File = new FileDescription(file.FileName, stream),
      Folder = folder,
      UseFilename = false,
      UniqueFilename = true,
      Overwrite = false,
      // Tự crop về kích thước hợp lý nếu quá lớn để tiết kiệm bandwidth + storage
      Transformation = new Transformation()
        .Width(1600).Height(1600).Crop("limit")
        .Quality("auto").FetchFormat("auto"),
    };

    var result = await _cloudinary.UploadAsync(uploadParams, ct);

    if (result.Error != null)
    {
      _log.LogError("Cloudinary upload failed: {Error}", result.Error.Message);
      throw new Exception($"Cloudinary lỗi: {result.Error.Message}");
    }

    var url = result.SecureUrl?.ToString() ?? result.Url?.ToString();
    if (string.IsNullOrWhiteSpace(url))
      throw new Exception("Cloudinary không trả về URL hợp lệ.");

    _log.LogInformation("Uploaded image {Bytes} bytes → {Url}", file.Length, url);
    return url;
  }
}
