using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class GitHubApiService
{
    private readonly HttpClient _httpClient;

    public GitHubApiService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectPublisher/0.1");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public Task<GitHubUser> GetCurrentUserAsync(string token, CancellationToken cancellationToken) =>
        SendAsync<GitHubUser>(CreateRequest(HttpMethod.Get, "user", token), cancellationToken);

    public async Task<GitHubRepository?> GetRepositoryAsync(
        string owner,
        string name,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}",
            token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<GitHubRepository>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("GitHub returned an empty repository response.");
    }

    public async Task<GitHubRepository> CreateRepositoryAsync(
        string owner,
        GitHubUser signedInUser,
        string name,
        string description,
        bool isPrivate,
        string token,
        CancellationToken cancellationToken)
    {
        var route = owner.Equals(signedInUser.Login, StringComparison.OrdinalIgnoreCase)
            ? "user/repos"
            : $"orgs/{Uri.EscapeDataString(owner)}/repos";

        using var request = CreateRequest(HttpMethod.Post, route, token);
        request.Content = JsonContent.Create(new
        {
            name,
            description,
            @private = isPrivate,
            auto_init = false,
            has_issues = true,
            has_projects = true,
            has_wiki = false
        });
        return await SendAsync<GitHubRepository>(request, cancellationToken);
    }

    public async Task<GitHubRepository> UpdateRepositoryDescriptionAsync(
        string owner,
        string name,
        string description,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Patch,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}",
            token);
        request.Content = JsonContent.Create(new { description });
        return await SendAsync<GitHubRepository>(request, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string route, string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("GitHub returned an empty response.");
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string details;
        try
        {
            using var json = JsonDocument.Parse(text);
            details = json.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? text
                : text;
        }
        catch
        {
            details = text;
        }

        throw new HttpRequestException(
            $"GitHub API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {details}",
            null,
            response.StatusCode);
    }
}
