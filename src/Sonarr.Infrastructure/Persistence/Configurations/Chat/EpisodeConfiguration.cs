using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Configurations.Chat;

internal sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    /// <summary>Embedding dimension of the ONNX sentence model (docs/04-database.md).</summary>
    public const int EmbeddingDimensions = 384;

    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("episode", "chat");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Quote).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.SentimentTag).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
        builder.HasAuditTimestamps();

        // Retention service and recall both scan per user, newest first.
        builder.HasIndex(e => new { e.GuildId, e.UserId, e.HappenedAt });

        // ivfflat over cosine distance, per docs/04-database.md. Lists=100 is the
        // pgvector default starting point; tune only if recall latency shows up.
        builder.HasIndex(e => e.Embedding)
            .HasDatabaseName("ix_episode_embedding")
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("lists", 100);
    }
}
