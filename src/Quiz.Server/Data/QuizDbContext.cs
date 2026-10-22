using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Quiz.Server.Domain;

namespace Quiz.Server.Data;

/// <summary>
/// SQLite holds every fact (docs/TASK.md D4). The domain classes are mapped as they are; the
/// computed members (current question, events, ordered questions) are not stored.
/// </summary>
public class QuizDbContext(DbContextOptions<QuizDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<QuizSet> QuizSets => Set<QuizSet>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Answer> Answers => Set<Answer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // Every time is stored as UTC text, whatever offset the caller passed.
        configuration.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(user =>
        {
            user.HasKey(u => u.UserId);
            user.Property(u => u.Username).UseCollation("NOCASE");
            user.HasIndex(u => u.Username).IsUnique();
        });

        model.Entity<QuizSet>(set =>
        {
            set.Property(s => s.Id).HasColumnName("QuizSetId");
            set.HasKey(s => s.Id);
            set.Ignore(s => s.TimeLimit);
            set.Ignore(s => s.Ordered);
            set.HasMany(s => s.Questions).WithOne().HasForeignKey(q => q.QuizSetId);
        });

        model.Entity<Question>(question =>
        {
            question.Property(q => q.Id).HasColumnName("QuestionId");
            question.HasKey(q => q.Id);
            question.HasIndex(q => new { q.QuizSetId, q.Position }).IsUnique();
            question.Property(q => q.Direction).HasConversion<string>();
            // CorrectOptionId has no FK (Question <-> Option would be circular); the seed validator checks it.
            question.HasMany(q => q.Options).WithOne().HasForeignKey(o => o.QuestionId);
        });

        // Option ids ("a".."d") repeat across questions, so the key includes the question.
        model.Entity<Option>(option => option.HasKey(o => new { o.QuestionId, o.OptionId }));

        model.Entity<Session>(session =>
        {
            session.HasKey(s => s.QuizId);
            session.Property(s => s.State).HasConversion<string>();
            session.Property(s => s.Phase).HasConversion<string>();
            session.HasIndex(s => s.State); // recovery finds the running sessions
            session.Ignore(s => s.Events);
            session.Ignore(s => s.CurrentQuestion);
            // History is never deleted by accident: a used quiz set or user cannot be removed.
            session.HasOne(s => s.QuizSet).WithMany().HasForeignKey(s => s.QuizSetId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<User>().WithMany().HasForeignKey(s => s.OrganizerUserId).OnDelete(DeleteBehavior.Restrict);
            session.HasMany(s => s.Players).WithOne().HasForeignKey(p => p.QuizId);
            session.HasMany(s => s.Answers).WithOne().HasForeignKey(a => a.QuizId);
        });

        model.Entity<Player>(player =>
        {
            player.HasKey(p => new { p.QuizId, p.UserId });
            player.HasIndex(p => new { p.QuizId, p.Online });
            player.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Answer>(answer =>
        {
            answer.HasKey(a => new { a.QuizId, a.UserId, a.QuestionId }); // one answer per player per question
            answer.HasOne<Player>().WithMany().HasForeignKey(a => new { a.QuizId, a.UserId });
            // The option belongs to the question, so this FK also proves the question exists.
            answer.HasOne<Option>().WithMany().HasForeignKey(a => new { a.QuestionId, a.OptionId }).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v.ToUniversalTime());
}
