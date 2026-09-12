using CallCenter.Application.Dispositions;
using CallCenter.Application.Dispositions.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Dispositions;

public sealed class DispositionService(CallCenterDbContext dbContext) : IDispositionService
{
    public async Task<IReadOnlyList<CallDispositionDto>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.CallDispositions.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var dispositions = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return dispositions.Select(ToDto).ToList();
    }

    public async Task<CallDispositionDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var disposition = await dbContext.CallDispositions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return disposition is null ? null : ToDto(disposition);
    }

    public async Task<CallDispositionDto> CreateAsync(
        CreateDispositionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        var exists = await dbContext.CallDispositions
            .AnyAsync(x => x.Code == normalizedCode, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException(
                $"A disposition with code '{normalizedCode}' already exists.");
        }

        var disposition = new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            RequiresFollowUp = request.RequiresFollowUp,
            RequiresNotes = request.RequiresNotes,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.CallDispositions.Add(disposition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(disposition);
    }

    public async Task<CallDispositionDto> UpdateAsync(
        Guid id,
        UpdateDispositionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var disposition = await dbContext.CallDispositions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Call disposition '{id}' was not found.");

        disposition.Name = request.Name.Trim();
        disposition.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        disposition.RequiresFollowUp = request.RequiresFollowUp;
        disposition.RequiresNotes = request.RequiresNotes;
        disposition.SortOrder = request.SortOrder;
        disposition.IsActive = request.IsActive;
        disposition.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(disposition);
    }

    public async Task<CallDispositionDto> ToggleStatusAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var disposition = await dbContext.CallDispositions
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Call disposition '{id}' was not found.");

        disposition.IsActive = !disposition.IsActive;
        disposition.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(disposition);
    }

    private static CallDispositionDto ToDto(CallDisposition d) => new()
    {
        Id = d.Id,
        Code = d.Code,
        Name = d.Name,
        Description = d.Description,
        RequiresFollowUp = d.RequiresFollowUp,
        RequiresNotes = d.RequiresNotes,
        SortOrder = d.SortOrder,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };
}
