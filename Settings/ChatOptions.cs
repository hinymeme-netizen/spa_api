namespace SpaApi.Settings;

public class ChatOptions
{
  public string GeminiApiKey { get; set; } = "";
  public string GeminiModel { get; set; } = "gemini-2.0-flash";
  // gemini-embedding-001 thay thế cho text-embedding-004 đã bị deprecate (Aug 2025).
  // Mô hình mới hỗ trợ Matryoshka (cho phép cắt dim) nên ta cần truyền outputDimensionality để khớp với Qdrant collection.
  public string EmbeddingModel { get; set; } = "gemini-embedding-001";
  public string QdrantUrl { get; set; } = "http://localhost:6333";
  public string QdrantApiKey { get; set; } = "";
  public string CollectionName { get; set; } = "spa_knowledge";
  public int EmbeddingDim { get; set; } = 768;
  public int RagTopK { get; set; } = 10;
}
