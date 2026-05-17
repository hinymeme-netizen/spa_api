# Spa API (.NET)

## Yêu cầu

- Cài **.NET SDK 8** (vì môi trường hiện tại chưa có `dotnet` CLI).

## Chạy dự án

```bash
cd SpaApi
dotnet restore
dotnet run
```

Mặc định API chạy Swagger tại:

- `https://localhost:xxxx/swagger`

## Tài khoản Admin mặc định

Khi chạy lần đầu, hệ thống tự tạo:

- Email: `admin@spa.local`
- Mật khẩu: `Admin@12345`

Bạn **nên đổi** khóa JWT trong `SpaApi/appsettings.json` (`Jwt:Key`) trước khi triển khai thật.

