using CallCenter.Application.Dispositions.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Dispositions;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Tests;

public sealed class DispositionServiceUnitTests
{
    [Fact]
    public async Task GetAllAsync_ActiveOnlyAndIncludeInactive_FiltersCorrectly()
    {
        await using var db = TestDbContextFactory.Create();
        db.CallDispositions.AddRange(
            new CallDisposition { Id = Guid.NewGuid(), Code = "SALE", Name = "Sale Made", SortOrder = 1, IsActive = true, CreatedAt = DateTime.UtcNow },
            new CallDisposition { Id = Guid.NewGuid(), Code = "INQUIRY", Name = "General Inquiry", SortOrder = 2, IsActive = true, CreatedAt = DateTime.UtcNow },
            new CallDisposition { Id = Guid.NewGuid(), Code = "DEPRECATED", Name = "Deprecated Option", SortOrder = 3, IsActive = false, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var service = new DispositionService(db);

        // Default: active only
        var activeDispositions = await service.GetAllAsync(includeInactive: false);
        Assert.Equal(2, activeDispositions.Count);
        Assert.DoesNotContain(activeDispositions, d => d.Code == "DEPRECATED");

        // Include inactive
        var allDispositions = await service.GetAllAsync(includeInactive: true);
        Assert.Equal(3, allDispositions.Count);
        Assert.Contains(allDispositions, d => d.Code == "DEPRECATED");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsDispositionOrNull()
    {
        await using var db = TestDbContextFactory.Create();
        var id = Guid.NewGuid();
        db.CallDispositions.Add(new CallDisposition
        {
            Id = id,
            Code = "CALLBACK",
            Name = "Call Back Requested",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DispositionService(db);

        var found = await service.GetByIdAsync(id);
        Assert.NotNull(found);
        Assert.Equal("CALLBACK", found.Code);

        var notFound = await service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(notFound);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_NormalizesCodeAndSaves()
    {
        await using var db = TestDbContextFactory.Create();
        var service = new DispositionService(db);

        var request = new CreateDispositionRequestDto
        {
            Code = "  escalate  ",
            Name = "Escalated to Supervisor",
            Description = "Requires supervisor attention",
            RequiresFollowUp = true,
            RequiresNotes = true,
            SortOrder = 5
        };

        var created = await service.CreateAsync(request);
        Assert.NotNull(created);
        Assert.Equal("ESCALATE", created.Code); // Uppercase trimmed
        Assert.Equal("Escalated to Supervisor", created.Name);
        Assert.True(created.RequiresFollowUp);
        Assert.True(created.RequiresNotes);
        Assert.True(created.IsActive);

        Assert.True(await db.CallDispositions.AnyAsync(d => d.Code == "ESCALATE"));
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        db.CallDispositions.Add(new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = "EXISTING",
            Name = "Existing",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DispositionService(db);

        var duplicateRequest = new CreateDispositionRequestDto
        {
            Code = "existing",
            Name = "Duplicate Entry"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(duplicateRequest));
    }

    [Fact]
    public async Task UpdateAsync_ValidUpdate_UpdatesAndReturnsDto()
    {
        await using var db = TestDbContextFactory.Create();
        var id = Guid.NewGuid();
        db.CallDispositions.Add(new CallDisposition
        {
            Id = id,
            Code = "RESOLVED",
            Name = "Resolved Initial",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DispositionService(db);

        var updateRequest = new UpdateDispositionRequestDto
        {
            Name = "Issue Resolved Completely",
            Description = "First contact resolution",
            RequiresFollowUp = false,
            RequiresNotes = false,
            SortOrder = 1,
            IsActive = true
        };

        var updated = await service.UpdateAsync(id, updateRequest);
        Assert.NotNull(updated);
        Assert.Equal("Issue Resolved Completely", updated.Name);
        Assert.Equal("First contact resolution", updated.Description);

        var dbRecord = await db.CallDispositions.FindAsync(id);
        Assert.Equal("Issue Resolved Completely", dbRecord!.Name);
    }

    [Fact]
    public async Task ToggleStatusAsync_TogglesIsActive()
    {
        await using var db = TestDbContextFactory.Create();
        var id = Guid.NewGuid();
        db.CallDispositions.Add(new CallDisposition
        {
            Id = id,
            Code = "TOGGLE",
            Name = "Toggle Test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DispositionService(db);

        var toggled = await service.ToggleStatusAsync(id);
        Assert.False(toggled.IsActive);

        var toggledAgain = await service.ToggleStatusAsync(id);
        Assert.True(toggledAgain.IsActive);
    }
}
