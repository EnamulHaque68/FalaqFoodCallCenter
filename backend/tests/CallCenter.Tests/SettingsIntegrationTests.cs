using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Settings.DTOs;

namespace CallCenter.Tests;

public sealed class SettingsIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        await factory.SeedAsync();
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Settings_AreVisibleToSupervisor_ButOnlyAdminCanModify()
    {
        await AuthenticateAsync("phase10-supervisor");
        var read = await client.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var settings = await read.Content.ReadFromJsonAsync<SystemSettingsDto>();
        Assert.NotNull(settings);
        settings!.Call.RingTimeoutSeconds = 45;
        var denied = await client.PutAsJsonAsync("/api/v1/settings", settings);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await AuthenticateAsync("phase10-admin");
        var saved = await client.PutAsJsonAsync("/api/v1/settings", settings);
        saved.EnsureSuccessStatusCode();
        var result = await saved.Content.ReadFromJsonAsync<SystemSettingsDto>();
        Assert.Equal(45, result!.Call.RingTimeoutSeconds);
    }

    [Fact]
    public async Task Settings_RejectInvalidOperationalValues()
    {
        await AuthenticateAsync("phase10-admin");
        var settings = await client.GetFromJsonAsync<SystemSettingsDto>("/api/v1/settings");
        settings!.Queue.MaxQueueWaitSeconds = 0;

        var response = await client.PutAsJsonAsync("/api/v1/settings", settings);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task AuthenticateAsync(string userName)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = IntegrationTestFactory.TestPassword
        });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
    }
}
