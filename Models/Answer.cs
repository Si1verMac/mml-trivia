using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("answers")]
 public class Answer
 {
  [Column("id")]
  public int Id { get; set; }

  [Column("gameid")]
  public int GameId { get; set; }

  [Column("teamid")]
  public int TeamId { get; set; }

  [Column("questionid")]
  public int QuestionId { get; set; }

  [Column("selectedanswer")]
  public string? SelectedAnswer { get; set; }

  [Column("wager")]
  public int? Wager { get; set; }

  [Column("iscorrect")]
  public bool? IsCorrect { get; set; }

  [Column("submittedat")]
  public DateTime SubmittedAt { get; set; }

  [ForeignKey("GameId")]
  public virtual Game? Game { get; set; }

  [ForeignKey("TeamId")]
  public virtual Team? Team { get; set; }

  [ForeignKey("QuestionId")]
  public virtual Question? Question { get; set; }
 }
}