using Microsoft.EntityFrameworkCore;

public class TriviaDbContext : DbContext
{
 public DbSet<Team> Teams { get; set; }
 public DbSet<Game> Games { get; set; }
 public DbSet<Question> Questions { get; set; }
 public DbSet<Answer> Answers { get; set; }
 public DbSet<GameTeam> GameTeams { get; set; }
 public DbSet<GameQuestion> GameQuestions { get; set; }

 public TriviaDbContext(DbContextOptions<TriviaDbContext> options) : base(options) { }

 protected override void OnModelCreating(ModelBuilder modelBuilder)
 {
  // Ensure unique team names
  modelBuilder.Entity<Team>()
      .HasIndex(t => t.Name)
      .IsUnique();

  modelBuilder.Entity<GameTeam>().HasKey(gt => new { gt.GameId, gt.TeamId });
  modelBuilder.Entity<GameQuestion>().HasKey(gq => new { gq.GameId, gq.QuestionId });
 }
}