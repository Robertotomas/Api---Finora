namespace Finora.Application.Options;

/// <summary>Postmark transactional API — https://postmarkapp.com/developer/api/email-api</summary>
public class PostmarkOptions
{
    public const string SectionName = "Postmark";

    /// <summary>Server API token (Postmark → Servers → Server → API Tokens). Use env Postmark__ServerToken or user secrets.</summary>
    public string ServerToken { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = "FinoraFlow";

    /// <summary>Optional stream (default transactional is <c>outbound</c>).</summary>
    public string MessageStream { get; set; } = "outbound";
}
