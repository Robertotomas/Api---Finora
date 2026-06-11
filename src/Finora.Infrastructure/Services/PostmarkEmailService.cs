using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finora.Application.Interfaces;
using Finora.Application.Options;
using Microsoft.Extensions.Options;

namespace Finora.Infrastructure.Services;

public class PostmarkEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PostmarkOptions _options;

    public PostmarkEmailService(HttpClient httpClient, IOptions<PostmarkOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task SendCoupleInviteLinkAsync(string toEmail, string inviterDisplayName, string registerUrl, CancellationToken cancellationToken = default)
    {
        var subject = "Convite FinoraFlow — Junte-se ao seu agregado";
        var name = WebUtility.HtmlEncode(inviterDisplayName);
        var body =
            EmailTemplate.Paragraph("Olá,") +
            EmailTemplate.Paragraph($"<strong>{name}</strong> convidou-o para partilhar o agregado no FinoraFlow. Faça a gestão das finanças do casal num só lugar.") +
            EmailTemplate.Button("Aceitar convite", registerUrl) +
            EmailTemplate.Muted("Se não esperava este email, pode ignorá-lo com segurança.");
        var html = EmailTemplate.Render($"{inviterDisplayName} convidou-o para o agregado no FinoraFlow.", "Convite para o seu agregado", body);
        var text = $"Olá,\n\n{inviterDisplayName} convidou-o para partilhar o agregado no FinoraFlow.\n\nAceitar convite: {registerUrl}\n";
        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    public Task SendCoupleInviteOtpAsync(string toEmail, string inviterDisplayName, string otpCode, CancellationToken cancellationToken = default)
    {
        var subject = "Código FinoraFlow — Convite para agregado";
        var name = WebUtility.HtmlEncode(inviterDisplayName);
        var body =
            EmailTemplate.Paragraph("Olá,") +
            EmailTemplate.Paragraph($"<strong>{name}</strong> convidou-o para partilhar o agregado no FinoraFlow. Use o código abaixo para confirmar.") +
            EmailTemplate.CodeBox(otpCode) +
            EmailTemplate.Muted("O código expira em 15 minutos. Se não iniciou este convite, pode ignorar este email.");
        var html = EmailTemplate.Render("O seu código de convite FinoraFlow.", "O seu código de convite", body);
        var text = $"Olá,\n\n{inviterDisplayName} convidou-o para partilhar o agregado.\n\nCódigo: {otpCode}\n\nExpira em 15 minutos.\n";
        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    public Task SendPasswordResetLinkAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        var subject = "FinoraFlow — Redefinir a sua palavra-passe";
        var body =
            EmailTemplate.Paragraph("Olá,") +
            EmailTemplate.Paragraph("Recebemos um pedido para redefinir a palavra-passe da sua conta FinoraFlow. Clique no botão para escolher uma nova.") +
            EmailTemplate.Button("Redefinir palavra-passe", resetUrl) +
            EmailTemplate.Muted("Este link expira em 1 hora. Se não pediu esta alteração, ignore este email — a sua palavra-passe não será alterada.");
        var html = EmailTemplate.Render("Redefina a palavra-passe da sua conta FinoraFlow.", "Redefinir a palavra-passe", body);
        var text = $"Olá,\n\nRecebemos um pedido para redefinir a palavra-passe da sua conta FinoraFlow.\n\nRedefinir palavra-passe: {resetUrl}\n\nEste link expira em 1 hora. Se não pediu esta alteração, ignore este email.\n";
        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    public Task SendEmailConfirmationLinkAsync(string toEmail, string confirmationUrl, CancellationToken cancellationToken = default)
    {
        var subject = "FinoraFlow — Confirme o seu email";
        var body =
            EmailTemplate.Paragraph("Olá,") +
            EmailTemplate.Paragraph("Bem-vindo ao FinoraFlow! Falta só um passo: confirme o seu email para ativar a conta.") +
            EmailTemplate.Button("Confirmar email", confirmationUrl) +
            EmailTemplate.Muted("Este link expira em 24 horas. Se não criou esta conta, pode ignorar este email.");
        var html = EmailTemplate.Render("Confirme o seu email para ativar a conta FinoraFlow.", "Confirme o seu email", body);
        var text = $"Olá,\n\nBem-vindo ao FinoraFlow! Confirme o seu email para ativar a conta.\n\nConfirmar email: {confirmationUrl}\n\nEste link expira em 24 horas. Se não criou esta conta, ignore este email.\n";
        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    private async Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
    {
        var token = _options.ServerToken?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "Postmark:ServerToken não está configurado. Define Postmark__ServerToken (user secrets ou variável de ambiente) com o Server API token do Postmark.");
        }

        var fromEmail = _options.FromEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(fromEmail))
            throw new InvalidOperationException("Postmark:FromEmail não está configurado.");

        var fromName = string.IsNullOrWhiteSpace(_options.FromName) ? "FinoraFlow" : _options.FromName.Trim();
        var from = $"{fromName} <{fromEmail}>";

        var payload = new PostmarkEmailRequest
        {
            From = from,
            To = to.Trim(),
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            MessageStream = string.IsNullOrWhiteSpace(_options.MessageStream) ? "outbound" : _options.MessageStream.Trim()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "email");
        request.Headers.TryAddWithoutValidation("X-Postmark-Server-Token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Postmark send failed: {(int)response.StatusCode} {body}");
        }

        using (var doc = JsonDocument.Parse(body))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("ErrorCode", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("Message", out var m) ? m.GetString() : body;
                throw new InvalidOperationException($"Postmark send failed: {msg}");
            }
        }
    }

    private sealed class PostmarkEmailRequest
    {
        [JsonPropertyName("From")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("To")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("Subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("HtmlBody")]
        public string HtmlBody { get; set; } = string.Empty;

        [JsonPropertyName("TextBody")]
        public string TextBody { get; set; } = string.Empty;

        [JsonPropertyName("MessageStream")]
        public string MessageStream { get; set; } = "outbound";
    }
}
