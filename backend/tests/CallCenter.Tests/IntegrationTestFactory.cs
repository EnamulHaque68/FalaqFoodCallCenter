using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;

namespace CallCenter.Tests;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"CallCenter-Integration-{Guid.NewGuid():N}";
    public const string TestPassword = "TestPassword123!";
    public Guid AdminUserId { get; private set; }
    public Guid SupervisorUserId { get; private set; }
    public Guid AgentUserId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid DispositionId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "CallCenter.Tests",
                ["Jwt:Audience"] = "CallCenter.Tests.Client",
                ["Jwt:SecretKey"] = "Phase10IntegrationSecretKey-AtLeast32Chars!",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Telephony:WebhookSecret"] = "Phase22-Enterprise-Webhook-Secret-9999!",
                ["Telephony:AccountSid"] = "test-mock-account-sid-12345",
                ["Telephony:AuthToken"] = "test-mock-auth-token-12345"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CallCenterDbContext>>();
            services.AddDbContext<CallCenterDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }

    public async Task SeedAsync()
    {
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        await db.Database.EnsureCreatedAsync();

        var adminRole=await db.Roles.SingleAsync(x=>x.Name=="Admin");
        var supervisorRole=await db.Roles.SingleAsync(x=>x.Name=="Supervisor");
        var agentRole=await db.Roles.SingleAsync(x=>x.Name=="Agent");
        AdminUserId=Guid.NewGuid(); SupervisorUserId=Guid.NewGuid(); AgentUserId=Guid.NewGuid(); AgentId=Guid.NewGuid(); CustomerId=Guid.NewGuid(); DispositionId=Guid.NewGuid();
        var hasher=new PasswordHasher<User>();
        var admin=new User { Id=AdminUserId, RoleId=adminRole.Id, UserName="phase10-admin", IsActive=true, CreatedAt=DateTime.UtcNow };
        admin.PasswordHash=hasher.HashPassword(admin,TestPassword);
        var supervisor=new User { Id=SupervisorUserId, RoleId=supervisorRole.Id, UserName="phase10-supervisor", IsActive=true, CreatedAt=DateTime.UtcNow };
        supervisor.PasswordHash=hasher.HashPassword(supervisor,TestPassword);
        var agent=new User { Id=AgentUserId, RoleId=agentRole.Id, UserName="phase10-agent", IsActive=true, CreatedAt=DateTime.UtcNow };
        agent.PasswordHash=hasher.HashPassword(agent,TestPassword);
        db.Users.AddRange(admin,supervisor,agent);
        db.Agents.Add(new Agent { Id=AgentId, UserId=AgentUserId, EmployeeCode="P10AG", DisplayName="Phase 10 Agent", Status=Domain.Enums.AgentStatus.Offline, CreatedAt=DateTime.UtcNow });
        db.Customers.Add(new Customer { Id=CustomerId, DisplayName="Phase 10 Customer", PhoneNumber="8801712345678", CreatedAt=DateTime.UtcNow });
        db.CallDispositions.Add(new CallDisposition { Id=DispositionId, Code="P10", Name="Phase 10 Completed", IsActive=true, CreatedAt=DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public string CreateExpiredToken(Guid userId, string userName, string roleName)
    {
        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<CallCenter.Infrastructure.Authentication.JwtTokenService>();
        var user = new User { Id = userId, UserName = userName, Role = new Role { Name = roleName } };
        var (token, _) = jwt.CreateToken(user, DateTime.UtcNow.AddMinutes(-10));
        return token;
    }
}
