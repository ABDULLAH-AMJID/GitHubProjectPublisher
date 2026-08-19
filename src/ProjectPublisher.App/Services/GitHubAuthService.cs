using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class GitHubAuthService
{
    public const string RequiredScopes = "repo read:user workflow";
    private static readonly Uri DeviceCodeUri = new("https://github.com/login/device/code");
    private static readonly Uri AccessTokenUri = new("https://github.com/login/oauth/access_token");
    private readonly HttpClient _httpClient;
    private readonly CredentialVault _vault;

    public GitHubAuthService(CredentialVault vault, HttpClient? httpClient = null)
    {
        _vault = vault;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectPublisher/0.1");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<DeviceCodeResponse> BeginDeviceFlowAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        EnsureClientId(clientId);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["scope"] = RequiredScopes
        });
        using var response = await _httpClient.PostAsync(DeviceCodeUri, content, cancellationToken);
        await EnsureSuccessAsync(response, "GitHub did not start device login.", cancellationToken);
        return await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("GitHub returned an empty device login response.");
    }

    public async Task<TokenBundle> CompleteDeviceFlowAsync(
        string clientId,
        DeviceCodeResponse deviceCode,
        CancellationToken cancellationToken)
    {
        var delaySeconds = Math.Max(deviceCode.Interval, 5);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId.Trim(),
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            using var response = await _httpClient.PostAsync(AccessTokenUri, content, cancellationToken);
            await EnsureSuccessAsync(response, "GitHub device login failed.", cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: cancellationToken)
                        ?? throw new InvalidOperationException("GitHub returned an empty token response.");

            if (!string.IsNullOrWhiteSpace(token.AccessToken))
            {
                var bundle = new TokenBundle
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken,
                    ExpiresAtUtc = token.ExpiresIn is > 0
                        ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
                        : null,
                    GrantedScopes = token.Scope
                };
                _vault.Save(bundle);
                return bundle;
            }

            switch (token.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    delaySeconds += 5;
                    continue;
                case "expired_token":
                    throw new InvalidOperationException("The GitHub device code expired. Start sign-in again.");
                case "access_denied":
                    throw new UnauthorizedAccessException("GitHub sign-in was cancelled or denied.");
                default:
                    throw new InvalidOperationException(token.ErrorDescription ?? token.Error ?? "GitHub sign-in failed.");
            }
        }

        throw new TimeoutException("The GitHub device code expired. Start sign-in again.");
    }

    public async Task<TokenBundle?> GetSavedValidTokenAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var bundle = _vault.Read();
        if (bundle is null || !HasRequiredScopes(bundle.GrantedScopes)) return null;

        if (bundle.ExpiresAtUtc is null || bundle.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            return bundle;

        if (string.IsNullOrWhiteSpace(bundle.RefreshToken) || string.IsNullOrWhiteSpace(clientId))
            return null;

        return await RefreshAsync(clientId, bundle.RefreshToken, bundle.GrantedScopes, cancellationToken);
    }

    public void SignOut() => _vault.Delete();

    public static void OpenVerificationPage(DeviceCodeResponse response)
    {
        Process.Start(new ProcessStartInfo(response.VerificationUri) { UseShellExecute = true });
    }

    private async Task<TokenBundle> RefreshAsync(
        string clientId,
        string refreshToken,
        string? currentScopes,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await _httpClient.PostAsync(AccessTokenUri, content, cancellationToken);
        await EnsureSuccessAsync(response, "GitHub sign-in could not be refreshed.", cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new UnauthorizedAccessException(token?.ErrorDescription ?? "GitHub sign-in expired. Please connect again.");

        var bundle = new TokenBundle
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken ?? refreshToken,
            ExpiresAtUtc = token.ExpiresIn is > 0
                ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
                : null,
            GrantedScopes = token.Scope ?? currentScopes
        };
        _vault.Save(bundle);
        return bundle;
    }

    public static bool HasRequiredScopes(string? grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes)) return false;
        var granted = grantedScopes
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RequiredScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(granted.Contains);
    }

    private static void EnsureClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Enter your GitHub OAuth App Client ID first.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"{message} HTTP {(int)response.StatusCode}. {details}");
    }
}
