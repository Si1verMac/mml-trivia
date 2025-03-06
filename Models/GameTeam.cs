using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("gameteams")]
 public class GameTeam
 {
  [Column("gameid")]
  public int GameId { get; set; }

  [Column("teamid")]
  public int TeamId { get; set; }

  [Column("score")]
  public int Score { get; set; }

  [ForeignKey("GameId")]
  public virtual Game Game { get; set; }

  [ForeignKey("TeamId")]
  public virtual Team Team { get; set; }

  public GameTeam()
  {
   Score = 0;
  }
 }
}