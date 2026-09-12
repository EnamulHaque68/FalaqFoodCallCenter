using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Infrastructure.Telephony;
using CallCenter.Infrastructure.Telephony.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CallCenter.Tests;

public sealed class TwilioTelephonyIntegrationTests : IAsyncLifetime
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

    private async Task AuthenticateAsync(string userName)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = IntegrationTestFactory.TestPassword
        });

        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
    }

    [Fact]
    public async Task GetVoiceToken_Unauthorized_Without_Jwt()
    {
        // Act
        var response = await client.GetAsync("/api/v1/telephony/token");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetVoiceToken_Returns_Token_For_Agent()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        // Act
        var response = await client.GetAsync("/api/v1/telephony/token");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TelephonyTokenResponseDto>();
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrWhiteSpace(dto.Token));
        Assert.False(string.IsNullOrWhiteSpace(dto.Identity));
        Assert.Equal("Simulated", dto.Provider);
        Assert.True(dto.ExpiresInSeconds > 0);
    }

    [Fact]
    public async Task TwilioTelephonyProvider_GenerateClientToken_Creates_Valid_Twilio_JWT()
    {
        // Arrange
        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Twilio",
            AccountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ApiKeySid = "SKaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ApiKeySecret = "secretsecretsecretsecretsecret12",
            TwimlAppSid = "APaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            VoiceNumber = "+15551234567",
            TokenTtlMinutes = 30
        });

        using var httpClient = new HttpClient();
        var provider = new TwilioTelephonyProvider(options, httpClient, NullLogger<TwilioTelephonyProvider>.Instance);
        var agentIdentity = "agent-test-identity-123";

        // Act
        var token = await provider.GenerateClientTokenAsync(agentIdentity, 30);

        // Assert
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Validate JWT format
        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(token));
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal("SKaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", jwt.Issuer);
        Assert.Equal("ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", jwt.Subject);
    }

    [Fact]
    public async Task InboundWebhook_Twilio_Returns_Dynamic_TwiML()
    {
        // Arrange
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CallSid", $"CA{Guid.NewGuid():N}"),
            new KeyValuePair<string, string>("From", "+15550001122"),
            new KeyValuePair<string, string>("To", "+15559998888"),
            new KeyValuePair<string, string>("CallStatus", "ringing")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/inbound")
        {
            Content = formContent
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<Response>", xml);
        Assert.True(xml.Contains("<Dial") || xml.Contains("<Say"));
    }

    [Fact]
    public async Task OutboundWebhook_Twilio_Returns_Dial_Number_TwiML()
    {
        // Arrange
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CallSid", $"CA{Guid.NewGuid():N}"),
            new KeyValuePair<string, string>("From", "client:agent_12345"),
            new KeyValuePair<string, string>("To", "+15558889999")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/outbound")
        {
            Content = formContent
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<Response>", xml);
        Assert.Contains("<Dial", xml);
        Assert.Contains("<Number>+15558889999</Number>", xml);
    }

    [Fact]
    public async Task OutboundWebhook_Twilio_ClientTransfer_Returns_Dial_Client_TwiML()
    {
        // Arrange
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CallSid", $"CA{Guid.NewGuid():N}"),
            new KeyValuePair<string, string>("From", "client:agent_1"),
            new KeyValuePair<string, string>("To", "client:agent_2")
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/outbound")
        {
            Content = formContent
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<Response>", xml);
        Assert.Contains("<Dial", xml);
        Assert.Contains("<Client>agent_2</Client>", xml);
    }
}
