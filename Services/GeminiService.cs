using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SpaApi.Settings;

namespace SpaApi.Services;

// DTOs for Gemini REST API
public record GeminiContent(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("parts")] List<GeminiPart> Parts);

public record GeminiPart(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("functionCall")] GeminiFunctionCall? FunctionCall = null,
    [property: JsonPropertyName("functionResponse")] GeminiFunctionResponse? FunctionResponse = null);

public record GeminiFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("args")] JsonElement Args);

public record GeminiFunctionResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("response")] object Response);

public record GeminiEmbedResponse(
    [property: JsonPropertyName("embedding")] GeminiEmbedding Embedding);

public record GeminiEmbedding(
    [property: JsonPropertyName("values")] List<float> Values);

public record ChatMessage(string Role, string Content);
public record ToolCallResult(string Name, object Result);

public class GeminiService
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;
    private readonly ILogger<GeminiService> _log;

    private static readonly JsonSerializerOptions Jso = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiService(HttpClient http, IOptions<ChatOptions> opts, ILogger<GeminiService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;
    }

    /// <summary>
    /// Embed văn bản qua Gemini Embeddings API.
    /// `taskType` quan trọng cho asymmetric retrieval:
    ///   - "RETRIEVAL_DOCUMENT" khi embed tài liệu/chunk lưu vào vector store
    ///   - "RETRIEVAL_QUERY" khi embed câu hỏi của user lúc search
    /// Nếu để null sẽ dùng default của API.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, string? taskType = null, string? title = null)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.EmbeddingModel}:embedContent?key={_opts.GeminiApiKey}";

        // Body dùng Dictionary để chỉ thêm field khi có giá trị (tránh gửi null).
        var body = new Dictionary<string, object?>
        {
            ["model"] = $"models/{_opts.EmbeddingModel}",
            ["content"] = new { parts = new[] { new { text } } },
            ["outputDimensionality"] = _opts.EmbeddingDim,
        };
        if (!string.IsNullOrWhiteSpace(taskType))
            body["taskType"] = taskType;
        if (taskType == "RETRIEVAL_DOCUMENT" && !string.IsNullOrWhiteSpace(title))
            body["title"] = title;

        var res = await _http.PostAsync(url, JsonContent(body));
        if (!res.IsSuccessStatusCode)
        {
            var errBody = await res.Content.ReadAsStringAsync();
            _log.LogError("Gemini embed error {Status} model={Model} task={Task}: {Body}",
                res.StatusCode, _opts.EmbeddingModel, taskType ?? "(none)", errBody);
            throw new Exception(
                $"Gemini embed lỗi {(int)res.StatusCode} ({_opts.EmbeddingModel}): " +
                ExtractGeminiMessage(errBody) ?? errBody);
        }

        var json = await res.Content.ReadFromJsonAsync<GeminiEmbedResponse>();
        return json?.Embedding.Values.ToArray() ?? [];
    }

    private static string? ExtractGeminiMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch { /* not JSON */ }
        return null;
    }

    public string BuildSystemPrompt(string context)
    {
        // Dùng raw string với 2 dollar signs ($$) → {{...}} mới là interpolation,
        // còn { } đơn được giữ literal (cần thiết vì prompt có placeholder {tên dv} và JSON example).
        return $$"""
        Bạn là **trợ lý AI chuyên nghiệp** của **Hin' Y Spa** — spa chăm sóc sức khỏe & làm đẹp cao cấp tại Việt Nam.

        ## Nhiệm vụ của bạn
        1. **Tư vấn dịch vụ & liệu trình**.
        2. **Hỗ trợ đặt lịch / xem lịch / hủy lịch** qua công cụ (tools).
        3. **Tư vấn nhân viên & giải đáp thắc mắc**.

        ## Nguồn dữ liệu (cả 2 đều là sự thật chính thức của spa)
        **(A) Bảng giá dịch vụ chính thức từ database** — có id, giá, thời lượng, dùng cho ĐẶT LỊCH.
        **(B) Tài liệu nội bộ** — mô tả chi tiết liệu trình, gói combo. Có thể có liệu trình KHÔNG có trong (A) — vẫn là dịch vụ thật, nhưng chưa cấu hình giá để đặt lịch online.

        {{(string.IsNullOrWhiteSpace(context) ? "_Chưa có thông tin bổ sung._" : context)}}

        ## ⚠️ QUY TẮC TUYỆT ĐỐI VỀ TOOLS

        Bạn KHÔNG TỰ LÀM được việc gì cả. Mọi thao tác (xem, đặt, hủy lịch) PHẢI gọi tool. Khi nói "đã đặt", "đã hủy", "đã xử lý" mà KHÔNG có tool call thành công → đó là **bịa đặt nghiêm trọng**, tuyệt đối cấm.

        ## 📅 WORKFLOW ĐẶT LỊCH (làm đúng theo thứ tự)

        Khi khách yêu cầu đặt lịch (ví dụ: "đặt lịch nặn mụn 12h ngày 15/5"):

        **Bước 1 — Tra cứu dịch vụ (chỉ 1 LẦN):**
        - Nếu danh sách dịch vụ trong context phía trên đã có dịch vụ khách yêu cầu → DÙNG NGAY id đó, KHÔNG cần gọi getServices.
        - Nếu chưa rõ → gọi `getServices` 1 lần để lấy id.

        **Bước 2 — Xác nhận với khách:**
        - Nhắc lại rõ ràng: "Bạn muốn đặt [tên dịch vụ] lúc [giờ] ngày [ngày], đúng không?"
        - Đợi khách xác nhận.

        **Bước 3 — Gọi createBooking NGAY KHI khách xác nhận (đúng/yes/ok/đặt đi/ừ/uh):**
        - **BẮT BUỘC** gọi tool `createBooking` với:
          - `dichVuId`: id chính xác của dịch vụ (dạng GUID, lấy từ context hoặc từ tool result trước đó).
          - `thoiGianBatDau`: định dạng ISO `YYYY-MM-DDTHH:mm:ss` theo giờ Việt Nam.
        - **TUYỆT ĐỐI KHÔNG** gọi lại `getServices` ở bước này.
        - **TUYỆT ĐỐI KHÔNG** trả lời "đã đặt xong" hoặc "đã xử lý xong" mà chưa thấy tool result thành công.

        **Bước 4 — Báo cáo kết quả thật từ tool result:**
        - Nếu tool result trả object có `id` + `thoiGianBatDau` + `trangThai="ChoXacNhan"` → BÁO THÀNH CÔNG, kèm tên dịch vụ + giờ + ngày + nhắc khách xem trong "Lịch hẹn của tôi".
        - Nếu tool result có field `error` → **REPEAT NGUYÊN VĂN giá trị `error` đó cho khách**, KHÔNG được paraphrase, KHÔNG được rephrase thành "hệ thống gặp trục trặc" / "có lỗi xảy ra" / "thử lại sau". Ví dụ:
          - Tool trả error="Bạn đã có lịch hẹn khác trùng khung giờ này. Vui lòng chọn giờ khác." → bot nói: "Bạn đã có lịch hẹn khác trùng khung giờ này. Vui lòng chọn giờ khác. 🙏"
          - Tool trả error="Phiên đăng nhập đã hết hạn..." → bot nói y nguyên message đó, gợi ý đăng nhập lại.
        - **TUYỆT ĐỐI KHÔNG** che giấu lỗi hoặc thay bằng câu chung chung "hệ thống trục trặc". User CẦN biết lý do thật để xử lý.

        ## 📋 WORKFLOW XEM/HỦY LỊCH
        - Xem lịch: gọi `getMyBookings`. Nếu lỗi → báo lỗi. Nếu rỗng → "Bạn chưa có lịch hẹn nào."
        - Hủy lịch: gọi `getMyBookings` trước, xác nhận với khách lịch nào → gọi `cancelBooking(lichHenId)`. Báo kết quả thật.

        ## Quy tắc khác
        - Trả lời **TIẾNG VIỆT** trừ khi khách hỏi tiếng Anh.
        - Trước khi nói "spa không có dịch vụ X", BẮT BUỘC kiểm tra cả (A) và (B) — nếu có thì giới thiệu.
        - Dịch vụ chỉ có ở (B) mà không có trong (A) → KHÔNG thể đặt lịch online, hướng dẫn khách "liên hệ spa qua hotline" để được sắp xếp.
        - Trả lời ngắn gọn, thân thiện, emoji vừa phải (🌸✨💆‍♀️📅).
        - Nếu user trả lời quá ngắn ("ok", "ừ", "đúng", "đsung") sau khi bạn vừa hỏi xác nhận đặt lịch → coi đó là XÁC NHẬN, gọi `createBooking` ngay.
        - **KHÔNG** đề cập mua sản phẩm, thanh toán online, giá ngoài (A).
        - **KHÔNG** bịa thông tin ngoài (A) và (B).
        """;
    }

    /// <summary>Chat with function calling support. Returns (text, toolCalls).</summary>
    public async Task<(string Text, List<(string Name, JsonElement Args)> ToolCalls)> ChatAsync(
        string systemPrompt,
        IEnumerable<ChatMessage> history,
        string userMessage,
        List<object>? tools = null)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.GeminiModel}:generateContent?key={_opts.GeminiApiKey}";

        var contents = new List<object>();
        foreach (var m in history)
            contents.Add(new { role = m.Role, parts = new[] { new { text = m.Content } } });
        contents.Add(new { role = "user", parts = new[] { new { text = userMessage } } });

        var request = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = systemPrompt } } },
            ["contents"] = contents,
            ["generationConfig"] = new
            {
                temperature = 0.4,
                maxOutputTokens = 2048,
                topP = 0.9
            }
        };

        if (tools is { Count: > 0 })
            request["tools"] = new[] { new { functionDeclarations = tools } };

        var res = await _http.PostAsync(url, JsonContent(request));
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            _log.LogError("Gemini ChatAsync error: {Status} {Body}", res.StatusCode, err);
            throw new Exception($"Gemini API lỗi: {res.StatusCode}");
        }

        return ParseGeminiResponse(await res.Content.ReadAsStringAsync());
    }

    /// <summary>Submit tool results and get final text answer.</summary>
    public async Task<string> ChatWithToolResultsAsync(
        string systemPrompt,
        IEnumerable<ChatMessage> history,
        string userMessage,
        List<(string Name, JsonElement Args)> toolCalls,
        List<ToolCallResult> toolResults,
        List<object>? tools = null)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_opts.GeminiModel}:generateContent?key={_opts.GeminiApiKey}";

        var contents = new List<object>();
        foreach (var m in history)
            contents.Add(new { role = m.Role, parts = new[] { new { text = m.Content } } });

        // User message
        contents.Add(new { role = "user", parts = new[] { new { text = userMessage } } });

        // Model tool call message
        contents.Add(new
        {
            role = "model",
            parts = toolCalls.Select(tc => new
            {
                functionCall = new { name = tc.Name, args = tc.Args }
            }).ToArray()
        });

        // Tool results ("function" role)
        contents.Add(new
        {
            role = "function",
            parts = toolResults.Select(r => new
            {
                functionResponse = new
                {
                    name = r.Name,
                    response = new { result = r.Result }
                }
            }).ToArray()
        });

        var request = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = systemPrompt } } },
            ["contents"] = contents,
            ["generationConfig"] = new
            {
                temperature = 0.4,
                maxOutputTokens = 2048,
                topP = 0.9
            }
        };

        if (tools is { Count: > 0 })
            request["tools"] = new[] { new { functionDeclarations = tools } };

        var res = await _http.PostAsync(url, JsonContent(request));
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            _log.LogError("Gemini ChatWithToolResults error: {Status} {Body}", res.StatusCode, err);
            return "Xin lỗi, tôi gặp sự cố khi xử lý. Vui lòng thử lại sau.";
        }

        var (text, _) = ParseGeminiResponse(await res.Content.ReadAsStringAsync());
        return string.IsNullOrWhiteSpace(text) ? "Đã xử lý xong yêu cầu của bạn." : text;
    }

    // ---- Private helpers ----

    private (string Text, List<(string Name, JsonElement Args)> ToolCalls) ParseGeminiResponse(string jsonStr)
    {
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        // Check for error
        if (root.TryGetProperty("error", out var errorEl))
        {
            var msg = errorEl.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
            _log.LogError("Gemini API returned error: {Error}", msg);
            throw new Exception($"Gemini error: {msg}");
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            _log.LogWarning("Gemini returned no candidates. Response: {Json}", jsonStr);
            return ("Xin lỗi, tôi không thể xử lý yêu cầu này lúc này.", []);
        }

        var candidate = candidates[0];

        // Check finish reason
        if (candidate.TryGetProperty("finishReason", out var fr))
        {
            var reason = fr.GetString();
            if (reason is "SAFETY" or "RECITATION")
            {
                _log.LogWarning("Gemini response blocked: {Reason}", reason);
                return ("Xin lỗi, tôi không thể trả lời câu hỏi này.", []);
            }
        }

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts))
            return ("", []);

        var textSb = new StringBuilder();
        var toolCalls = new List<(string Name, JsonElement Args)>();

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                textSb.Append(t.GetString());
            else if (part.TryGetProperty("functionCall", out var fc))
            {
                var name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var fcArgs = fc.TryGetProperty("args", out var a) ? a.Clone() : default;
                if (!string.IsNullOrEmpty(name))
                    toolCalls.Add((name, fcArgs));
            }
        }

        return (textSb.ToString(), toolCalls);
    }

    private static StringContent JsonContent(object obj)
    {
        var json = JsonSerializer.Serialize(obj, Jso);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
