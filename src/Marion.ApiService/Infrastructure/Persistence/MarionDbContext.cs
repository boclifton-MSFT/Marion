using Microsoft.EntityFrameworkCore;

namespace Marion.ApiService.Infrastructure.Persistence;

public sealed class MarionDbContext(DbContextOptions<MarionDbContext> options)
    : DbContext(options)
{
    public DbSet<AuthTransaction> AuthTransactions => Set<AuthTransaction>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuthTransaction>(entity =>
        {
            entity.ToTable("MarionAuthTransactions");
            entity.HasKey(transaction => transaction.TransactionId);
            entity.Property(transaction => transaction.TransactionId).HasMaxLength(128);
            entity.Property(transaction => transaction.ExpiresAt).HasColumnType("datetime2(3)");
            entity.HasIndex(transaction => transaction.ExpiresAt);
        });

        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.ToTable("MarionAuthSessions");
            entity.HasKey(session => session.SessionId);
            entity.Property(session => session.SessionId).HasMaxLength(128);
            entity.Property(session => session.UserId).HasMaxLength(128);
            entity.Property(session => session.IssuedAt).HasColumnType("datetime2(3)");
            entity.Property(session => session.LastActiveAt).HasColumnType("datetime2(3)");
        });

        modelBuilder.Entity<ExternalIdentity>(entity =>
        {
            entity.ToTable("MarionExternalIdentities");

            // Clustered on UserId so the (Issuer, Subject) key stays inside the 1700-byte
            // non-clustered index limit; a clustered key of that width exceeds SQL Server's 900.
            entity.HasKey(identity => new { identity.Issuer, identity.Subject })
                .IsClustered(false);
            entity.Property(identity => identity.Issuer).HasMaxLength(255);
            entity.Property(identity => identity.Subject).HasMaxLength(255);
            entity.Property(identity => identity.UserId).HasMaxLength(128);
            entity.Property(identity => identity.CreatedAt).HasColumnType("datetime2(3)");
            entity.HasIndex(identity => identity.UserId).IsUnique().IsClustered();
        });
    }
}
