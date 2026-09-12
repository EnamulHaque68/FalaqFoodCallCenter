using CallCenter.Application.RealTime;
using CallCenter.Application.Routing;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Routing;
using CallCenter.Infrastructure.Routing.Strategies;
using Moq;

namespace CallCenter.Tests;

public sealed class RoutingTests
{
    [Fact]
    public async Task Routing_selects_oldest_available_agent_deterministically()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var callId = Guid.NewGuid();

        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "Customer",
            PhoneNumber = "8801712345678",
            CreatedAt = DateTime.UtcNow
        });

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        db.Agents.AddRange(
            new Agent
            {
                Id = first,
                UserId = Guid.NewGuid(),
                EmployeeCode = "A1",
                DisplayName = "A1",
                Status = AgentStatus.Available,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new Agent
            {
                Id = second,
                UserId = Guid.NewGuid(),
                EmployeeCode = "A2",
                DisplayName = "A2",
                Status = AgentStatus.Available,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });

        db.Calls.Add(new Call
        {
            Id = callId,
            CustomerId = customerId,
            CorrelationId = "r1",
            PhoneNumber = "8801712345678",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Queued,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        var service = new RoutingService(db, new Mock<IRealTimeNotifier>().Object);

        var result = await service.RouteCallAsync(callId);

        Assert.Equal(first, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);
    }

    [Fact]
    public async Task Routing_queues_call_when_no_available_agent_exists()
    {
        await using var db = TestDbContextFactory.Create();
        var queueId = Guid.NewGuid();
        var callId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "Customer",
            PhoneNumber = "8801712345678",
            CreatedAt = DateTime.UtcNow
        });

        db.CallQueues.Add(new CallQueue
        {
            Id = queueId,
            Name = "General",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        db.Calls.Add(new Call
        {
            Id = callId,
            CustomerId = customerId,
            CorrelationId = "r2",
            PhoneNumber = "8801712345678",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Queued,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        var service = new RoutingService(db, new Mock<IRealTimeNotifier>().Object);

        var result = await service.RouteCallAsync(callId);

        Assert.Null(result.AgentId);
        Assert.Equal(queueId, result.QueueId);
        Assert.Equal(CallStatus.Queued, result.CallStatus);
        Assert.Single(db.CallQueueEntries);
    }

    [Fact]
    public void LeastBusy_selects_agent_with_fewer_active_calls()
    {
        var strategy = new LeastBusyRoutingStrategy();
        var call = new Call { Id = Guid.NewGuid() };

        var agentBusy = new Agent { Id = Guid.NewGuid(), DisplayName = "Busy Agent", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var agentIdle = new Agent { Id = Guid.NewGuid(), DisplayName = "Idle Agent", CreatedAt = DateTime.UtcNow.AddHours(-2) };

        var context = new RoutingContext
        {
            Call = call,
            Candidates =
            [
                new RoutingCandidate { Agent = agentBusy, ActiveCallsCount = 2, CompletedCallsTodayCount = 1 },
                new RoutingCandidate { Agent = agentIdle, ActiveCallsCount = 0, CompletedCallsTodayCount = 1 }
            ]
        };

        var selected = strategy.SelectAgent(context);

        Assert.NotNull(selected);
        Assert.Equal(agentIdle.Id, selected.Id);
    }

    [Fact]
    public void LeastBusy_selects_agent_with_fewer_completed_calls_today()
    {
        var strategy = new LeastBusyRoutingStrategy();
        var call = new Call { Id = Guid.NewGuid() };

        var agentHeavy = new Agent { Id = Guid.NewGuid(), DisplayName = "Heavy Load", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var agentLight = new Agent { Id = Guid.NewGuid(), DisplayName = "Light Load", CreatedAt = DateTime.UtcNow.AddHours(-2) };

        var context = new RoutingContext
        {
            Call = call,
            Candidates =
            [
                new RoutingCandidate { Agent = agentHeavy, ActiveCallsCount = 0, CompletedCallsTodayCount = 10 },
                new RoutingCandidate { Agent = agentLight, ActiveCallsCount = 0, CompletedCallsTodayCount = 2 }
            ]
        };

        var selected = strategy.SelectAgent(context);

        Assert.NotNull(selected);
        Assert.Equal(agentLight.Id, selected.Id);
    }

    [Fact]
    public void LeastBusy_selects_longest_idle_agent()
    {
        var strategy = new LeastBusyRoutingStrategy();
        var call = new Call { Id = Guid.NewGuid() };

        var now = DateTime.UtcNow;
        var agentRecent = new Agent { Id = Guid.NewGuid(), DisplayName = "Recent Call", CreatedAt = now.AddHours(-1) };
        var agentLongIdle = new Agent { Id = Guid.NewGuid(), DisplayName = "Long Idle", CreatedAt = now.AddHours(-2) };

        var context = new RoutingContext
        {
            Call = call,
            Candidates =
            [
                new RoutingCandidate { Agent = agentRecent, ActiveCallsCount = 0, CompletedCallsTodayCount = 3, LastCallEndedAt = now.AddMinutes(-5) },
                new RoutingCandidate { Agent = agentLongIdle, ActiveCallsCount = 0, CompletedCallsTodayCount = 3, LastCallEndedAt = now.AddMinutes(-60) }
            ]
        };

        var selected = strategy.SelectAgent(context);

        Assert.NotNull(selected);
        Assert.Equal(agentLongIdle.Id, selected.Id);
    }

    [Fact]
    public void RoundRobin_cycles_through_available_agents()
    {
        var strategy = new RoundRobinRoutingStrategy();
        var call = new Call { Id = Guid.NewGuid() };

        var now = DateTime.UtcNow;
        var a1 = new Agent { Id = Guid.NewGuid(), DisplayName = "Agent 1", CreatedAt = now.AddMinutes(-10) };
        var a2 = new Agent { Id = Guid.NewGuid(), DisplayName = "Agent 2", CreatedAt = now.AddMinutes(-5) };

        var context = new RoutingContext
        {
            Call = call,
            Candidates =
            [
                new RoutingCandidate { Agent = a1 },
                new RoutingCandidate { Agent = a2 }
            ]
        };

        var first = strategy.SelectAgent(context);
        var second = strategy.SelectAgent(context);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void TeamBased_matches_agent_team_with_fallback()
    {
        var baseStrategy = new LeastBusyRoutingStrategy();
        var strategy = new TeamBasedRoutingStrategy(baseStrategy);
        var call = new Call { Id = Guid.NewGuid() };

        var agentBilling = new Agent { Id = Guid.NewGuid(), DisplayName = "Billing Agent", Team = "Billing", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var agentSupport = new Agent { Id = Guid.NewGuid(), DisplayName = "Support Agent", Team = "Support", CreatedAt = DateTime.UtcNow.AddHours(-2) };

        // Match Billing team
        var contextBilling = new RoutingContext
        {
            Call = call,
            PreferredTeam = "Billing",
            Candidates =
            [
                new RoutingCandidate { Agent = agentBilling },
                new RoutingCandidate { Agent = agentSupport }
            ]
        };

        var selectedBilling = strategy.SelectAgent(contextBilling);
        Assert.NotNull(selectedBilling);
        Assert.Equal(agentBilling.Id, selectedBilling.Id);

        // Fallback when team has no members
        var contextKitchen = new RoutingContext
        {
            Call = call,
            PreferredTeam = "Kitchen",
            Candidates =
            [
                new RoutingCandidate { Agent = agentBilling },
                new RoutingCandidate { Agent = agentSupport }
            ]
        };

        var selectedFallback = strategy.SelectAgent(contextKitchen);
        Assert.NotNull(selectedFallback);
    }

    [Fact]
    public void SkillBased_matches_agent_skill_with_fallback()
    {
        var baseStrategy = new LeastBusyRoutingStrategy();
        var strategy = new SkillBasedRoutingStrategy(baseStrategy);
        var call = new Call { Id = Guid.NewGuid() };

        var agentVip = new Agent { Id = Guid.NewGuid(), DisplayName = "VIP Specialist", EmployeeCode = "VIP-01", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var agentRegular = new Agent { Id = Guid.NewGuid(), DisplayName = "Regular Agent", EmployeeCode = "REG-01", CreatedAt = DateTime.UtcNow.AddHours(-2) };

        var contextVip = new RoutingContext
        {
            Call = call,
            RequiredSkill = "VIP",
            Candidates =
            [
                new RoutingCandidate { Agent = agentRegular },
                new RoutingCandidate { Agent = agentVip }
            ]
        };

        var selected = strategy.SelectAgent(contextVip);
        Assert.NotNull(selected);
        Assert.Equal(agentVip.Id, selected.Id);
    }

    [Fact]
    public void Priority_routes_high_priority_call()
    {
        var baseStrategy = new LeastBusyRoutingStrategy();
        var strategy = new PriorityRoutingStrategy(baseStrategy);
        var call = new Call { Id = Guid.NewGuid() };

        var agentHeavy = new Agent { Id = Guid.NewGuid(), DisplayName = "Heavy Load", CreatedAt = DateTime.UtcNow.AddHours(-1) };
        var agentUnoccupied = new Agent { Id = Guid.NewGuid(), DisplayName = "Unoccupied", CreatedAt = DateTime.UtcNow.AddHours(-2) };

        var contextPriority = new RoutingContext
        {
            Call = call,
            CallPriority = 5,
            Candidates =
            [
                new RoutingCandidate { Agent = agentHeavy, ActiveCallsCount = 1, CompletedCallsTodayCount = 8 },
                new RoutingCandidate { Agent = agentUnoccupied, ActiveCallsCount = 0, CompletedCallsTodayCount = 1 }
            ]
        };

        var selected = strategy.SelectAgent(contextPriority);
        Assert.NotNull(selected);
        Assert.Equal(agentUnoccupied.Id, selected.Id);
    }
}
