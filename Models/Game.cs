using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("games")]
 public class Game
 {
  [Column("id")]
  public int Id { get; set; }

  [Column("name")]
  [Required]
  [StringLength(100)]
  public string Name { get; set; }

  [Column("status")]
  [Required]
  public string Status { get; set; } = "Created";

  [Column("currentquestionid")]
  public int? CurrentQuestionId { get; set; }

  [Column("createdat")]
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  [Column("startedat")]
  public DateTime? StartedAt { get; set; }

  [Column("endedat")]
  public DateTime? EndedAt { get; set; }

  public int CurrentQuestionIndex { get; set; }

  // Navigation properties
  public virtual ICollection<GameTeam> GameTeams { get; set; }
  public virtual ICollection<GameQuestion> GameQuestions { get; set; }

  [ForeignKey("CurrentQuestionId")]
  public virtual Question CurrentQuestion { get; set; }

  public Game()
  {
   GameTeams = new List<GameTeam>();
   GameQuestions = new List<GameQuestion>();
   CreatedAt = DateTime.UtcNow;
   CurrentQuestionIndex = 0;
  }
 }
}