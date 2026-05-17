# Báo cáo: Luồng Chatbot — Upstore Spa

## 1. Tổng quan
Ứng dụng có một chatbot AI tích hợp với backend để thực hiện RAG (retrieval-augmented generation), gọi hàm (tool calling) và thao tác nội bộ (đặt/hủy/xem lịch). Chatbot dùng Google Gemini (REST) cho embedding và generation, Qdrant làm vector store.

## 2. Thành phần chính (code)
- Backend orchestration: [SpaApi/Services/ChatOrchestratorService.cs](SpaApi/Services/ChatOrchestratorService.cs)
- Lớp gọi AI & hàm tiện ích: [SpaApi/Services/GeminiService.cs](SpaApi/Services/GeminiService.cs)
- Vector DB (Qdrant) client: [SpaApi/Services/QdrantService.cs](SpaApi/Services/QdrantService.cs)
- API chat endpoint: [SpaApi/Controllers/ChatController.cs](SpaApi/Controllers/ChatController.cs)
- API index (đưa tài liệu vào Qdrant): [SpaApi/Controllers/ChatIndexController.cs](SpaApi/Controllers/ChatIndexController.cs)
- Frontend chat UI: [SpaWeb/components/chat-widget.tsx](SpaWeb/components/chat-widget.tsx)
- Cấu hình: [SpaApi/Settings/ChatOptions.cs](SpaApi/Settings/ChatOptions.cs)

## 3. Luồng hoạt động (tóm tắt step-by-step)
1. Frontend (`chat-widget.tsx`) gửi POST `/api/chat` với payload: `{ message, history?, token? }`. `token` là JWT nếu user đã đăng nhập.
2. `ChatController.Chat` nhận yêu cầu, xây `internalBase` (URL nội bộ) rồi gọi `ChatOrchestratorService.RunAsync`.
3. `ChatOrchestratorService.RunAsync`:
   - Xây `dbContext` mô tả trạng thái hiện thời (giờ mở, danh sách dịch vụ, nhân viên) từ DB.
   - Thực hiện RAG: lấy embedding của truy vấn bằng `GeminiService.EmbedAsync`, tìm các document tương tự bằng `QdrantService.SearchAsync` → nếu có, nối vào context.
   - Gọi Gemini lần 1 (`GeminiService.ChatAsync`) truyền `systemPrompt` + history + message + `tools` (tool definitions). Gemini có thể trả về text, hoặc gọi hàm (function call).
   - Nếu Gemini trả tool-calls → `ChatOrchestratorService` thực thi các tool tương ứng (nội bộ): `getServices`, `getStaff`, `createBooking`, `getMyBookings`, `cancelBooking`. Các tool dùng DB trực tiếp hoặc gọi API nội bộ (khi cần xác thực, dùng token và `internalBase`).
   - Gửi kết quả tool execution về Gemini (`ChatWithToolResultsAsync`) để Gemini tạo câu trả lời cuối cùng.
   - Trả về `ChatResponse { Answer, ToolsUsed }` cho controller.
4. Frontend nhận `answer` và hiển thị; hiển thị badges cho tools nếu có.

## 4. Tool (function) hiện có và tham số
(định nghĩa nằm trong `ChatOrchestratorService.ToolDefinitions`)
- `getServices(tuKhoa?)` — trả về danh sách dịch vụ (id, tên, giá, thời lượng, mô tả).
- `getStaff()` — trả về danh sách nhân viên đang làm việc (id, họ tên, chuyên môn, SĐT).
- `createBooking(dichVuId, thoiGianBatDau, nhanVienId?, ghiChu?)` — tạo lịch bằng cách gọi `/api/lich-hen` (yêu cầu token).
- `getMyBookings()` — gọi `/api/lich-hen/me` (yêu cầu token).
- `cancelBooking(lichHenId)` — gọi `/api/lich-hen/{id}/huy` (yêu cầu token).

## 5. RAG / Indexing
- Endpoint để index tài liệu: `POST /api/chat/index` (có option `rebuild=true`).
- `ChatIndexController` thu thập:
  - Dịch vụ từ DB (mỗi dịch vụ → 1 doc)
  - Markdown trong thư mục `spa-docs/` (chia nhỏ theo tiêu đề)
- Sau đó thực hiện embedding hàng loạt bằng `GeminiService.EmbedAsync` và upsert vào Qdrant qua `QdrantService.UpsertAsync`.

## 6. Cấu hình quan trọng
File cấu hình mapping `ChatOptions`:
- `GeminiApiKey`, `GeminiModel`, `EmbeddingModel`
- `QdrantUrl`, `QdrantApiKey`, `CollectionName`, `EmbeddingDim`, `RagTopK`

Vị trí sửa: `appsettings.json` hoặc biến môi trường (tuỳ cấu hình host). Kiểm tra `Program.cs` để biết binding `ChatOptions`.

## 7. Tính năng & hành vi
- Hỗ trợ trả lời bằng tiếng Việt, có hướng dẫn ứng xử trong `BuildSystemPrompt`.
- Khi cần dữ liệu động (dịch vụ, nhân viên, lịch), AI sẽ gọi tools — tránh bịa thông tin.
- Hỗ trợ đặt lịch/hủy/xem lịch (yêu cầu token để thao tác tài khoản).
- Nếu Qdrant không sẵn sàng, RAG bị skip (không block luồng). Gemini vẫn trả lời dựa trên context hệ thống và history.

## 8. Hoạt động triển khai / chạy thử (quick-start)
1. Đặt `ChatOptions.GeminiApiKey` (API key Google Generative Language) trong `appsettings.json` hoặc biến môi trường.
2. Chạy Qdrant local hoặc cấu hình `QdrantUrl` tới service phù hợp.
3. Build & run API: `dotnet run` trong `SpaApi`.
4. (Tùy chọn) Index data vào Qdrant:

```bash
# từ thư mục SpaApi
curl -X POST "http://localhost:5000/api/chat/index"
```

5. Mở frontend (`SpaWeb`) và đảm bảo `NEXT_PUBLIC_API_BASE_URL` trỏ tới API.

## 9. Hạn chế & gợi ý cải tiến
- Hiện tại Gemini API key được lưu trực tiếp; nên sử dụng Secret Manager / biến môi trường trong môi trường production.
- Thiết lập retry/backoff khi gọi Gemini/ Qdrant.
- Giới hạn kích thước history hoặc token để tránh chi phí lớn và lỗi từ AI model.
- Thêm logging/telemetry cho từng tool call để debug hành vi AI khi gọi function.
- Xác thực chặt token trước khi gọi internal endpoints (hiện dùng token JWT trực tiếp qua header Bearer).

## 10. Tệp tham khảo chính
- [SpaApi/Services/ChatOrchestratorService.cs](SpaApi/Services/ChatOrchestratorService.cs)
- [SpaApi/Services/GeminiService.cs](SpaApi/Services/GeminiService.cs)
- [SpaApi/Services/QdrantService.cs](SpaApi/Services/QdrantService.cs)
- [SpaApi/Controllers/ChatController.cs](SpaApi/Controllers/ChatController.cs)
- [SpaApi/Controllers/ChatIndexController.cs](SpaApi/Controllers/ChatIndexController.cs)
- [SpaWeb/components/chat-widget.tsx](SpaWeb/components/chat-widget.tsx)

---
Báo cáo này do codebase hiện tại tóm lược tự động. Muốn tôi bổ sung: ví dụ luồng đặt lịch cụ thể (request/response mẫu), sơ đồ sequence, hay kiểm tra cấu hình `appsettings.json` không?