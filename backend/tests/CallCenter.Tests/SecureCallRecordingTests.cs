using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Recordings;
using CallCenter.Application.Recordings.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class SecureCallRecordingTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;
    private Guid agentTwoId;
    private Guid agentTwoUserId;
    private Guid callAgent1Id;
    private Guid callAgent2Id;
    private Guid recAgent1Id;
    private Guid recAgent2Id;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        await factory.SeedAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IRecordingStorage>();
        var recordingService = scope.ServiceProvider.GetRequiredService<IRecordingService>();

        // Seed Agent Two
        var agentRole = await db.Roles.SingleAsync(x => x.Name == "Agent");
        var hasher = new PasswordHasher<User>();
        agentTwoUserId = Guid.NewGuid();
        var userTwo = new User
        {
            Id = agentTwoUserId,
            RoleId = agentRole.Id,
            UserName = "phase23-agent-two",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        userTwo.PasswordHash = hasher.HashPassword(userTwo, IntegrationTestFactory.TestPassword);
        db.Users.Add(userTwo);

        agentTwoId = Guid.NewGuid();
        var agentTwo = new Agent
        {
            Id = agentTwoId,
            UserId = agentTwoUserId,
            EmployeeCode = "P23AG2",
            DisplayName = "Phase 23 Agent Two",
            Status = AgentStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Agents.Add(agentTwo);

        // Seed Call 1 assigned to Agent 1 (factory.AgentId)
        callAgent1Id = Guid.NewGuid();
        var call1 = new Call
        {
            Id = callAgent1Id,
            CustomerId = factory.CustomerId,
            AssignedAgentId = factory.AgentId,
            CorrelationId = "CORR-P23-REC-01",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Completed,
            PhoneNumber = "8801711100011",
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            AnsweredAt = DateTime.UtcNow.AddMinutes(-9),
            EndedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        db.Calls.Add(call1);

        // Seed Call 2 assigned to Agent 2 (agentTwoId)
        callAgent2Id = Guid.NewGuid();
        var call2 = new Call
        {
            Id = callAgent2Id,
            CustomerId = factory.CustomerId,
            AssignedAgentId = agentTwoId,
            CorrelationId = "CORR-P23-REC-02",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Completed,
            PhoneNumber = "8801711100022",
            StartedAt = DateTime.UtcNow.AddMinutes(-8),
            AnsweredAt = DateTime.UtcNow.AddMinutes(-7),
            EndedAt = DateTime.UtcNow.AddMinutes(-3),
            CreatedAt = DateTime.UtcNow.AddMinutes(-8)
        };
        db.Calls.Add(call2);

        await db.SaveChangesAsync();

        // Save physical recordings through IRecordingService
        var sampleAudio1 = Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....SampleAudioForAgent1Call....");
        using var stream1 = new MemoryStream(sampleAudio1);
        var rec1Dto = await recordingService.SaveRecordingAsync(
            callAgent1Id,
            "REC-TWILIO-001",
            stream1,
            "audio/wav",
            TimeSpan.FromSeconds(240));
        recAgent1Id = rec1Dto.Id;

        var sampleAudio2 = Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....SampleAudioForAgent2Call....");
        using var stream2 = new MemoryStream(sampleAudio2);
        var rec2Dto = await recordingService.SaveRecordingAsync(
            callAgent2Id,
            "REC-TWILIO-002",
            stream2,
            "audio/wav",
            TimeSpan.FromSeconds(240));
        recAgent2Id = rec2Dto.Id;
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
        Assert.NotNull(payload);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
    }

    private void ClearAuth()
    {
        client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task UnauthenticatedAccess_ToRecordings_Returns401Unauthorized()
    {
        ClearAuth();

        var getList = await client.GetAsync("/api/v1/recordings");
        Assert.Equal(HttpStatusCode.Unauthorized, getList.StatusCode);

        var getSingle = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, getSingle.StatusCode);

        var stream = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/stream");
        Assert.Equal(HttpStatusCode.Unauthorized, stream.StatusCode);

        var download = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/download");
        Assert.Equal(HttpStatusCode.Unauthorized, download.StatusCode);

        var delete = await client.DeleteAsync($"/api/v1/recordings/{recAgent1Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task AgentIsolation_AgentCanAccessOwnCall_CannotAccessOtherAgentCall()
    {
        // Log in as Agent 1
        await AuthenticateAsync("phase10-agent");

        // 1. Agent 1 accessing Agent 1's call recording -> 200 OK
        var ownRec = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}");
        Assert.Equal(HttpStatusCode.OK, ownRec.StatusCode);
        var dto = await ownRec.Content.ReadFromJsonAsync<CallRecordingDto>();
        Assert.NotNull(dto);
        Assert.Equal(recAgent1Id, dto!.Id);
        Assert.Equal(callAgent1Id, dto.CallId);
        Assert.Equal(RecordingStatus.Available, dto.Status);

        // 2. Agent 1 accessing Agent 2's call recording -> 403 Forbidden
        var foreignRec = await client.GetAsync($"/api/v1/recordings/{recAgent2Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignRec.StatusCode);

        // 3. Search as Agent 1 -> only Agent 1's recording is returned
        var searchResp = await client.GetAsync("/api/v1/recordings");
        Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);
        var paged = await searchResp.Content.ReadFromJsonAsync<RecordingPagedResultDto>();
        Assert.NotNull(paged);
        Assert.All(paged!.Items, item => Assert.Equal(factory.AgentId, item.AssignedAgentId));
        Assert.Contains(paged.Items, i => i.Id == recAgent1Id);
        Assert.DoesNotContain(paged.Items, i => i.Id == recAgent2Id);
    }

    [Fact]
    public async Task Agent_CannotDownloadRecordings_SupervisorAndAdminCanDownload()
    {
        // 1. Agent attempt -> 403 Forbidden (Agent lacks Permissions.Recordings.Download)
        await AuthenticateAsync("phase10-agent");
        var agentDownload = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/download");
        Assert.Equal(HttpStatusCode.Forbidden, agentDownload.StatusCode);

        // 2. Supervisor attempt -> 200 OK with audio file attachment
        await AuthenticateAsync("phase10-supervisor");
        var supervisorDownload = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/download");
        Assert.Equal(HttpStatusCode.OK, supervisorDownload.StatusCode);
        Assert.Equal("audio/wav", supervisorDownload.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(supervisorDownload.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", supervisorDownload.Content.Headers.ContentDisposition!.DispositionType);
        var bytes = await supervisorDownload.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);

        // 3. Admin attempt -> 200 OK
        await AuthenticateAsync("phase10-admin");
        var adminDownload = await client.GetAsync($"/api/v1/recordings/{recAgent2Id}/download");
        Assert.Equal(HttpStatusCode.OK, adminDownload.StatusCode);
    }

    [Fact]
    public async Task PlaybackToken_GeneratesSignedToken_AllowsStreamingWithoutBearer()
    {
        // 1. As Agent 1, request a playback token
        await AuthenticateAsync("phase10-agent");
        var tokenResp = await client.PostAsync($"/api/v1/recordings/{recAgent1Id}/playback-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResp.StatusCode);

        var tokenDto = await tokenResp.Content.ReadFromJsonAsync<PlaybackTokenResponseDto>();
        Assert.NotNull(tokenDto);
        Assert.False(string.IsNullOrWhiteSpace(tokenDto!.Token));
        Assert.True(tokenDto.ExpiresAtUtc > DateTime.UtcNow);

        // 2. Clear authorization header (simulate HTML5 audio element playing directly via query param)
        ClearAuth();

        // 3. Stream with valid token -> 200 OK
        var streamResp = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/stream?token={Uri.EscapeDataString(tokenDto.Token)}");
        Assert.Equal(HttpStatusCode.OK, streamResp.StatusCode);
        Assert.Equal("audio/wav", streamResp.Content.Headers.ContentType?.MediaType);
        var audioBytes = await streamResp.Content.ReadAsByteArrayAsync();
        Assert.True(audioBytes.Length > 0);

        // 4. Stream with tampered token -> 401 Unauthorized
        var tamperedToken = tokenDto.Token.Substring(0, tokenDto.Token.Length - 5) + "abcde";
        var tamperedResp = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/stream?token={Uri.EscapeDataString(tamperedToken)}");
        Assert.Equal(HttpStatusCode.Unauthorized, tamperedResp.StatusCode);

        // 5. Stream with token for different recording -> 401 Unauthorized
        var wrongRecResp = await client.GetAsync($"/api/v1/recordings/{recAgent2Id}/stream?token={Uri.EscapeDataString(tokenDto.Token)}");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongRecResp.StatusCode);
    }

    [Fact]
    public async Task Streaming_SupportsRangeRequests_ForAudioScrubbing()
    {
        await AuthenticateAsync("phase10-agent");
        var tokenResp = await client.PostAsync($"/api/v1/recordings/{recAgent1Id}/playback-token", null);
        var tokenDto = await tokenResp.Content.ReadFromJsonAsync<PlaybackTokenResponseDto>();

        ClearAuth();

        // HTTP 206 Partial Content range request
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/recordings/{recAgent1Id}/stream?token={Uri.EscapeDataString(tokenDto!.Token)}");
        request.Headers.Range = new RangeHeaderValue(0, 5);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        var rangeBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(6, rangeBytes.Length);
    }

    [Fact]
    public async Task NoBlobsInSqlServer_DatabaseStoresOnlyMetadataAndStorageKey()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var recording = await db.CallRecordings.AsNoTracking().SingleAsync(r => r.Id == recAgent1Id);

        Assert.NotNull(recording.StorageKey);
        Assert.False(string.IsNullOrWhiteSpace(recording.StorageKey));
        Assert.Equal("audio/wav", recording.ContentType);
        Assert.NotNull(recording.ChecksumSha256);
        Assert.NotNull(recording.Duration);
        Assert.NotNull(recording.FileSizeBytes);
        Assert.Equal(RecordingStatus.Available, recording.Status);

        // Confirm database entity type has NO byte[] property
        var entityType = db.Model.FindEntityType(typeof(CallRecording));
        Assert.NotNull(entityType);
        var byteProperties = entityType!.GetProperties()
            .Where(p => p.ClrType == typeof(byte[]) || p.ClrType == typeof(MemoryStream))
            .ToList();
        Assert.Empty(byteProperties); // Zero binary blob properties in SQL entity!
    }

    [Fact]
    public async Task RetentionPolicy_ExpiredRecordingsPurgedFromDisk_AndStatusUpdated()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var recordingService = scope.ServiceProvider.GetRequiredService<IRecordingService>();
        var storage = scope.ServiceProvider.GetRequiredService<IRecordingStorage>();

        // Create an expired recording
        var sampleBytes = Encoding.UTF8.GetBytes("RIFF....WAVEfmt ....ExpiredRecordingSampleData....");
        using var stream = new MemoryStream(sampleBytes);
        var expiredRecDto = await recordingService.SaveRecordingAsync(
            callAgent1Id,
            "REC-EXPIRED-999",
            stream,
            "audio/wav",
            TimeSpan.FromSeconds(60));

        var expiredRec = await db.CallRecordings.SingleAsync(r => r.Id == expiredRecDto.Id);
        expiredRec.RetentionUntil = DateTime.UtcNow.AddDays(-2); // Expired 2 days ago
        await db.SaveChangesAsync();

        var fileExistedBefore = await storage.ExistsAsync(expiredRec.StorageKey);
        Assert.True(fileExistedBefore);

        // Trigger retention cleanup as Admin
        await AuthenticateAsync("phase10-admin");
        var cleanupResp = await client.PostAsync("/api/v1/recordings/retention/cleanup", null);
        Assert.Equal(HttpStatusCode.OK, cleanupResp.StatusCode);

        var result = await cleanupResp.Content.ReadFromJsonAsync<RetentionCleanupResultDto>();
        Assert.NotNull(result);
        Assert.True(result!.PurgedCount >= 1);
        Assert.Contains(expiredRecDto.Id, result.PurgedRecordingIds);

        // Verify DB record marked deleted via a fresh context scope
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var updatedRec = await verifyDb.CallRecordings.AsNoTracking().SingleAsync(r => r.Id == expiredRecDto.Id);
        Assert.True(updatedRec.IsDeleted);
        Assert.Equal(RecordingStatus.Deleted, updatedRec.Status);
        Assert.NotNull(updatedRec.DeletedAt);

        // Verify audio file was removed from storage
        var fileExistsAfter = await storage.ExistsAsync(expiredRec.StorageKey);
        Assert.False(fileExistsAfter);
    }

    [Fact]
    public async Task AuditTrail_StreamingDownloadingAndDeleting_WritesAuditLogs()
    {
        // 1. Stream
        await AuthenticateAsync("phase10-supervisor");
        var streamResp = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/stream");
        Assert.Equal(HttpStatusCode.OK, streamResp.StatusCode);

        // 2. Download
        var downloadResp = await client.GetAsync($"/api/v1/recordings/{recAgent1Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResp.StatusCode);

        // 3. Delete as Admin
        await AuthenticateAsync("phase10-admin");
        var deleteResp = await client.DeleteAsync($"/api/v1/recordings/{recAgent2Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // 4. Verify AuditLogs
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var auditLogs = await db.AuditLogs
            .Where(a => a.EntityName == "CallRecording")
            .ToListAsync();

        Assert.Contains(auditLogs, a => a.Action == "RecordingStreamed");
        Assert.Contains(auditLogs, a => a.Action == "RecordingDownloaded");
        Assert.Contains(auditLogs, a => a.Action == "RecordingDeleted");
    }
}
