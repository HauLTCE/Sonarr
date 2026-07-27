using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Web;

namespace Sonarr.Infrastructure.Persistence.Configurations.Web;

internal sealed class LoginTokenConfiguration : IEntityTypeConfiguration<LoginToken>
{
    public void Configure(EntityTypeBuilder<LoginToken> builder)
    {
        builder.ToTable("login_token", "web");

        // PK is the SHA-256 hex of the token; the raw value is never stored.
        builder.HasKey(t => t.TokenHash);
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.Property(t => t.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(t => t.RequestedIp).HasColumnType("inet");
        builder.HasAuditTimestamps();

        // Sweeper deletes expired rows.
        builder.HasIndex(t => t.ExpiresAt);
    }
}

internal sealed class WebSessionConfiguration : IEntityTypeConfiguration<WebSession>
{
    public void Configure(EntityTypeBuilder<WebSession> builder)
    {
        builder.ToTable("session", "web");
        builder.HasKey(s => s.SessionId);
        builder.Property(s => s.SessionId).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(512);
        builder.HasAuditTimestamps();

        // "Log out everywhere" and the sweeper.
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.ExpiresAt);
    }
}

internal sealed class WebAuditConfiguration : IEntityTypeConfiguration<WebAudit>
{
    public void Configure(EntityTypeBuilder<WebAudit> builder)
    {
        builder.ToTable("audit", "web");
        builder.HasKey(a => a.AuditId);
        builder.Property(a => a.AuditId).UseIdentityAlwaysColumn();
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Target).HasMaxLength(256);
        builder.Property(a => a.SessionId).HasMaxLength(64);
        builder.Property(a => a.Detail).IsJsonb();
        builder.Property(a => a.At).HasDefaultValueSql("now()");
        builder.HasAuditTimestamps();

        builder.HasIndex(a => a.At);

        // No FK to web.session: audit rows must outlive session cleanup.
    }
}
