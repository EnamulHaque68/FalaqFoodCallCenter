using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Customers.DTOs;

namespace CallCenter.Tests;

public sealed class CustomerManagementTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;
    private string adminToken = null!;
    private string supervisorToken = null!;
    private string agentToken = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        await factory.SeedAsync();

        adminToken = await LoginAsync("phase10-admin", IntegrationTestFactory.TestPassword);
        supervisorToken = await LoginAsync("phase10-supervisor", IntegrationTestFactory.TestPassword);
        agentToken = await LoginAsync("phase10-agent", IntegrationTestFactory.TestPassword);
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = username,
            Password = password
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return body!.AccessToken;
    }

    private void SetToken(string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task CreateCustomer_ValidPayload_ReturnsCreatedWithCrmIdAndNotes()
    {
        SetToken(adminToken);
        var uniquePhone = $"88019{Random.Shared.Next(10000000, 99999999)}";
        var crmId = $"CRM-{Guid.NewGuid():N}"[..12];

        var response = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Tariqul Islam",
            Phone = uniquePhone,
            Email = "tariqul@falaqfood.com",
            Address = "Plot 12, Gulshan 1, Dhaka",
            CrmCustomerId = crmId,
            Notes = "Prefers spicy options and fast delivery"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CustomerResponseDto>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Tariqul Islam", result.FullName);
        Assert.Equal(uniquePhone, result.Phone);
        Assert.Equal(crmId, result.CrmCustomerId);
        Assert.Equal("Prefers spicy options and fast delivery", result.Notes);
    }

    [Fact]
    public async Task CreateCustomer_DuplicatePhone_ReturnsConflict()
    {
        SetToken(adminToken);
        var uniquePhone = $"88018{Random.Shared.Next(10000000, 99999999)}";

        // Create initial customer
        var createFirst = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Original Customer",
            Phone = uniquePhone
        });
        createFirst.EnsureSuccessStatusCode();

        // Attempt second customer with same phone
        var createDuplicate = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Duplicate Phone Attempt",
            Phone = uniquePhone
        });

        Assert.Equal(HttpStatusCode.Conflict, createDuplicate.StatusCode);
    }

    [Fact]
    public async Task UpdateCustomer_ValidPayload_UpdatesCrmIdAndNotes()
    {
        SetToken(supervisorToken);
        var uniquePhone = $"88016{Random.Shared.Next(10000000, 99999999)}";

        var create = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Initial Name",
            Phone = uniquePhone,
            Notes = "Old Note"
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<CustomerResponseDto>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/customers/{created!.Id}", new UpdateCustomerRequestDto
        {
            FullName = "Updated Customer Name",
            Phone = uniquePhone,
            CrmCustomerId = "CRM-UPDATED-99",
            Notes = "Updated VIP Instructions"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Customer Name", updated.FullName);
        Assert.Equal("CRM-UPDATED-99", updated.CrmCustomerId);
        Assert.Equal("Updated VIP Instructions", updated.Notes);
    }

    [Fact]
    public async Task UpdateCustomer_DuplicatePhoneWithOtherCustomer_ReturnsConflict()
    {
        SetToken(adminToken);
        var phoneA = $"88015{Random.Shared.Next(10000000, 99999999)}";
        var phoneB = $"88014{Random.Shared.Next(10000000, 99999999)}";

        var custA = await (await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto { FullName = "Customer A", Phone = phoneA })).Content.ReadFromJsonAsync<CustomerResponseDto>();
        var custB = await (await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto { FullName = "Customer B", Phone = phoneB })).Content.ReadFromJsonAsync<CustomerResponseDto>();

        // Try updating B to phoneA
        var updateConflict = await client.PutAsJsonAsync($"/api/v1/customers/{custB!.Id}", new UpdateCustomerRequestDto
        {
            FullName = "Customer B",
            Phone = phoneA
        });

        Assert.Equal(HttpStatusCode.Conflict, updateConflict.StatusCode);
    }

    [Fact]
    public async Task SearchCustomers_ByCrmIdAndNotes_ReturnsMatches()
    {
        SetToken(adminToken);
        var uniquePhone = $"88013{Random.Shared.Next(10000000, 99999999)}";
        var crmId = $"SEARCH-CRM-{Guid.NewGuid():N}"[..18];
        var uniqueNote = $"RareSpecialNote-{Guid.NewGuid():N}"[..18];

        var create = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Search Test Customer",
            Phone = uniquePhone,
            CrmCustomerId = crmId,
            Notes = uniqueNote
        });
        create.EnsureSuccessStatusCode();

        // Search by CRM ID
        var searchCrm = await client.GetFromJsonAsync<CustomerPagedResultDto>($"/api/v1/customers?search={crmId}");
        Assert.NotNull(searchCrm);
        Assert.Contains(searchCrm.Items, x => x.CrmCustomerId == crmId);

        // Search by Notes
        var searchNotes = await client.GetFromJsonAsync<CustomerPagedResultDto>($"/api/v1/customers?search={uniqueNote}");
        Assert.NotNull(searchNotes);
        Assert.Contains(searchNotes.Items, x => x.Notes?.Contains(uniqueNote) == true);
    }

    [Fact]
    public async Task GetCustomerCalls_ReturnsLinkedCalls()
    {
        SetToken(agentToken);
        var response = await client.GetAsync($"/api/v1/customers/{factory.CustomerId}/calls?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DeleteCustomer_WithoutCalls_SucceedsForAdmin()
    {
        SetToken(adminToken);
        var uniquePhone = $"88012{Random.Shared.Next(10000000, 99999999)}";

        var create = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Temporary Customer",
            Phone = uniquePhone
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<CustomerResponseDto>();

        var deleteResponse = await client.DeleteAsync($"/api/v1/customers/{created!.Id}");
        Assert.True(deleteResponse.StatusCode == HttpStatusCode.NoContent || deleteResponse.StatusCode == HttpStatusCode.OK);

        var getAfterDelete = await client.GetAsync($"/api/v1/customers/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomer_WithCalls_ReturnsConflict()
    {
        SetToken(adminToken);
        // Create a call for factory.CustomerId first so there is a referencing call record
        var createCall = await client.PostAsJsonAsync("/api/v1/calls/incoming", new CreateIncomingCallRequestDto
        {
            CustomerId = factory.CustomerId,
            PhoneNumber = "8801712345678",
            CorrelationId = $"del-test-{Guid.NewGuid()}"
        });
        createCall.EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/api/v1/customers/{factory.CustomerId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CustomerAuthorization_SupervisorCanCreateAndUpdate_ForbiddenOnDelete()
    {
        SetToken(supervisorToken);
        var uniquePhone = $"88011{Random.Shared.Next(10000000, 99999999)}";

        // Supervisor can create
        var createResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Supervisor Created Customer",
            Phone = uniquePhone
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerResponseDto>();

        // Supervisor can update
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/customers/{created!.Id}", new UpdateCustomerRequestDto
        {
            FullName = "Supervisor Updated Customer",
            Phone = uniquePhone
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Supervisor CANNOT delete (Admin only)
        var deleteResponse = await client.DeleteAsync($"/api/v1/customers/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CustomerAuthorization_AgentCanRead_ForbiddenOnMutations()
    {
        SetToken(agentToken);

        // Agent can view list
        var listResponse = await client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        // Agent can view details
        var detailsResponse = await client.GetAsync($"/api/v1/customers/{factory.CustomerId}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        // Agent CANNOT create
        var createResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Agent Attempt",
            Phone = "8801700000000"
        });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        // Agent CANNOT update
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/customers/{factory.CustomerId}", new UpdateCustomerRequestDto
        {
            FullName = "Agent Mutate Attempt",
            Phone = "8801712345678"
        });
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);

        // Agent CANNOT delete
        var deleteResponse = await client.DeleteAsync($"/api/v1/customers/{factory.CustomerId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }
}
