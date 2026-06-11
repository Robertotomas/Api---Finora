namespace Finora.Application.Exceptions;

/// <summary>
/// Lançada no login quando as credenciais estão corretas mas o email ainda não foi
/// confirmado. O controller traduz isto para 403 com código EMAIL_NOT_CONFIRMED.
/// </summary>
public class EmailNotConfirmedException : Exception
{
    public string Email { get; }

    public EmailNotConfirmedException(string email)
        : base("Confirma o teu email antes de iniciar sessão.")
    {
        Email = email;
    }
}
