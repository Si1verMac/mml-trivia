using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TriviaApp.Models
{
 [Table("questions")]
 public class Question
 {
  [Column("id")]
  public int Id { get; set; }

  [Column("text")]
  [Required]
  public string[] Text { get; set; } = Array.Empty<string>();

  [Column("type")]
  [Required]
  public string Type { get; set; }

  [Column("options")]
  [Required]
  public string[] Options { get; set; } = Array.Empty<string>();

  [Column("correctanswer")]
  [Required]
  public string[] CorrectAnswer { get; set; } = Array.Empty<string>();

  [Column("points")]
  public int? Points { get; set; }  // Now nullable

  [Column("createdat")]
  public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

  public virtual ICollection<GameQuestion> GameQuestions { get; set; }

  public Question()
  {
   GameQuestions = new List<GameQuestion>();
  }

  // Helper methods to ensure backward compatibility
  public string GetFirstText()
  {
   return Text != null && Text.Length > 0 ? Text[0] : string.Empty;
  }

  public string GetFirstCorrectAnswer()
  {
   return CorrectAnswer != null && CorrectAnswer.Length > 0 ? CorrectAnswer[0] : string.Empty;
  }
 }
}