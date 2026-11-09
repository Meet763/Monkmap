using Microsoft.AspNetCore.DataProtection;

namespace WorkflowPlatform.Api.Integrations;

/// <summary>
/// Encrypts/decrypts secret strings (OAuth tokens, OAuth state) using ASP.NET Core Data Protection.
/// Keys are managed by the framework; in production point them at a shared/keyring store.
/// </summary>
public sealed class CredentialProtector
{
    private readonly IDataProtector _tokens;
    private readonly IDataProtector _state;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _tokens = provider.CreateProtector("Flowforge.IntegrationTokens.v1");
        _state = provider.CreateProtector("Flowforge.OAuthState.v1");
    }

    public string Protect(string plaintext) => _tokens.Protect(plaintext);
    public string Unprotect(string ciphertext) => _tokens.Unprotect(ciphertext);

    public string ProtectState(string plaintext) => _state.Protect(plaintext);
    public string UnprotectState(string ciphertext) => _state.Unprotect(ciphertext);
}
