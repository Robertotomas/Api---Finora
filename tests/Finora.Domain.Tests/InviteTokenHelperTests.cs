using Finora.Infrastructure.Services;

namespace Finora.Domain.Tests;

public class InviteTokenHelperTests
{
    // --- MaskEmail ---

    [Theory]
    [InlineData("joao@example.com", "j***@example.com")]
    [InlineData("ab@dominio.pt", "a***@dominio.pt")]
    [InlineData("a@b.com", "a@b.com")]            // local de 1 char → não mascara
    public void MaskEmail_MasksLocalPart(string email, string expected)
    {
        Assert.Equal(expected, InviteTokenHelper.MaskEmail(email));
    }

    [Theory]
    [InlineData("sememail")]      // sem @
    [InlineData("@dominio.com")]  // @ no início (at == 0)
    [InlineData("local@")]        // @ no fim (sem domínio)
    [InlineData("")]
    public void MaskEmail_InvalidFormats_ReturnMask(string email)
    {
        Assert.Equal("***", InviteTokenHelper.MaskEmail(email));
    }

    // --- Hash ---

    [Fact]
    public void Hash_IsDeterministic()
    {
        Assert.Equal(InviteTokenHelper.Hash("token-abc"), InviteTokenHelper.Hash("token-abc"));
    }

    [Fact]
    public void Hash_DiffersForDifferentInputs()
    {
        Assert.NotEqual(InviteTokenHelper.Hash("token-a"), InviteTokenHelper.Hash("token-b"));
    }

    [Fact]
    public void Hash_ProducesUppercaseHex64Chars()
    {
        var hash = InviteTokenHelper.Hash("qualquer");
        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars
        Assert.Matches("^[0-9A-F]+$", hash);
    }
}
