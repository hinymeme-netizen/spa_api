# Spa API — Backend (.NET 8)

API backend cho hệ thống quản lý spa, xây dựng bằng ASP.NET Core 8, MySQL, tích hợp AI Gemini và Qdrant.

---

## Yêu cầu hệ thống

| Công cụ | Phiên bản | Link tải |
|---------|-----------|----------|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| MySQL | 8.0+ | https://dev.mysql.com/downloads/ |
| Git | Bất kỳ | https://git-scm.com |

---

## Cài đặt & Chạy

### Bước 1 — Clone dự án

```bash
git clone <repo-url>
cd spa_api
```

### Bước 2 — Tạo file cấu hình

```bash
# Windows
copy .env.example .env

# Mac/Linux
cp .env.example .env
```

Mở file `.env` và điền các giá trị thực tế (xem hướng dẫn bên dưới).

### Bước 3 — Tạo database MySQL

```sql
CREATE DATABASE spa_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### Bước 4 — Cài Entity Framework CLI (nếu chưa có)

```bash
dotnet tool install --global dotnet-ef
```

### Bước 5 — Khôi phục package

```bash
dotnet restore
```

### Bước 6 — Migrate database

```bash
dotnet ef database update
```

### Bước 7 — Chạy API

```bash
dotnet run
```

API chạy tại:
- **http://localhost:5000** — API endpoint
- **http://localhost:5000/swagger** — Giao diện test API

---

## Cấu hình môi trường (.env)

### Database

```env
DB_HOST=localhost
DB_PORT=3306
DB_NAME=spa_db
DB_USER=root
DB_PASS=your_mysql_password
```

### JWT (Bảo mật token)

```env
JWT_KEY=your_secret_key_at_least_32_characters
JWT_ISSUER=SpaApi
JWT_AUDIENCE=SpaApi
JWT_EXPIRES_MINUTES=10080
```

> Tạo key ngẫu nhiên: `openssl rand -base64 48`

### Gemini AI

Lấy API key tại: https://aistudio.google.com/apikey

```env
GEMINI_API_KEY=AIzaSy_your_key_here
GEMINI_MODEL=gemini-2.5-flash
EMBEDDING_MODEL=gemini-embedding-001
```

### Qdrant (Vector DB cho AI chat)

Đăng ký miễn phí tại: https://cloud.qdrant.io

```env
QDRANT_URL=https://your-cluster.cloud.qdrant.io
QDRANT_API_KEY=your_qdrant_key
CHAT_COLLECTION_NAME=spa_knowledge
```

### Cloudinary (Upload ảnh)

Đăng ký miễn phí tại: https://cloudinary.com — vào Dashboard > API Keys

```env
CLOUDINARY_CLOUD_NAME=your_cloud_name
CLOUDINARY_API_KEY=your_api_key
CLOUDINARY_API_SECRET=your_api_secret
CLOUDINARY_ROOT_FOLDER=spa-uploads
```

### Google OAuth

Tạo tại: https://console.cloud.google.com > APIs & Services > Credentials

```env
GOOGLE_CLIENT_ID=your_client_id.apps.googleusercontent.com
```

### Email (Gmail SMTP)

Bật **Mật khẩu ứng dụng** trong tài khoản Google:  
Tài khoản Google → Bảo mật → Xác minh 2 bước → Mật khẩu ứng dụng

```env
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=your_email@gmail.com
SMTP_PASS=xxxx xxxx xxxx xxxx
SMTP_FROM=your_email@gmail.com
SMTP_FROM_NAME=Ten Spa
```

---

## Tài khoản mặc định (sau seed)

| Vai trò | Email | Mật khẩu |
|---------|-------|----------|
| Admin | admin@spa.local | Admin@12345 |

---

## Cấu trúc thư mục

```
spa_api/
├── Controllers/        # API endpoints
├── Services/           # Business logic, AI services
├── Models/             # Entity models
├── Migrations/         # Database migrations
├── Settings/           # Config classes
├── appsettings.json    # Cấu hình chính
└── .env                # Biến môi trường (không commit)
```

---

## Lệnh thường dùng

```bash
# Chạy dev
dotnet run

# Thêm migration mới
dotnet ef migrations add TenMigration

# Cập nhật database
dotnet ef database update

# Build production
dotnet publish -c Release
```
