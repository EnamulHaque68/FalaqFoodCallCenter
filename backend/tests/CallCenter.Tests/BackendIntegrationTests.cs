using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;

namespace CallCenter.Tests;

public sealed class BackendIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;
    private string token = null!;

    public async Task InitializeAsync()
    {
        client=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await factory.SeedAsync();
        var login=await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto { UserName="phase10-admin", Password=IntegrationTestFactory.TestPassword });
        login.EnsureSuccessStatusCode();
        var body=await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        token=body!.AccessToken;
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
    }

    public Task DisposeAsync(){ client.Dispose(); factory.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Login_returns_access_token()
    {
        var response=await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto { UserName="phase10-admin", Password=IntegrationTestFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(result!.AccessToken));
        Assert.Equal("Admin",result.Role);
    }

    [Fact]
    public async Task Agent_status_endpoint_updates_status()
    {
        var response=await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto { Status=AgentStatus.Available });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.Equal(AgentStatus.Available,result!.Status);
    }

    [Fact]
    public async Task Incoming_call_can_be_created()
    {
        var response=await client.PostAsJsonAsync("/api/v1/calls/incoming", new CreateIncomingCallRequestDto { CustomerId=factory.CustomerId, PhoneNumber="+8801712345678", CorrelationId=$"in-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.Equal(CallDirection.Inbound,result!.Direction);
        Assert.Equal(CallStatus.Queued,result.Status);
    }

    [Fact]
    public async Task Outgoing_call_can_be_created()
    {
        var response=await client.PostAsJsonAsync("/api/v1/calls/outgoing", new CreateOutgoingCallRequestDto { CustomerId=factory.CustomerId, PhoneNumber="+8801712345678", CorrelationId=$"out-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.Equal(CallDirection.Outbound,result!.Direction);
        Assert.Equal(CallStatus.Ringing,result.Status);
    }

    [Fact]
    public async Task Call_can_be_completed_with_active_disposition()
    {
        var create=await client.PostAsJsonAsync("/api/v1/calls/incoming", new CreateIncomingCallRequestDto { CustomerId=factory.CustomerId, PhoneNumber="8801712345678", CorrelationId=$"complete-{Guid.NewGuid()}" });
        create.EnsureSuccessStatusCode();
        var call=await create.Content.ReadFromJsonAsync<CallResponseDto>();
        await client.PutAsJsonAsync($"/api/v1/calls/{call!.Id}/transition", new TransitionCallRequestDto { Status=CallStatus.Ringing });
        await client.PutAsJsonAsync($"/api/v1/calls/{call.Id}/transition", new TransitionCallRequestDto { Status=CallStatus.Connected });
        var completed=await client.PostAsJsonAsync($"/api/v1/calls/{call.Id}/complete", new CompleteCallRequestDto { DispositionId=factory.DispositionId });
        Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
        var result=await completed.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.Equal(CallStatus.Completed,result!.Status);
        Assert.Equal(factory.DispositionId,result.CallDispositionId);
    }

    [Fact]
    public async Task Call_history_returns_created_calls()
    {
        var correlation=$"history-{Guid.NewGuid()}";
        var create=await client.PostAsJsonAsync("/api/v1/calls/outgoing", new CreateOutgoingCallRequestDto { CustomerId=factory.CustomerId, PhoneNumber="8801712345678", CorrelationId=correlation });
        create.EnsureSuccessStatusCode();
        var history=await client.GetAsync($"/api/v1/calls/history?customerId={factory.CustomerId}&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK,history.StatusCode);
        using var json=JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("totalCount").GetInt32() >= 1);
    }
}
