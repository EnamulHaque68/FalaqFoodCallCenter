using CallCenter.Application.RealTime;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Agents;
using Moq;

namespace CallCenter.Tests;

public sealed class AgentStatusTests
{
    [Fact]
    public async Task Agent_status_can_change_to_available_and_notifies_realtime_layer()
    {
        await using var db = TestDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent { Id=agentId, UserId=userId, EmployeeCode="AG001", DisplayName="Agent 1", Status=AgentStatus.Offline, CreatedAt=DateTime.UtcNow });
        await db.SaveChangesAsync();

        var notifier = new Mock<IRealTimeNotifier>();
        var service = new AgentService(db, notifier.Object);

        var result = await service.UpdateStatusAsync(agentId, AgentStatus.Available);

        Assert.NotNull(result);
        Assert.Equal(AgentStatus.Available, result.Status);
        notifier.Verify(x => x.NotifyAgentStatusChangedAsync(It.IsAny<CallCenter.Application.RealTime.Contracts.AgentStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_agent_status_is_rejected()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var service = new AgentService(db, notifier.Object);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateStatusAsync(Guid.NewGuid(), (AgentStatus)999));
    }
}
