using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MkAIHub.Api.Core;

namespace MkAIHub.Api.Data;

/// <summary>
/// SQLite connection helpers matching the Python engine setup: parent
/// directory creation, WAL journaling, a busy timeout, and the shared
/// datetime text format used by SQLAlchemy.
/// </summary>
public static class Database
{
    public const int SqliteBusyTimeoutMs = 5_000;

    public static string ConnectionStringFor(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = true,
        };
        builder["Foreign Keys"] = "True";
        return builder.ConnectionString;
    }

    /// <summary>
    /// Open a connection with the same per-connection pragmas the Python
    /// engine installs (busy timeout; foreign keys come from the connection
    /// string).
    /// </summary>
    public static SqliteConnection OpenSqlite(string databasePath)
    {
        var connection = new SqliteConnection(ConnectionStringFor(databasePath));
        connection.Open();
        ApplyBusyTimeout(connection);
        return connection;
    }

    public static void ApplyBusyTimeout(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {SqliteBusyTimeoutMs};";
        command.ExecuteNonQuery();
    }

    /// <summary>Create the parent directory and enable WAL journaling.</summary>
    public static void PrepareSqlite(string databasePath)
    {
        if (databasePath is ":" or ":memory:")
        {
            return;
        }
        var fullPath = Path.GetFullPath(databasePath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        if (!File.Exists(fullPath))
        {
            // The schema migrator owns database creation; only tune existing files.
            return;
        }
        using var connection = OpenSqlite(fullPath);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteScalar();
    }

    /// <summary>EF interceptor applying the busy-timeout pragma per connection.</summary>
    public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyBusyTimeout(connection);
            base.ConnectionOpened(connection, eventData);
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ApplyBusyTimeout(connection);
            return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        }
    }

    public static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    public static DateTime TruncateToMicroseconds(DateTime value)
        => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    /// <summary>
    /// Datetime text format identical to SQLAlchemy's SQLite storage so both
    /// backends can share one database file.
    /// </summary>
    public static string FormatSqliteTimestamp(DateTime value)
    {
        var utc = TruncateToMicroseconds(AsUtc(value));
        var fraction = utc.Ticks % TimeSpan.TicksPerSecond;
        var baseText = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (fraction == 0)
        {
            return baseText;
        }
        var micros = fraction / 10;
        return string.Create(CultureInfo.InvariantCulture, $"{baseText}.{micros:D6}");
    }

    public static DateTime ParseSqliteTimestamp(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static ValueConverter<DateTime, string> DateTimeTextConverter { get; } =
        new(v => FormatSqliteTimestamp(v), s => ParseSqliteTimestamp(s));

    public static ValueConverter<DateTime?, string> NullableDateTimeTextConverter { get; } =
        new(v => v.HasValue ? FormatSqliteTimestamp(v.Value) : null!, s => string.IsNullOrWhiteSpace(s) ? null : ParseSqliteTimestamp(s));

    /// <summary>
    /// Numeric columns are stored as REAL/INTEGER because SQLAlchemy binds
    /// Decimal values as floats on SQLite; reading goes through the shortest
    /// round-trip string so 85.55 reloads as exactly 85.55 before quantizing
    /// to the declared scale, mirroring SQLAlchemy's result processor.
    /// </summary>
    public static ValueConverter<decimal, double> Score2Converter { get; } =
        new(v => (double)v, s => Quantize(FromStorage(s), 2));

    public static ValueConverter<decimal?, double?> NullableScore2Converter { get; } =
        new(v => v.HasValue ? (double)v.Value : null, s => s.HasValue ? Quantize(FromStorage(s.Value), 2) : null);

    public static ValueConverter<decimal, double> Score4Converter { get; } =
        new(v => (double)v, s => Quantize(FromStorage(s), 4));

    private static decimal FromStorage(double value)
        => decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static decimal Quantize(decimal value, int scale)
        => decimal.Round(value, scale, MidpointRounding.AwayFromZero);
}

/// <summary>
/// EF Core model mirroring the SQLAlchemy models and Alembic-managed schema.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<ArtifactFile> ArtifactFiles => Set<ArtifactFile>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskParticipant> TaskParticipants => Set<TaskParticipant>();

    public DbSet<TaskSubmission> TaskSubmissions => Set<TaskSubmission>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<Competition> Competitions => Set<Competition>();

    public DbSet<CompetitionRegistration> CompetitionRegistrations => Set<CompetitionRegistration>();

    public DbSet<CompetitionReview> CompetitionReviews => Set<CompetitionReview>();

    public DbSet<CompetitionResult> CompetitionResults => Set<CompetitionResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasCheckConstraint("ck_users_role", "role IN ('EMPLOYEE', 'SYSTEM_ADMIN')");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.Username).IsUnique().HasDatabaseName("uq_users_username");
            entity.HasIndex(user => new { user.Role, user.IsActive }).HasDatabaseName("ix_users_role_active");
            entity.Property(user => user.Username).HasMaxLength(64).IsRequired();
            entity.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(session => session.Id);
            entity.HasOne(session => session.User)
                .WithMany(user => user.Sessions)
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(session => session.TokenHash).IsUnique().HasDatabaseName("uq_user_sessions_token_hash");
            entity.HasIndex(session => session.UserId).HasDatabaseName("ix_user_sessions_user_id");
            entity.HasIndex(session => new { session.UserId, session.RevokedAt, session.ExpiresAt })
                .HasDatabaseName("ix_user_sessions_user_active");
            entity.Property(session => session.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(file => file.Id);
            entity.HasOne(file => file.Uploader)
                .WithMany()
                .HasForeignKey(file => file.UploaderId);
            entity.HasIndex(file => file.RelativePath).IsUnique().HasDatabaseName("uq_files_relative_path");
            entity.HasIndex(file => new { file.UploaderId, file.CreatedAt }).HasDatabaseName("ix_files_uploader_created");
            entity.Property(file => file.OriginalName).HasMaxLength(255).IsRequired();
            entity.Property(file => file.StoredName).HasMaxLength(100).IsRequired();
            entity.Property(file => file.RelativePath).HasMaxLength(500).IsRequired();
            entity.Property(file => file.Extension).HasMaxLength(32).IsRequired();
            entity.Property(file => file.MimeType).HasMaxLength(150).IsRequired();
            entity.Property(file => file.Sha256).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Artifact>(entity =>
        {
            entity.ToTable("artifacts");
            entity.HasCheckConstraint("ck_artifacts_status", "status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')");
            entity.HasKey(artifact => artifact.Id);
            entity.HasOne(artifact => artifact.Author)
                .WithMany()
                .HasForeignKey(artifact => artifact.AuthorId);
            entity.HasIndex(artifact => new { artifact.Status, artifact.PublishedAt })
                .HasDatabaseName("ix_artifacts_status_published");
            entity.HasIndex(artifact => new { artifact.AuthorId, artifact.UpdatedAt })
                .HasDatabaseName("ix_artifacts_author_updated");
            entity.Property(artifact => artifact.Title).HasMaxLength(200).IsRequired();
            entity.Property(artifact => artifact.Summary).HasMaxLength(500).IsRequired();
            entity.Property(artifact => artifact.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ArtifactFile>(entity =>
        {
            entity.ToTable("artifact_files");
            entity.HasKey(link => link.Id);
            entity.HasOne(link => link.Artifact)
                .WithMany(artifact => artifact.FileLinks)
                .HasForeignKey(link => link.ArtifactId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.File)
                .WithMany(file => file.ArtifactLinks)
                .HasForeignKey(link => link.FileId);
            entity.HasIndex(link => new { link.ArtifactId, link.FileId })
                .IsUnique()
                .HasDatabaseName("uq_artifact_files_artifact_file");
            entity.HasIndex(link => link.FileId).HasDatabaseName("ix_artifact_files_file_id");
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.ToTable("comments");
            entity.HasCheckConstraint("ck_comments_status", "status IN ('VISIBLE', 'HIDDEN')");
            entity.HasCheckConstraint("ck_comments_target", "(artifact_id IS NULL) <> (issue_id IS NULL)");
            entity.HasKey(comment => comment.Id);
            entity.HasOne(comment => comment.Artifact)
                .WithMany(artifact => artifact.Comments)
                .HasForeignKey(comment => comment.ArtifactId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(comment => comment.Issue)
                .WithMany(issue => issue.Comments)
                .HasForeignKey(comment => comment.IssueId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(comment => comment.Author)
                .WithMany()
                .HasForeignKey(comment => comment.AuthorId);
            entity.HasIndex(comment => new { comment.ArtifactId, comment.CreatedAt })
                .HasDatabaseName("ix_comments_artifact_created");
            entity.HasIndex(comment => new { comment.IssueId, comment.CreatedAt })
                .HasDatabaseName("ix_comments_issue_created");
            entity.Property(comment => comment.Status).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasCheckConstraint("ck_tasks_status", "status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')");
            entity.HasCheckConstraint(
                "ck_tasks_competition_score",
                "competition_id IS NULL OR (competition_max_score > 0 AND competition_weight > 0)");
            entity.HasCheckConstraint(
                "ck_tasks_competition_fields",
                "competition_id IS NOT NULL "
                + "OR (competition_required IS NULL AND competition_sort_order IS NULL "
                + "AND competition_max_score IS NULL AND competition_weight IS NULL)");
            entity.HasKey(task => task.Id);
            entity.HasOne(task => task.Creator)
                .WithMany()
                .HasForeignKey(task => task.CreatorId);
            entity.HasOne(task => task.Competition)
                .WithMany()
                .HasForeignKey(task => task.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(task => new { task.Status, task.UpdatedAt })
                .HasDatabaseName("ix_tasks_status_updated");
            entity.HasIndex(task => new { task.CreatorId, task.UpdatedAt })
                .HasDatabaseName("ix_tasks_creator_updated");
            entity.HasIndex(task => new { task.CompetitionId, task.CompetitionSortOrder })
                .HasDatabaseName("ix_tasks_competition");
            entity.Property(task => task.Title).HasMaxLength(200).IsRequired();
            entity.Property(task => task.Status).HasMaxLength(32).IsRequired();
            entity.Property(task => task.CompetitionMaxScore)
                .HasConversion(global::MkAIHub.Api.Data.Database.NullableScore2Converter);
            entity.Property(task => task.CompetitionWeight)
                .HasConversion(global::MkAIHub.Api.Data.Database.NullableScore2Converter);
        });

        modelBuilder.Entity<Issue>(entity =>
        {
            entity.ToTable("issues");
            entity.HasCheckConstraint("ck_issues_status", "status IN ('OPEN', 'CLOSED')");
            entity.HasKey(issue => issue.Id);
            entity.HasOne(issue => issue.Author)
                .WithMany()
                .HasForeignKey(issue => issue.AuthorId);
            entity.HasIndex(issue => new { issue.Status, issue.UpdatedAt })
                .HasDatabaseName("ix_issues_status_updated");
            entity.HasIndex(issue => new { issue.AuthorId, issue.UpdatedAt })
                .HasDatabaseName("ix_issues_author_updated");
            entity.Property(issue => issue.Title).HasMaxLength(200).IsRequired();
            entity.Property(issue => issue.Status).HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("competitions");
            entity.HasCheckConstraint("ck_competitions_window", "start_at < end_at");
            entity.HasKey(competition => competition.Id);
            entity.HasOne(competition => competition.Creator)
                .WithMany()
                .HasForeignKey(competition => competition.CreatedBy);
            entity.HasIndex(competition => new { competition.StartAt, competition.EndAt })
                .HasDatabaseName("ix_competitions_start_end");
            entity.Property(competition => competition.Title).HasMaxLength(200).IsRequired();
            entity.Property(competition => competition.Summary).HasMaxLength(500).IsRequired();
            entity.Property(competition => competition.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<TaskParticipant>(entity =>
        {
            entity.ToTable("task_participants");
            entity.HasCheckConstraint("ck_task_participants_status", "status IN ('ACTIVE', 'LEFT')");
            entity.HasKey(participant => participant.Id);
            entity.HasOne(participant => participant.Task)
                .WithMany()
                .HasForeignKey(participant => participant.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(participant => participant.User)
                .WithMany()
                .HasForeignKey(participant => participant.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(participant => new { participant.TaskId, participant.UserId })
                .IsUnique()
                .HasDatabaseName("uq_task_participants_task_user");
            entity.HasIndex(participant => new { participant.UserId, participant.Status })
                .HasDatabaseName("ix_task_participants_user");
            entity.Property(participant => participant.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<TaskSubmission>(entity =>
        {
            entity.ToTable("task_submissions");
            entity.HasCheckConstraint(
                "ck_task_submissions_status",
                "status IN ('SUBMITTED', 'REVISION_REQUIRED', 'ACCEPTED', 'REJECTED')");
            entity.HasCheckConstraint("ck_task_submissions_round", "round_no >= 1");
            entity.HasKey(submission => submission.Id);
            entity.HasOne(submission => submission.Task)
                .WithMany()
                .HasForeignKey(submission => submission.TaskId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(submission => submission.Participant)
                .WithMany()
                .HasForeignKey(submission => submission.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(submission => submission.Artifact)
                .WithMany(artifact => artifact.TaskSubmissions)
                .HasForeignKey(submission => submission.ArtifactId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(submission => submission.Decider)
                .WithMany()
                .HasForeignKey(submission => submission.DeciderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(submission => new { submission.ParticipantId, submission.RoundNo })
                .IsUnique()
                .HasDatabaseName("uq_task_submissions_round");
            entity.HasIndex(submission => new { submission.TaskId, submission.Status })
                .HasDatabaseName("ix_task_submissions_task_status");
            entity.HasIndex(submission => submission.ParticipantId)
                .HasDatabaseName("ix_task_submissions_participant");
            entity.HasIndex(submission => submission.ArtifactId)
                .HasDatabaseName("ix_task_submissions_artifact");
            entity.Property(submission => submission.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CompetitionRegistration>(entity =>
        {
            entity.ToTable("competition_registrations");
            entity.HasCheckConstraint(
                "ck_competition_registrations_status",
                "status IN ('REGISTERED', 'CANCELLED')");
            entity.HasKey(registration => registration.Id);
            entity.HasOne(registration => registration.Competition)
                .WithMany()
                .HasForeignKey(registration => registration.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(registration => registration.User)
                .WithMany()
                .HasForeignKey(registration => registration.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(registration => new { registration.CompetitionId, registration.UserId })
                .IsUnique()
                .HasDatabaseName("uq_competition_registrations_competition_user");
            entity.HasIndex(registration => registration.UserId)
                .HasDatabaseName("ix_competition_registrations_user");
            entity.Property(registration => registration.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CompetitionReview>(entity =>
        {
            entity.ToTable("competition_reviews");
            entity.HasKey(review => review.Id);
            entity.HasOne(review => review.Submission)
                .WithOne(submission => submission.CompetitionReview)
                .HasForeignKey<CompetitionReview>(review => review.TaskSubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(review => review.Reviewer)
                .WithMany()
                .HasForeignKey(review => review.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(review => review.TaskSubmissionId)
                .IsUnique()
                .HasDatabaseName("uq_competition_reviews_submission");
            entity.Property(review => review.RawScore)
                .HasConversion(global::MkAIHub.Api.Data.Database.Score2Converter);
        });

        modelBuilder.Entity<CompetitionResult>(entity =>
        {
            entity.ToTable("competition_results");
            entity.HasCheckConstraint("ck_competition_results_rank", "rank >= 1");
            entity.HasKey(result => result.Id);
            entity.HasOne(result => result.Competition)
                .WithMany()
                .HasForeignKey(result => result.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(result => result.Registration)
                .WithMany()
                .HasForeignKey(result => result.RegistrationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(result => result.Publisher)
                .WithMany()
                .HasForeignKey(result => result.PublishedBy)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(result => new { result.CompetitionId, result.RegistrationId })
                .IsUnique()
                .HasDatabaseName("uq_competition_results_competition_registration");
            entity.HasIndex(result => new { result.CompetitionId, result.Rank })
                .HasDatabaseName("ix_competition_results_rank");
            entity.Property(result => result.TotalScore)
                .HasConversion(global::MkAIHub.Api.Data.Database.Score4Converter);
            entity.Property(result => result.Award).HasMaxLength(200);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(SnakeCaseNamingPolicy.ToSnakeCase(property.Name));
            }
        }

        AttachDateTimeConversions(modelBuilder);
    }

    private static void AttachDateTimeConversions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(global::MkAIHub.Api.Data.Database.DateTimeTextConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(global::MkAIHub.Api.Data.Database.NullableDateTimeTextConverter);
                }
            }
        }
    }
}
