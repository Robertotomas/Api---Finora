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
        var subject = "Convite FinoraFlow — Junta-te ao teu agregado";
        var html = $"""
            <p>Olá,</p>
            <p><strong>{WebUtility.HtmlEncode(inviterDisplayName)}</strong> convidou-te para partilhar o agregado no FinoraFlow.</p>
            <p><a href="{WebUtility.HtmlEncode(registerUrl)}">Criar conta com este convite</a></p>
            <p>Se não esperavas este email, podes ignorá-lo.</p>
            """;
        var text = $"Olá,\n\n{inviterDisplayName} convidou-te para partilhar o agregado no FinoraFlow.\n\nCriar conta: {registerUrl}\n";
        return SendAsync(toEmail, subject, html, text, cancellationToken);
    }

    public Task SendCoupleInviteOtpAsync(string toEmail, string inviterDisplayName, string otpCode, CancellationToken cancellationToken = default)
    {
        var subject = "Código FinoraFlow — Convite para agregado";
        var html = $"""
            <p>Olá,</p>
            <p><strong>{WebUtility.HtmlEncode(inviterDisplayName)}</strong> convidou-te para partilhar o agregado no FinoraFlow.</p>
            <p>O teu código de verificação é: <strong>{WebUtility.HtmlEncode(otpCode)}</strong></p>
            <p>Expira em 15 minutos. Se não iniciaste este convite, ignora este email.</p>
            """;
        var text = $"Olá,\n\n{inviterDisplayName} convidou-te para partilhar o agregado.\n\nCódigo: {otpCode}\n\nExpira em 15 minutos.\n";
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
