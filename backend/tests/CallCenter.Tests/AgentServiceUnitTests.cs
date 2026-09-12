using CallCenter.Application.Audit;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Queues;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Agents;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CallCenter.Tests;

public sealed class AgentServiceUnitTests
{
    [Fact]
    public async Task GetAllAsync_FilteringAndSearch_ReturnsMatchingAgents()
    {
        await using var db = TestDbContextFactory.Create();
        var adminRole = new Role { Id = Guid.NewGuid(), Name = "Agent", CreatedAt = DateTime.UtcNow };
        db.Roles.Add(adminRole);

        var user1 = new User { Id = Guid.NewGuid(), RoleId = adminRole.Id, UserName = "agent.one", PasswordHash = "hash123", IsActive = true, CreatedAt = DateTime.UtcNow };
        var user2 = new User { Id = Guid.NewGuid(), RoleId = adminRole.Id, UserName = "agent.two", PasswordHash = "hash123", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(user1, user2);

        db.Agents.AddRange(
            new Agent { Id = Guid.NewGuid(), UserId = user1.Id, EmployeeCode = "AG001", DisplayName = "Sarah Connor", Team = "Support", Status = AgentStatus.Available, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Agent { Id = Guid.NewGuid(), UserId = user2.Id, EmployeeCode = "AG002", DisplayName = "John Connor", Team = "Sales", Status = AgentStatus.Busy, IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var service = new AgentService(db, new Mock<IRealTimeNotifier>().Object);

        // Filter by status
        var availableAgents = await service.GetAllAsync(status: AgentStatus.Available);
        Assert.Single(availableAgents);
        Assert.Equal("AG001", availableAgents[0].EmployeeCode);

        // Filter by team
        var salesAgents = await service.GetAllAsync(team: "Sales");
        Assert.Single(salesAgents);
        Assert.Equal("AG002", salesAgents[0].EmployeeCode);

        // Search term
        var searchAgents = await service.GetAllAsync(search: "Sarah");
        Assert.Single(searchAgents);
        Assert.Equal("Sarah Connor", searchAgents[0].DisplayName);
    }

    [Fact]
    public async Task CreateAsync_ValidAgent_CreatesUserAndAgentSuccessfully()
    {
        await using var db = TestDbContextFactory.Create();
        var role = new Role { Id = Guid.NewGuid(), Name = "Agent", CreatedAt = DateTime.UtcNow };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var mockNotifier = new Mock<IRealTimeNotifier>();
        var mockAudit = new Mock<IAuditLogService>();
        var service = new AgentService(db, mockNotifier.Object, auditLogService: mockAudit.Object);

        var request = new CreateAgentRequestDto
        {
            EmployeeCode = "AG100",
            DisplayName = "Kyle Reese",
            UserName = "kyle.reese",
            Password = "Password123!",
            Team = "Tier1",
            RoleName = "Agent"
        };

        var created = await service.CreateAsync(request);
        Assert.NotNull(created);
        Assert.Equal("AG100", created.EmployeeCode);
        Assert.Equal("Kyle Reese", created.DisplayName);
        Assert.Equal(AgentStatus.Offline, created.Status);

        // Verify entity persisted in database
        Assert.True(await db.Agents.AnyAsync(a => a.EmployeeCode == "AG100"));
        Assert.True(await db.Users.AnyAsync(u => u.UserName == "kyle.reese"));
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmployeeCode_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EmployeeCode = "AG200",
            DisplayName = "Existing Agent",
            Status = AgentStatus.Offline,
            CreatedAt = DateTime.UtcNow
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var service = new AgentService(db, new Mock<IRealTimeNotifier>().Object);

        var request = new CreateAgentRequestDto
        {
            EmployeeCode = "AG200",
            DisplayName = "New Agent",
            UserName = "new.agent"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateStatusAsync_ValidTransition_UpdatesStatusAndFiresNotification()
    {
        await using var db = TestDbContextFactory.Create();
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            UserId = userId,
            EmployeeCode = "AG300",
            DisplayName = "Marcus Wright",
            Status = AgentStatus.Offline,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockNotifier = new Mock<IRealTimeNotifier>();
        var service = new AgentService(db, mockNotifier.Object);

        var updated = await service.UpdateStatusAsync(agentId, AgentStatus.Available);
        Assert.NotNull(updated);
        Assert.Equal(AgentStatus.Available, updated.Status);

        mockNotifier.Verify(
            x => x.NotifyAgentStatusChangedAsync(It.Is<AgentStatusChangedEvent>(e => e.AgentId == agentId && e.Status == "Available"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_DeactivatedAgent_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            UserId = Guid.NewGuid(),
            EmployeeCode = "AG400",
            DisplayName = "Inactive Agent",
            Status = AgentStatus.Offline,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AgentService(db, new Mock<IRealTimeNotifier>().Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStatusAsync(agentId, AgentStatus.Available));
    }

    [Fact]
    public async Task DeactivateAsync_DeactivatesAgentAndUser_SetsOffline()
    {
        await using var db = TestDbContextFactory.Create();
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var role = new Role { Id = Guid.NewGuid(), Name = "Agent", CreatedAt = DateTime.UtcNow };
        db.Roles.Add(role);
        var user = new User { Id = userId, RoleId = role.Id, Role = role, UserName = "deact.agent", PasswordHash = "hash123", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        db.Agents.Add(new Agent
        {
            Id = agentId,
            UserId = userId,
            EmployeeCode = "AG500",
            DisplayName = "Agent To Deactivate",
            Status = AgentStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var mockNotifier = new Mock<IRealTimeNotifier>();
        var service = new AgentService(db, mockNotifier.Object);

        var deactivated = await service.DeactivateAsync(agentId);
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);
        Assert.Equal(AgentStatus.Offline, deactivated.Status);

        var dbUser = await db.Users.FindAsync(userId);
        Assert.False(dbUser!.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_WhenCallsReferenced_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            UserId = Guid.NewGuid(),
            EmployeeCode = "AG600",
            DisplayName = "Agent With Calls",
            Status = AgentStatus.Offline,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Calls.Add(new Call
        {
            Id = Guid.NewGuid(),
            AssignedAgentId = agentId,
            CustomerId = Guid.NewGuid(),
            PhoneNumber = "8801700000000",
            CorrelationId = "call-agent-del",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AgentService(db, new Mock<IRealTimeNotifier>().Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(agentId));
    }
}
