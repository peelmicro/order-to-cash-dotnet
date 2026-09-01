using Microsoft.EntityFrameworkCore;

namespace OrderToCash.Seed.Infrastructure.Persistence;

/// <summary>
/// Upsert-by-id — the mechanism that makes "running the seed twice is a
/// no-op" (feature_list.json #12) fall out of deterministic ids rather than
/// needing its own dedup table: every row this seed writes carries a
/// deterministic (<c>DeterministicId.Of</c>-derived) primary key, so a
/// second run finds the same row and updates it in place instead of
/// inserting a duplicate.
/// Mirrors #7's own <c>onDuplicateKeyUpdate</c> writers, adapted to EF
/// Core's change tracker rather than a raw MySQL upsert clause.
/// </summary>
public static class EfUpsert
{
    public static async Task<TEntity> UpsertAsync<TEntity>(
        this DbContext db,
        Guid id,
        Func<TEntity> create,
        Action<TEntity> apply,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var existing = await db.Set<TEntity>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var entity = create();
            apply(entity);
            db.Set<TEntity>().Add(entity);
            return entity;
        }

        apply(existing);
        return existing;
    }
}
