using System.ComponentModel.DataAnnotations;

namespace SpaApi.Domain;

/// <summary>
/// Tài liệu tri thức được admin upload để embed lên Qdrant phục vụ chatbot RAG.
/// Mỗi tài liệu được chunk + embed thành nhiều points trong collection `spa_knowledge`,
/// phân biệt qua payload `tailieuId`.
/// </summary>
public sealed class TaiLieu
{
  public Guid Id { get; set; }

  [MaxLength(300)]
  public string TieuDe { get; set; } = default!;

  // Nội dung dạng text/markdown thô
  public string NoiDung { get; set; } = default!;

  [MaxLength(100)]
  public string? Nguon { get; set; }

  // Số chunk thực tế đã embed lên Qdrant
  public int SoChunk { get; set; }

  // ChoXuLy | DangXuLy | HoanThanh | Loi
  [MaxLength(20)]
  public string TrangThai { get; set; } = "ChoXuLy";

  public DateTime NgayTao { get; set; } = DateTime.UtcNow;
  public DateTime? CapNhatLuc { get; set; }
}
