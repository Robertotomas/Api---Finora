using System.Net;

namespace Finora.Infrastructure.Services;

/// <summary>
/// Template HTML partilhado dos emails transacionais (identidade FinoraFlow).
/// Markup compatível com clientes de email: tabelas + estilos inline, largura 600px,
/// verde da marca (#166534). Os textos dinâmicos devem vir já escapados pelos callers
/// (as helpers <see cref="Button"/>/<see cref="CodeBox"/> escapam os seus argumentos).
/// </summary>
internal static class EmailTemplate
{
    private const string FontStack =
        "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";

    /// <summary>Envolve o conteúdo (<paramref name="bodyHtml"/>) no cartão com cabeçalho e rodapé da marca.</summary>
    public static string Render(string previewText, string heading, string bodyHtml)
    {
        var preview = WebUtility.HtmlEncode(previewText);
        var head = WebUtility.HtmlEncode(heading);
        return $$"""
<!DOCTYPE html>
<html lang="pt">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta name="color-scheme" content="light">
  <title>{{head}}</title>
</head>
<body style="margin:0;padding:0;background-color:#f1f5f9;">
  <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{preview}}</div>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f1f5f9;">
    <tr>
      <td align="center" style="padding:32px 16px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background-color:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 10px 30px -12px rgba(15,23,42,0.18);">
          <tr>
            <td style="padding:36px 40px 8px 40px;font-family:{{FontStack}};">
              <h1 style="margin:0 0 16px 0;font-size:21px;line-height:1.3;font-weight:800;letter-spacing:-0.02em;color:#0f172a;">{{head}}</h1>
              {{bodyHtml}}
            </td>
          </tr>
          <tr>
            <td style="padding:24px 40px 32px 40px;">
              <div style="height:1px;background-color:#eef2f6;line-height:1px;font-size:0;margin-bottom:20px;">&nbsp;</div>
              <p style="margin:0;font-family:{{FontStack}};font-size:12px;line-height:1.6;color:#94a3b8;">
                FinoraFlow · As tuas finanças, sob controlo.<br>
                Este é um email automático — por favor não respondas a esta mensagem.
              </p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    /// <summary>Parágrafo de corpo. <paramref name="innerHtml"/> pode conter HTML simples (ex.: &lt;strong&gt;).</summary>
    public static string Paragraph(string innerHtml) =>
        $"""<p style="margin:0 0 16px 0;font-family:{FontStack};font-size:15px;line-height:1.65;color:#475569;">{innerHtml}</p>""";

    /// <summary>Nota discreta (cinzenta), tipicamente depois do CTA.</summary>
    public static string Muted(string innerHtml) =>
        $"""<p style="margin:18px 0 0 0;font-family:{FontStack};font-size:13px;line-height:1.6;color:#94a3b8;">{innerHtml}</p>""";

    /// <summary>Botão CTA verde (bulletproof: tabela + bgcolor para clientes que ignoram border-radius).</summary>
    public static string Button(string label, string url)
    {
        var safeUrl = WebUtility.HtmlEncode(url);
        var safeLabel = WebUtility.HtmlEncode(label);
        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 4px 0;">
              <tr>
                <td align="center" bgcolor="#166534" style="border-radius:10px;">
                  <a href="{safeUrl}" target="_blank" style="display:inline-block;padding:13px 30px;font-family:{FontStack};font-size:15px;font-weight:700;line-height:1;color:#ffffff;text-decoration:none;border-radius:10px;">{safeLabel}</a>
                </td>
              </tr>
            </table>
            """;
    }

    /// <summary>Caixa de destaque para um código (OTP).</summary>
    public static string CodeBox(string code)
    {
        var safe = WebUtility.HtmlEncode(code);
        return $"""
            <div style="margin:8px 0 4px 0;font-family:{FontStack};font-size:32px;font-weight:800;letter-spacing:10px;text-align:center;color:#166534;background-color:#f0fdf4;border:1px solid #bbf7d0;border-radius:12px;padding:18px 12px;">{safe}</div>
            """;
    }
}
