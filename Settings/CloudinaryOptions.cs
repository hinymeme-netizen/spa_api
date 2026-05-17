namespace SpaApi.Settings;

public sealed class CloudinaryOptions
{
  public string CloudName { get; set; } = "";
  public string ApiKey { get; set; } = "";
  public string ApiSecret { get; set; } = "";
  /// <summary>Folder gốc trên Cloudinary (vd: "upstore-spa").</summary>
  public string RootFolder { get; set; } = "upstore-spa";
}
