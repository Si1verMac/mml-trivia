using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("questions")]
 public class Question
 {
  [Column("id")]
  public int Id { get; set; }

  [Column("text")]
  [Required]
  public string Text { get; set; }

  [Column("type")]
  [Required]
  public string Type { get; set; }

  [Column("options")]
  [Required]
  public string[] Options { get; set; }

  [Column("correctanswer")]
  [Required]
  public string CorrectAnswer { get; set; }

  [Column("points")]
  public int? Points { get; set; }  // Now nullable

  [Column("createdat")]
  public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

  public virtual ICollection<GameQuestion> GameQuestions { get; set; }

  public Question()
  {
   GameQuestions = new List<GameQuestion>();
   Options = Array.Empty<string>();
  }
 }
}