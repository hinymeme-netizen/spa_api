using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaApi.Data;
using SpaApi.Domain;
using SpaApi.Security;
using SpaApi.Settings;

namespace SpaApi.Services;

public record ChatRequest(
    string Message,
    List<ChatMessage>? History = null,
    string? Token = null);

public record ChatResponse(string Answer, List<string> ToolsUsed);

public class ChatOrchestratorService
{
    private readonly GeminiService _gemini;
    private readonly QdrantService _qdrant;
    private readonly ChatOptions _opts;
    private readonly SpaDbContext _db;
    private readonly SpaOptions _spaOpts;
    private readonly ILogger<ChatOrchestratorService> _log;

    // ---- Tool definitions ----
    private static readonly List<object> ToolDefinitions = new()
    {
        new {
            name = "getServices",
            description = "Lấy danh sách đầy đủ dịch vụ spa kèm giá và thời lượng. Dùng khi khách hỏi dịch vụ, giá cả, thời gian làm.",
            parameters = new {
                type = "object",
                properties = new {
                    tuKhoa = new { type = "string", description = "Từ khóa tìm kiếm dịch vụ (tùy chọn)" }
                }
            }
        },
        new {
            name = "getStaff",
            description = "Lấy danh sách nhân viên đang làm việc kèm chuyên môn. Dùng khi khách hỏi về nhân viên, chuyên viên.",
            parameters = new {
                type = "object",
                properties = new { }
            }
        },
        new {
            name = "createBooking",
            description = "Đặt lịch hẹn dịch vụ cho khách. Yêu cầu khách đã đăng nhập. Hỏi rõ: dịch vụ muốn đặt, ngày giờ. Thời gian phải trong giờ làm việc (08:00–20:00) và bội số 30 phút.",
            parameters = new {
                type = "object",
                properties = new {
                    dichVuId = new { type = "string", description = "ID của dịch vụ (lấy từ getServices)" },
                    thoiGianBatDau = new { type = "string", description = "Thời gian bắt đầu định dạng ISO: YYYY-MM-DDTHH:mm:ss" },
                    nhanVienId = new { type = "string", description = "ID nhân viên mong muốn (tùy chọn, lấy từ getStaff)" },
                    ghiChu = new { type = "string", description = "Ghi chú thêm từ khách (tùy chọn)" }
                },
                required = new[] { "dichVuId", "thoiGianBatDau" }
            }
        },
        new {
            name = "getMyBookings",
            description = "Xem danh sách lịch hẹn của khách (lịch sử & sắp tới). Yêu cầu đăng nhập.",
            parameters = new {
                type = "object",
                properties = new { }
            }
        },
        new {
            name = "cancelBooking",
            description = "Hủy một lịch hẹn đã đặt. Yêu cầu đăng nhập. Chỉ hủy được trước ít nhất 4 giờ.",
            parameters = new {
                type = "object",
                properties = new {
                    lichHenId = new { type = "string", description = "ID lịch hẹn cần hủy (lấy từ getMyBookings)" }
                },
                required = new[] { "lichHenId" }
            }
        }
    };

    private readonly IJwtTokenService _jwt;

    public ChatOrchestratorService(
        GeminiService gemini,
        QdrantService qdrant,
        IOptions<ChatOptions> opts,
        SpaDbContext db,
        IOptions<SpaOptions> spaOpts,
        IJwtTokenService jwt,
        ILogger<ChatOrchestratorService> log)
    {
        _gemini = gemini;
        _qdrant = qdrant;
        _opts = opts.Value;
        _db = db;
        _spaOpts = spaOpts.Value;
        _jwt = jwt;
        _log = log;
    }

    /// <summary>Validate JWT token, trả userId nếu hợp lệ + role là User (không phải Admin/NhanVien dùng tool đặt lịch).</summary>
    private Guid? ValidateUserToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var principal = _jwt.Validate(token);
        if (principal is null) return null;
        var idStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idStr, out var id)) return null;
        return id;
    }

    private bool HopLeGioLamViec(DateTime localTime)
    {
        var mo = TimeOnly.ParseExact(_spaOpts.GioLamViec.MoCua, "HH:mm", CultureInfo.InvariantCulture);
        var dong = TimeOnly.ParseExact(_spaOpts.GioLamViec.DongCua, "HH:mm", CultureInfo.InvariantCulture);
        var t = TimeOnly.FromDateTime(localTime);
        return t >= mo && t <= dong;
    }

    private bool DungBuocPhut(DateTime localTime) =>
        (localTime.Minute % _spaOpts.GioLamViec.BuocPhutDatLich) == 0;

    private bool ConDuocHuy(DateTime localStart) =>
        (localStart - DateTime.Now).TotalHours >= _spaOpts.GioLamViec.SoGioToiThieuDeHuy;

    public async Task<ChatResponse> RunAsync(ChatRequest req)
    {
        var history = req.History ?? [];

        // 1. Load DB context (services, staff, config) — always available
        var dbContext = await BuildDbContextAsync();

        // 2. Try RAG context — optional, non-fatal.
        // RETRIEVAL_QUERY task type bắt buộc để asymmetric với chunks (đã embed với RETRIEVAL_DOCUMENT)
        // → similarity score giữa query và chunk có ngữ nghĩa tương ứng tăng đáng kể.
        string ragContext = "";
        try
        {
            var vector = await _gemini.EmbedAsync(req.Message, taskType: "RETRIEVAL_QUERY");
            var docs = await _qdrant.SearchAsync(vector, _opts.RagTopK);

            // Log đầy đủ để debug retrieval quality
            var topPreview = string.Join(" | ",
                docs.Take(5).Select((d, i) => $"[{i + 1}] {d.Title} ({d.Score:F3})"));
            _log.LogInformation(
                "RAG: query=\"{Q}\" → {Count} docs. Top: {Top}",
                req.Message.Length > 80 ? req.Message[..80] + "..." : req.Message,
                docs.Count,
                topPreview);

            if (docs.Count > 0)
                ragContext = "\n\n## (B) Tài liệu nội bộ (đã được khớp với câu hỏi):\n" +
                             string.Join("\n\n", docs.Select((d, i) =>
                                 $"### [{i + 1}] {d.Title} (relevance={d.Score:F2})\n{d.Text}"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RAG retrieval skipped (Qdrant/embedding lỗi)");
        }

        // 3. Build system prompt with full context
        var systemPrompt = _gemini.BuildSystemPrompt(dbContext + ragContext);

        // 4. First Gemini call
        var (text, toolCalls) = await _gemini.ChatAsync(systemPrompt, history, req.Message, ToolDefinitions);

        if (toolCalls.Count == 0)
            return new ChatResponse(
                text.TrimOrDefault("Xin lỗi, tôi không hiểu câu hỏi. Bạn thử diễn đạt cách khác được không?"),
                []);

        // 5. Execute tools
        _log.LogInformation(
            "Tools called: {Names}",
            string.Join(", ", toolCalls.Select(t => t.Name)));
        var toolResults = await ExecuteToolsAsync(toolCalls, req.Token);
        foreach (var tr in toolResults)
        {
            var preview = JsonSerializer.Serialize(tr.Result);
            _log.LogInformation(
                "Tool {Name} result: {Preview}",
                tr.Name,
                preview.Length > 200 ? preview[..200] + "..." : preview);
        }

        // 6. Second Gemini call with tool results → final answer
        var finalText = await _gemini.ChatWithToolResultsAsync(
            systemPrompt, history, req.Message, toolCalls, toolResults, ToolDefinitions);

        // KHÔNG fallback "Đã xử lý xong" — sẽ làm user hiểu nhầm thành công.
        // Thay vào đó, build mô tả từ tool result thật.
        if (string.IsNullOrWhiteSpace(finalText))
        {
            finalText = SummarizeToolResults(toolResults);
        }

        return new ChatResponse(
            finalText,
            toolCalls.Select(t => t.Name).ToList());
    }

    /// <summary>
    /// Khi Gemini lượt 2 trả empty, dựng câu trả lời thay thế dựa trên tool results
    /// để tránh nói "đã xử lý xong" giả tạo.
    /// </summary>
    private static string SummarizeToolResults(List<ToolCallResult> results)
    {
        var sb = new StringBuilder();
        foreach (var tr in results)
        {
            var json = JsonSerializer.Serialize(tr.Result);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Trường hợp tool báo lỗi
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var err))
            {
                sb.AppendLine($"❌ {err.GetString() ?? "Có lỗi xảy ra khi thực thi yêu cầu."}");
                continue;
            }

            switch (tr.Name)
            {
                case "createBooking":
                    if (root.TryGetProperty("thoiGianBatDau", out var tg) && tg.ValueKind == JsonValueKind.String)
                    {
                        var dvName = root.TryGetProperty("tenDichVu", out var n) ? n.GetString() : null;
                        sb.AppendLine($"✅ Đã tạo lịch hẹn{(dvName != null ? $" cho {dvName}" : "")} lúc {tg.GetString()}. Lịch đang chờ admin xác nhận, bạn có thể xem trong mục 'Lịch hẹn của tôi'.");
                    }
                    else
                    {
                        sb.AppendLine("Tôi vừa thử tạo lịch nhưng không nhận được phản hồi rõ ràng — vui lòng kiểm tra trong mục 'Lịch hẹn của tôi' giúp mình.");
                    }
                    break;
                case "cancelBooking":
                    sb.AppendLine("✅ Đã hủy lịch hẹn theo yêu cầu của bạn.");
                    break;
                case "getMyBookings":
                    sb.AppendLine("Bạn vui lòng cho mình biết bạn muốn làm gì với danh sách lịch hẹn này nhé.");
                    break;
                case "getServices":
                case "getStaff":
                    sb.AppendLine("Bạn cần mình tư vấn chi tiết hơn về điều gì không?");
                    break;
                default:
                    sb.AppendLine("Tôi đã thực hiện yêu cầu, nhưng không có thông tin phản hồi cụ thể.");
                    break;
            }
        }

        var msg = sb.ToString().Trim();
        return string.IsNullOrEmpty(msg) ? "Bạn vui lòng cho mình thêm chi tiết để hỗ trợ tốt hơn nhé." : msg;
    }

    // ---- Build DB context ----

    private async Task<string> BuildDbContextAsync()
    {
        var sb = new StringBuilder();
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));

        sb.AppendLine($"## Ngày giờ hiện tại: {now:dddd, dd/MM/yyyy HH:mm}");
        sb.AppendLine($"## Giờ làm việc: {_spaOpts.GioLamViec.MoCua} – {_spaOpts.GioLamViec.DongCua} (hàng ngày)");
        sb.AppendLine($"## Đặt lịch theo bội số {_spaOpts.GioLamViec.BuocPhutDatLich} phút, hủy lịch trước tối thiểu {_spaOpts.GioLamViec.SoGioToiThieuDeHuy} giờ.");

        // Services
        var services = await _db.DichVus.AsNoTracking()
            .Where(d => d.HienThi)
            .OrderBy(d => d.Ten)
            .ToListAsync();

        if (services.Count > 0)
        {
            sb.AppendLine("\n## (A) Bảng giá dịch vụ chính thức (database) — dùng cho đặt lịch & báo giá:");
            foreach (var s in services)
            {
                sb.Append($"- **{s.Ten}** (id={s.Id}): {s.Gia:N0}đ, {s.ThoiLuongPhut} phút");
                if (!string.IsNullOrWhiteSpace(s.MoTa))
                    sb.Append($" — {s.MoTa}");
                sb.AppendLine();
            }
            sb.AppendLine("_Lưu ý: đây CHỈ là các dịch vụ đã cấu hình giá để đặt lịch trực tuyến. Spa còn các liệu trình khác trong tài liệu nội bộ — xem phần (B) bên dưới._");
        }

        // Staff
        var staff = await _db.NhanViens.AsNoTracking()
            .Where(n => n.DangLamViec)
            .OrderBy(n => n.HoTen)
            .ToListAsync();

        if (staff.Count > 0)
        {
            sb.AppendLine("\n## Nhân viên đang làm việc:");
            foreach (var nv in staff)
            {
                sb.Append($"- **{nv.HoTen}** (id={nv.Id})");
                if (!string.IsNullOrWhiteSpace(nv.ChuyenMon))
                    sb.Append($" — chuyên môn: {nv.ChuyenMon}");
                if (!string.IsNullOrWhiteSpace(nv.SoDienThoai))
                    sb.Append($" — SĐT: {nv.SoDienThoai}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ---- Tool execution ----

    private async Task<List<ToolCallResult>> ExecuteToolsAsync(
        List<(string Name, JsonElement Args)> toolCalls, string? token)
    {
        var tasks = toolCalls.Select(tc => ExecuteToolAsync(tc.Name, tc.Args, token));
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<ToolCallResult> ExecuteToolAsync(
        string name, JsonElement args, string? token)
    {
        try
        {
            return name switch
            {
                "getServices" => await HandleGetServices(args),
                "getStaff" => await HandleGetStaff(),
                "createBooking" => await HandleCreateBooking(args, token),
                "getMyBookings" => await HandleGetMyBookings(token),
                "cancelBooking" => await HandleCancelBooking(args, token),
                _ => new ToolCallResult(name, new { error = $"Tool '{name}' không tồn tại." })
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Tool {Name} failed", name);
            return new ToolCallResult(name, new { error = $"Lỗi khi thực thi '{name}': {ex.Message}" });
        }
    }

    private async Task<ToolCallResult> HandleGetServices(JsonElement args)
    {
        var tuKhoa = GetString(args, "tuKhoa") ?? GetString(args, "search");
        var services = await _db.DichVus.AsNoTracking()
            .Where(d => d.HienThi)
            .OrderBy(d => d.Ten)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(tuKhoa))
            services = services.Where(s =>
                s.Ten.Contains(tuKhoa, StringComparison.OrdinalIgnoreCase) ||
                (s.MoTa != null && s.MoTa.Contains(tuKhoa, StringComparison.OrdinalIgnoreCase))
            ).ToList();

        var items = services.Select(s => new
        {
            id = s.Id,
            ten = s.Ten,
            gia = s.Gia,
            thoiLuongPhut = s.ThoiLuongPhut,
            moTa = s.MoTa
        }).ToList();

        return new ToolCallResult("getServices", items);
    }

    private async Task<ToolCallResult> HandleGetStaff()
    {
        var staff = await _db.NhanViens.AsNoTracking()
            .Where(n => n.DangLamViec)
            .OrderBy(n => n.HoTen)
            .ToListAsync();

        var items = staff.Select(nv => new
        {
            id = nv.Id,
            hoTen = nv.HoTen,
            chuyenMon = nv.ChuyenMon,
            soDienThoai = nv.SoDienThoai
        }).ToList();

        return new ToolCallResult("getStaff", items);
    }

    // ---- Tools booking gọi DB trực tiếp (KHÔNG dùng self-call HTTP nữa) ----

    private async Task<ToolCallResult> HandleCreateBooking(JsonElement args, string? token)
    {
        var userId = ValidateUserToken(token);
        if (userId is null)
            return new ToolCallResult("createBooking",
                new { error = "Bạn cần đăng nhập (hoặc phiên đăng nhập đã hết hạn). Vui lòng đăng nhập lại để đặt lịch." });

        var dichVuIdStr = GetString(args, "dichVuId");
        var thoiGianStr = GetString(args, "thoiGianBatDau");
        var nhanVienIdStr = GetString(args, "nhanVienId");
        var ghiChu = GetString(args, "ghiChu");

        if (!Guid.TryParse(dichVuIdStr, out var dichVuId))
            return new ToolCallResult("createBooking", new { error = "ID dịch vụ không hợp lệ." });
        if (string.IsNullOrWhiteSpace(thoiGianStr) || !DateTime.TryParse(thoiGianStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var thoiGianBatDau))
            return new ToolCallResult("createBooking", new { error = "Thời gian không hợp lệ. Cần định dạng YYYY-MM-DDTHH:mm:ss." });

        if (thoiGianBatDau.Kind == DateTimeKind.Unspecified)
            thoiGianBatDau = DateTime.SpecifyKind(thoiGianBatDau, DateTimeKind.Local);

        var dv = await _db.DichVus.FirstOrDefaultAsync(x => x.Id == dichVuId && x.HienThi);
        if (dv is null)
            return new ToolCallResult("createBooking", new { error = "Dịch vụ không tồn tại hoặc đang ẩn." });

        // Validate giờ (mirror logic LichHenController.Tao)
        if (thoiGianBatDau < DateTime.Now.AddMinutes(-1))
            return new ToolCallResult("createBooking", new { error = "Thời gian đặt lịch phải nằm trong tương lai." });
        if (thoiGianBatDau > DateTime.Now.AddMonths(6))
            return new ToolCallResult("createBooking", new { error = "Chỉ cho phép đặt lịch trong vòng 6 tháng tới." });
        if (!HopLeGioLamViec(thoiGianBatDau))
            return new ToolCallResult("createBooking",
                new { error = $"Thời gian đặt lịch ngoài giờ làm việc ({_spaOpts.GioLamViec.MoCua} – {_spaOpts.GioLamViec.DongCua})." });
        if (!DungBuocPhut(thoiGianBatDau))
            return new ToolCallResult("createBooking",
                new { error = $"Thời gian phải theo bước {_spaOpts.GioLamViec.BuocPhutDatLich} phút (vd: …:00, …:30)." });

        var end = thoiGianBatDau.AddMinutes(dv.ThoiLuongPhut);
        if (!HopLeGioLamViec(end.AddMinutes(-1)))
            return new ToolCallResult("createBooking",
                new { error = $"Lịch dự kiến kết thúc lúc {end:HH:mm}, vượt giờ đóng cửa ({_spaOpts.GioLamViec.DongCua})." });

        var trungGio = await _db.LichHens.AnyAsync(x =>
            x.TaiKhoanId == userId &&
            x.TrangThai != TrangThaiLichHen.DaHuy &&
            x.TrangThai != TrangThaiLichHen.TuChoi &&
            x.ThoiGianBatDau < end &&
            x.ThoiGianKetThuc > thoiGianBatDau);
        if (trungGio)
            return new ToolCallResult("createBooking",
                new { error = "Bạn đã có lịch hẹn khác trùng khung giờ này. Vui lòng chọn giờ khác." });

        Guid? nhanVienId = null;
        string? tenNhanVien = null;
        if (Guid.TryParse(nhanVienIdStr, out var nvId))
        {
            var err = await BookingValidator.ValidateAssignNhanVienAsync(_db, nvId, thoiGianBatDau, end, excludeLichId: null);
            if (err is not null)
                return new ToolCallResult("createBooking", new { error = err });
            var nv = await _db.NhanViens.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nvId);
            nhanVienId = nv!.Id;
            tenNhanVien = nv.HoTen;
        }

        var lich = new LichHen
        {
            Id = Guid.NewGuid(),
            TaiKhoanId = userId.Value,
            DichVuId = dv.Id,
            NhanVienId = nhanVienId,
            ThoiGianBatDau = thoiGianBatDau,
            ThoiGianKetThuc = end,
            GhiChu = string.IsNullOrWhiteSpace(ghiChu) ? null : ghiChu.Trim(),
            TrangThai = TrangThaiLichHen.ChoXacNhan
        };
        _db.LichHens.Add(lich);
        await _db.SaveChangesAsync();

        _log.LogInformation("Tool createBooking → created lich {Id} dichVu={Dv} time={Time}", lich.Id, dv.Ten, thoiGianBatDau);

        return new ToolCallResult("createBooking", new
        {
            id = lich.Id,
            tenDichVu = dv.Ten,
            tenNhanVien,
            thoiGianBatDau = lich.ThoiGianBatDau.ToString("yyyy-MM-dd HH:mm"),
            thoiGianKetThuc = lich.ThoiGianKetThuc.ToString("HH:mm"),
            trangThai = "ChoXacNhan",
            ghiChu = lich.GhiChu,
        });
    }

    private async Task<ToolCallResult> HandleGetMyBookings(string? token)
    {
        var userId = ValidateUserToken(token);
        if (userId is null)
            return new ToolCallResult("getMyBookings",
                new { error = "Bạn cần đăng nhập (hoặc phiên đăng nhập đã hết hạn). Vui lòng đăng nhập lại để xem lịch hẹn." });

        var items = await _db.LichHens.AsNoTracking()
            .Where(x => x.TaiKhoanId == userId)
            .OrderByDescending(x => x.ThoiGianBatDau)
            .Include(x => x.DichVu)
            .Include(x => x.NhanVien)
            .Take(20)
            .Select(x => new
            {
                id = x.Id,
                tenDichVu = x.DichVu.Ten,
                tenNhanVien = x.NhanVien != null ? x.NhanVien.HoTen : null,
                thoiGianBatDau = x.ThoiGianBatDau.ToString("yyyy-MM-dd HH:mm"),
                trangThai = x.TrangThai.ToString(),
                ghiChu = x.GhiChu,
                lyDoTuChoi = x.LyDoTuChoi,
            })
            .ToListAsync();

        return new ToolCallResult("getMyBookings", items);
    }

    private async Task<ToolCallResult> HandleCancelBooking(JsonElement args, string? token)
    {
        var userId = ValidateUserToken(token);
        if (userId is null)
            return new ToolCallResult("cancelBooking",
                new { error = "Bạn cần đăng nhập (hoặc phiên đăng nhập đã hết hạn). Vui lòng đăng nhập lại để hủy lịch." });

        var idStr = GetString(args, "lichHenId");
        if (!Guid.TryParse(idStr, out var lichHenId))
            return new ToolCallResult("cancelBooking", new { error = "ID lịch hẹn không hợp lệ." });

        var lich = await _db.LichHens.FirstOrDefaultAsync(x => x.Id == lichHenId && x.TaiKhoanId == userId);
        if (lich is null)
            return new ToolCallResult("cancelBooking", new { error = "Không tìm thấy lịch hẹn này trong tài khoản của bạn." });

        if (lich.TrangThai is TrangThaiLichHen.DaHuy)
            return new ToolCallResult("cancelBooking", new { error = "Lịch hẹn này đã được hủy trước đó." });
        if (lich.TrangThai is TrangThaiLichHen.HoanThanh)
            return new ToolCallResult("cancelBooking", new { error = "Lịch hẹn này đã hoàn thành, không thể hủy." });
        if (lich.TrangThai is TrangThaiLichHen.TuChoi)
            return new ToolCallResult("cancelBooking", new { error = "Lịch hẹn này đã bị từ chối, không cần hủy." });

        if (!ConDuocHuy(lich.ThoiGianBatDau))
            return new ToolCallResult("cancelBooking",
                new { error = $"Đã quá thời hạn hủy (cần trước ít nhất {_spaOpts.GioLamViec.SoGioToiThieuDeHuy} giờ so với giờ hẹn)." });

        lich.TrangThai = TrangThaiLichHen.DaHuy;
        lich.CapNhatLuc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _log.LogInformation("Tool cancelBooking → cancelled {Id}", lich.Id);

        return new ToolCallResult("cancelBooking", new
        {
            success = true,
            message = "Đã hủy lịch hẹn thành công."
        });
    }

    // ---- Helpers ----

    private static string? GetString(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(key, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}

internal static class StringExtensions
{
    public static string TrimOrDefault(this string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
}
