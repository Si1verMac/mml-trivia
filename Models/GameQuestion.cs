using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("gamequestions")]
 public class GameQuestion
 {
  [Column("gameid")]
  public int GameId { get; set; }

  [Column("questionid")]
  public int QuestionId { get; set; }

  [Column("orderindex")]
  public int OrderIndex { get; set; }

  [Column("isanswered")]
  public bool IsAnswered { get; set; }

  [ForeignKey("GameId")]
  public virtual Game Game { get; set; }

  [ForeignKey("QuestionId")]
  public virtual Question Question { get; set; }

  public GameQuestion()
  {
   IsAnswered = false;
   OrderIndex = 0;
  }
 }
}