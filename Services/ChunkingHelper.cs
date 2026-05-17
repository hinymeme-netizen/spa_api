using System.Text;
using System.Text.RegularExpressions;

namespace SpaApi.Services;

/// <summary>
/// Tách văn bản dài thành các chunk phù hợp để embed.
/// Chiến lược: ưu tiên cắt theo paragraph (\n\n).
/// Paragraph quá dài thì cắt tiếp theo câu (. ! ?), ghép câu cho đến khi sát maxChars.
/// </summary>
public static class ChunkingHelper
{
  // Tách câu theo dấu kết câu Việt/Anh, có hỗ trợ "..." và xuống dòng đơn.
  private static readonly Regex SentenceSplitter =
    new(@"(?<=[\.\!\?…])\s+|\n+", RegexOptions.Compiled);

  public static List<string> ChunkText(string text, int maxChars = 500, int minChunkChars = 30)
  {
    if (string.IsNullOrWhiteSpace(text)) return [];

    var normalized = text.Replace("\r\n", "\n").Trim();
    var paragraphs = normalized
      .Split(new[] { "\n\n", "\n \n" }, StringSplitOptions.RemoveEmptyEntries)
      .Select(p => p.Trim())
      .Where(p => p.Length > 0)
      .ToList();

    var chunks = new List<string>();
    foreach (var para in paragraphs)
    {
      if (para.Length <= maxChars)
      {
        chunks.Add(para);
        continue;
      }

      // Paragraph quá dài → tách câu rồi ghép
      var sentences = SentenceSplitter.Split(para)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToList();

      var current = new StringBuilder();
      foreach (var sentence in sentences)
      {
        if (current.Length + sentence.Length + 1 > maxChars && current.Length > 0)
        {
          chunks.Add(current.ToString().Trim());
          current.Clear();
        }
        if (current.Length > 0) current.Append(' ');
        current.Append(sentence);

        // Câu đơn cũng có thể vượt maxChars (vd. liệt kê dài) → flush ngay
        if (current.Length >= maxChars)
        {
          chunks.Add(current.ToString().Trim());
          current.Clear();
        }
      }
      if (current.Length > 0) chunks.Add(current.ToString().Trim());
    }

    return chunks
      .Where(c => c.Length >= minChunkChars)
      .ToList();
  }
}
