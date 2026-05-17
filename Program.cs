using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SpaApi.Data;
using SpaApi.Security;
using SpaApi.Services;
using SpaApi.Settings;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Chuẩn hóa lỗi validation: thay vì ProblemDetails mặc định (không có field "message"),
// trả về { message, errors[] } để FE dùng `err?.message` show toast.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(opts =>
{
  opts.InvalidModelStateResponseFactory = ctx =>
  {
    var errors = ctx.ModelState
      .Where(kv => kv.Value!.Errors.Count > 0)
      .Select(kv => new
      {
        field = kv.Key,
        messages = kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
      })
      .ToArray();

    var firstMessage = errors
      .SelectMany(e => e.messages)
      .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
      ?? "Dữ liệu gửi lên không hợp lệ.";

    return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
    {
      message = firstMessage,
      errors
    });
  };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
  c.SwaggerDoc("v1", new OpenApiInfo { Title = "SpaApi", Version = "v1" });
  c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
  {
    Name = "Authorization",
    Type = SecuritySchemeType.Http,
    Scheme = "bearer",
    BearerFormat = "JWT",
    In = ParameterLocation.Header,
    Description = "Nhập: Bearer {token}"
  });
  c.AddSecurityRequirement(new OpenApiSecurityRequirement
  {
    {
      new OpenApiSecurityScheme
      {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
      },
      Array.Empty<string>()
    }
  });
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SpaOptions>(builder.Configuration.GetSection("Spa"));

// ---- AI Chat Services ----
builder.Services.Configure<ChatOptions>(opts =>
{
  opts.GeminiApiKey = builder.Configuration["GEMINI_API_KEY"]
    ?? builder.Configuration["Chat:GeminiApiKey"] ?? "";
  opts.GeminiModel = builder.Configuration["GEMINI_MODEL"]
    ?? builder.Configuration["Chat:GeminiModel"]
    ?? builder.Configuration["Chat__GeminiModel"]
    ?? "gemini-2.0-flash";
  opts.EmbeddingModel = builder.Configuration["EMBEDDING_MODEL"]
    ?? builder.Configuration["Chat:EmbeddingModel"]
    ?? "gemini-embedding-001";
  opts.QdrantUrl = builder.Configuration["QDRANT_URL"]
    ?? builder.Configuration["Chat:QdrantUrl"] ?? "http://localhost:6333";
  opts.QdrantApiKey = builder.Configuration["QDRANT_API_KEY"]
    ?? builder.Configuration["Chat:QdrantApiKey"] ?? "";
  opts.CollectionName = builder.Configuration["CHAT_COLLECTION_NAME"]
    ?? builder.Configuration["Chat:CollectionName"] ?? "spa_knowledge";
});
builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddScoped<QdrantService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ChatOrchestratorService>();
builder.Services.AddScoped<EmbeddingService>();

// ---- Cloudinary image storage ----
builder.Services.Configure<CloudinaryOptions>(opts =>
{
  opts.CloudName = builder.Configuration["CLOUDINARY_CLOUD_NAME"]
    ?? builder.Configuration["Cloudinary:CloudName"] ?? "";
  opts.ApiKey = builder.Configuration["CLOUDINARY_API_KEY"]
    ?? builder.Configuration["Cloudinary:ApiKey"] ?? "";
  opts.ApiSecret = builder.Configuration["CLOUDINARY_API_SECRET"]
    ?? builder.Configuration["Cloudinary:ApiSecret"] ?? "";
  opts.RootFolder = builder.Configuration["CLOUDINARY_ROOT_FOLDER"]
    ?? builder.Configuration["Cloudinary:RootFolder"] ?? "upstore-spa";
});
builder.Services.AddSingleton<IImageStorageService, CloudinaryImageStorageService>();

builder.Services.AddDbContext<SpaDbContext>(opt =>
{
  var cs = builder.Configuration.GetConnectionString("SpaDb");
  opt.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 0)));
});

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
builder.Services
  .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(opt =>
  {
    var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
    opt.TokenValidationParameters = new TokenValidationParameters
    {
      ValidateIssuer = true,
      ValidateAudience = true,
      ValidateLifetime = true,
      ValidateIssuerSigningKey = true,
      ValidIssuer = jwt.Issuer,
      ValidAudience = jwt.Audience,
      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
      ClockSkew = TimeSpan.FromSeconds(30),
      NameClaimType = ClaimTypes.NameIdentifier,
      RoleClaimType = ClaimTypes.Role
    };
  });

builder.Services.AddAuthorization();

builder.Services.AddCors(opt =>
{
  var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
  opt.AddPolicy("SpaWeb", p =>
  {
    if (origins.Length == 0)
      p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
      p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
  });
});

var app = builder.Build();

await SpaApi.Data.DbInitializer.InitializeAsync(app.Services);

var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", defaultValue: app.Environment.IsDevelopment());
if (swaggerEnabled)
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

// Forwarded headers từ proxy (Railway, Vercel, nginx...) để Request.Scheme/Host phản ánh đúng giá trị HTTPS thật
// Đặt TRƯỚC tất cả middleware khác để các middleware sau thấy đúng scheme.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
  ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
  // Railway/Vercel proxy nằm ngoài app, không thuộc subnet known → chấp nhận mọi proxy.
  KnownNetworks = { },
  KnownProxies = { }
});

app.UseCors("SpaWeb");
// KHÔNG dùng UseHttpsRedirection trong môi trường containerized:
// - Railway/Vercel proxy đã ép HTTPS từ ngoài, app chỉ nhận HTTP nội bộ.
// - HttpsRedirection sẽ trả 307 cho mọi request HTTP nội bộ → break self-call (tools chat) và proxy.
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

