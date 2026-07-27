using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Repositories.Chat;

/// <inheritdoc cref="IPersonRepository"/>
public sealed class PersonRepository(SonarrDbContext db) : IPersonRepository
{
    /// <summary>Confidence a fact starts at, and how much a repeat mention adds.</summary>
    private const float InitialConfidence = 0.6f;

    private const float ReinforceStep = 0.15f;

    public Task<Person?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => db.People.FirstOrDefaultAsync(p => p.GuildId == guildId && p.UserId == userId, ct);

    /// <remarks>
    /// One transaction for all four tables (docs/10). A partial commit is the failure mode that
    /// produced the old bot's "furious for no reason" rows: registers moved, the event that
    /// explains the movement did not.
    /// </remarks>
    public async Task SaveTurnAsync(ChatTurnWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

        if (db.Entry(write.Person).State == EntityState.Detached)
        {
            bool exists = await db.People.AnyAsync(
                p => p.GuildId == write.Person.GuildId && p.UserId == write.Person.UserId, ct);
            if (exists)
            {
                db.People.Update(write.Person);
            }
            else
            {
                db.People.Add(write.Person);
            }
        }

        write.Person.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (FactWrite fact in write.Facts)
        {
            await UpsertFactAsync(write.Person.GuildId, write.Person.UserId, fact, ct);
        }

        foreach (Episode episode in write.Episodes)
        {
            episode.GuildId = write.Person.GuildId;
            episode.UserId = write.Person.UserId;
            db.Episodes.Add(episode);
        }

        foreach (RelationshipEvent relationshipEvent in write.Events)
        {
            relationshipEvent.GuildId = write.Person.GuildId;
            relationshipEvent.UserId = write.Person.UserId;
            db.RelationshipEvents.Add(relationshipEvent);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<Fact>> GetFactsAsync(
        long guildId,
        long userId,
        CancellationToken ct = default)
        => await db.Facts
            .AsNoTracking()
            .Where(f => f.GuildId == guildId && f.UserId == userId && f.Active)
            .OrderByDescending(f => f.LearnedAtTurn)
            .ToListAsync(ct);

    /// <remarks>
    /// Soft delete: the row stays so trajectory queries still see that she once knew this, but
    /// nothing reads an inactive fact. "Forget" is a user-facing promise about behaviour.
    /// </remarks>
    public async Task<bool> ForgetFactAsync(
        long guildId,
        long userId,
        string predicate,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicate);

        int updated = await db.Facts
            .Where(f => f.GuildId == guildId
                && f.UserId == userId
                && f.Predicate == predicate
                && f.Active)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(f => f.Active, false)
                    .SetProperty(f => f.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        return updated > 0;
    }

    public async Task<IReadOnlyList<RelationshipEvent>> GetRecentEventsAsync(
        long guildId,
        long userId,
        DateTimeOffset since,
        int limit,
        CancellationToken ct = default)
        => await db.RelationshipEvents
            .AsNoTracking()
            .Where(e => e.GuildId == guildId && e.UserId == userId && e.At >= since)
            .OrderByDescending(e => e.At)
            .Take(Math.Max(limit, 1))
            .ToListAsync(ct);

    /// <summary>
    /// Same predicate + same value reinforces confidence; a different value supersedes the old
    /// row rather than overwriting it, so "you said pizza, now you say sushi" stays visible.
    /// </summary>
    private async Task UpsertFactAsync(long guildId, long userId, FactWrite fact, CancellationToken ct)
    {
        Fact? existing = await db.Facts.FirstOrDefaultAsync(
            f => f.GuildId == guildId
                && f.UserId == userId
                && f.Predicate == fact.Predicate
                && f.Active,
            ct);

        if (existing is not null)
        {
            if (existing.Value == fact.Value)
            {
                existing.Confidence = Math.Min(1f, existing.Confidence + ReinforceStep);
                existing.LearnedAtTurn = fact.LearnedAtTurn;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                return;
            }

            existing.Active = false;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        db.Facts.Add(new Fact
        {
            GuildId = guildId,
            UserId = userId,
            Predicate = fact.Predicate,
            Value = fact.Value,
            Confidence = InitialConfidence,
            LearnedAtTurn = fact.LearnedAtTurn,
            LearnedAt = DateTimeOffset.UtcNow,
            Active = true,
        });
    }
}
