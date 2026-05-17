namespace SpaApi.Settings;

public sealed class SpaOptions
{
  public GioLamViecOptions GioLamViec { get; set; } = new();
}

public sealed class GioLamViecOptions
{
  public string MoCua { get; set; } = "08:00";
  public string DongCua { get; set; } = "20:00";
  public int BuocPhutDatLich { get; set; } = 30;
  public int SoGioToiThieuDeHuy { get; set; } = 4;
}

