using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace SpaApi.Services;

public interface IEmailSender
{
  /// <summary>
  /// Gửi email. Nếu chưa cấu hình → log ra console + return true (dev vẫn thấy link reset trong logs).
  /// Ưu tiên Resend HTTP API (qua port 443, không bị cloud provider block).
  /// Fallback SMTP nếu chưa có Resend key.
  /// </summary>
  Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public sealed class SmtpEmailSender : IEmailSender
{
  private readonly IConfiguration _config;
  private readonly ILogger<SmtpEmailSender> _logger;
  private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

  public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
  {
    _config = config;
    _logger = logger;
  }

  public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
  {
    // Ưu tiên Resend HTTP API — không bị cloud platform block port outbound
    var resendKey = Get("RESEND_API_KEY", "Resend:ApiKey");
    if (!string.IsNullOrWhiteSpace(resendKey))
    {
      return await SendViaResendAsync(resendKey, to, subject, htmlBody, ct);
    }

    // Fallback: SMTP cổ điển
    return await SendViaSmtpAsync(to, subject, htmlBody, ct);
  }

  private async Task<bool> SendViaResendAsync(string apiKey, string to, string subject, string html, CancellationToken ct)
  {
    // Resend yêu cầu sender domain phải verified trong account.
    // Nếu user set RESEND_FROM thì dùng (admin tự chịu trách nhiệm verify).
    // Ngược lại → dùng onboarding@resend.dev (sandbox, chỉ gửi tới chính email đăng ký Resend).
    // KHÔNG fallback sang SMTP_FROM vì Gmail address sẽ bị Resend reject 403.
    var fromEmail = Get("RESEND_FROM", "Resend:From");
    if (string.IsNullOrWhiteSpace(fromEmail) || fromEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
    {
      if (!string.IsNullOrWhiteSpace(fromEmail))
      {
        _logger.LogWarning(
          "[EmailSender/Resend] RESEND_FROM='{From}' không hợp lệ (Gmail không thể verify với Resend). " +
          "Đang fallback sang onboarding@resend.dev. Để gửi từ domain riêng, hãy verify domain trong resend.com/domains.",
          fromEmail);
      }
      fromEmail = "onboarding@resend.dev";
    }

    var fromName = Get("SMTP_FROM_NAME", "Smtp:FromName") ?? "Hin' Y Spa";
    var fromHeader = $"{fromName} <{fromEmail}>";

    try
    {
      var payload = new
      {
        from = fromHeader,
        to = new[] { to },
        subject,
        html,
      };
      var json = JsonSerializer.Serialize(payload);

      using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
      req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
      req.Content = new StringContent(json, Encoding.UTF8, "application/json");

      _logger.LogInformation("[EmailSender/Resend] Sending to {To} via api.resend.com from={From}...", to, fromHeader);
      using var res = await _http.SendAsync(req, ct);
      var body = await res.Content.ReadAsStringAsync(ct);

      if (res.IsSuccessStatusCode)
      {
        _logger.LogInformation("[EmailSender/Resend] ✅ Sent to {To}. Response: {Body}", to, body);
        return true;
      }

      _logger.LogError("[EmailSender/Resend] ❌ HTTP {Status} to {To}. Response: {Body}",
        (int)res.StatusCode, to, body);
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "[EmailSender/Resend] ❌ Exception sending to {To}: {Message}", to, ex.Message);
      return false;
    }
  }

  private async Task<bool> SendViaSmtpAsync(string to, string subject, string htmlBody, CancellationToken ct)
  {
    var host = Get("SMTP_HOST", "Smtp:Host");
    var portStr = Get("SMTP_PORT", "Smtp:Port");
    var user = Get("SMTP_USER", "Smtp:User");
    var pass = Get("SMTP_PASS", "Smtp:Pass");
    var from = Get("SMTP_FROM", "Smtp:From") ?? user;
    var fromName = Get("SMTP_FROM_NAME", "Smtp:FromName") ?? "Hin' Y Spa";

    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user)
        || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(from))
    {
      _logger.LogWarning(
        "[EmailSender] SMTP & Resend đều chưa cấu hình. Email sẽ chỉ log ra console.\n" +
        "Khuyến nghị: set RESEND_API_KEY (qua https://resend.com — không bị Railway block port).\n\n" +
        "==== EMAIL FAKE-SEND ====\nTo: {To}\nSubject: {Subject}\nBody:\n{Body}\n========================",
        to, subject, htmlBody);
      return true;
    }

    if (!int.TryParse(portStr, out var port)) port = 587;
    var cleanPass = pass!.Replace(" ", "").Trim();

    try
    {
      using var msg = new MailMessage();
      msg.From = new MailAddress(from!, fromName);
      msg.To.Add(new MailAddress(to));
      msg.Subject = subject;
      msg.Body = htmlBody;
      msg.IsBodyHtml = true;
      msg.SubjectEncoding = Encoding.UTF8;
      msg.BodyEncoding = Encoding.UTF8;

      var client = new SmtpClient(host, port)
      {
        UseDefaultCredentials = false,
      };
      client.Credentials = new NetworkCredential(user!.Trim(), cleanPass);
      client.EnableSsl = true;
      client.DeliveryMethod = SmtpDeliveryMethod.Network;
      client.Timeout = 30000;

      try
      {
        _logger.LogInformation("[EmailSender/SMTP] Sending to {To} via {Host}:{Port} as {User}...",
          to, host, port, user);
        await client.SendMailAsync(msg, ct);
        _logger.LogInformation("[EmailSender/SMTP] ✅ Sent to {To} subject={Subject}", to, subject);
        return true;
      }
      finally
      {
        client.Dispose();
      }
    }
    catch (SmtpException smtpEx)
    {
      _logger.LogError(smtpEx,
        "[EmailSender/SMTP] ❌ SMTP error to {To}: StatusCode={Status}, Message={Msg}. " +
        "Cloud platform có thể block port — set RESEND_API_KEY để fix.",
        to, smtpEx.StatusCode, smtpEx.Message);
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex,
        "[EmailSender/SMTP] ❌ Failed to {To} via {Host}:{Port}. Type={Type}, Message={Message}",
        to, host, portStr, ex.GetType().Name, ex.Message);
      return false;
    }
  }

  private string? Get(string envKey, string configKey)
  {
    var v = Environment.GetEnvironmentVariable(envKey);
    if (!string.IsNullOrWhiteSpace(v)) return v;
    return _config[configKey];
  }
}
