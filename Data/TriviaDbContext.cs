using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TriviaApp.Models;

namespace TriviaApp.Data
{
    public class TriviaDbContext : DbContext
    {
        private readonly IConfiguration _configuration;

        public TriviaDbContext(DbContextOptions<TriviaDbContext> options, IConfiguration configuration)
            : base(options)
        {
            _configuration = configuration;
        }

        public DbSet<Team> Teams { get; set; }
        public DbSet<Game> Games { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<Answer> Answers { get; set; }
        public DbSet<GameTeam> GameTeams { get; set; }
        public DbSet<GameQuestion> GameQuestions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection") ??
                    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
                optionsBuilder.UseNpgsql(connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Convert all table names and column names to lowercase.
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(entity.GetTableName().ToLower());

                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(property.GetColumnName().ToLower());
                }
            }

            // Configure unique index on Team name.
            modelBuilder.Entity<Team>()
                .HasIndex(t => t.Name)
                .IsUnique();

            // Configure composite key for GameTeam.
            modelBuilder.Entity<GameTeam>()
                .HasKey(gt => new { gt.GameId, gt.TeamId });

            // Configure relationships for GameTeam.
            modelBuilder.Entity<GameTeam>()
                .HasOne(gt => gt.Game)
                .WithMany(g => g.GameTeams)
                .HasForeignKey(gt => gt.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameTeam>()
                .HasOne(gt => gt.Team)
                .WithMany(t => t.GameTeams)
                .HasForeignKey(gt => gt.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure composite key for GameQuestion.
            modelBuilder.Entity<GameQuestion>()
                .HasKey(gq => new { gq.GameId, gq.QuestionId });

            // Configure the one-to-many relationship between Game and GameQuestion.
            modelBuilder.Entity<Game>()
                .HasMany(g => g.GameQuestions)
                .WithOne(gq => gq.Game)
                .HasForeignKey(gq => gq.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure the one-to-many relationship between Question and GameQuestion.
            modelBuilder.Entity<Question>()
                .HasMany(q => q.GameQuestions)
                .WithOne(gq => gq.Question)
                .HasForeignKey(gq => gq.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure the optional relationship for the current question of a game.
            modelBuilder.Entity<Game>()
                .HasOne(g => g.CurrentQuestion)
                .WithMany()
                .HasForeignKey(g => g.CurrentQuestionId)
                .IsRequired(false);

            // Configure the Question entity to handle JSON conversions
            modelBuilder.Entity<Question>(entity =>
            {
                // Value comparer for string arrays
                var stringArrayComparer = new ValueComparer<string[]>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, v != null ? v.GetHashCode() : 0)),
                    c => c.ToArray()
                );

                // Set up JSON conversion for Text field
                entity.Property(e => e.Text)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                        v => JsonDeserialize<string[]>(v) ?? Array.Empty<string>()
                    )
                    .Metadata.SetValueComparer(stringArrayComparer);

                // Set up JSON conversion for Options field
                entity.Property(e => e.Options)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                        v => JsonDeserialize<string[]>(v) ?? Array.Empty<string>()
                    )
                    .Metadata.SetValueComparer(stringArrayComparer);

                // Set up JSON conversion for CorrectAnswer field
                entity.Property(e => e.CorrectAnswer)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                        v => JsonDeserialize<string[]>(v) ?? Array.Empty<string>()
                    )
                    .Metadata.SetValueComparer(stringArrayComparer);
            });
        }

        // Helper method to safely deserialize JSON
        private static T JsonDeserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                return default;

            try
            {
                // If it's a string that isn't JSON formatted, wrap it in an array
                if (!json.StartsWith("[") && typeof(T) == typeof(string[]))
                {
                    return (T)(object)new string[] { json };
                }

                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                // If deserialization fails and we want string[], return the raw string as a single-element array
                if (typeof(T) == typeof(string[]))
                {
                    return (T)(object)new string[] { json };
                }
                return default;
            }
        }
    }
}