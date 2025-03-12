using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TriviaApp.Models
{
 [Table("teams")]
 public class Team
 {
  [Column("id")]
  public int Id { get; set; }

  [Column("name")]
  [Required]
  [StringLength(100)]
  public string Name { get; set; }

  [Column("password")]
  [Required]
  public string PasswordHash { get; set; }

  [Column("createdat")]
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  [Column("isoperator")]
  public bool IsOperator { get; set; } // Added for operator role

  public virtual ICollection<GameTeam> GameTeams { get; set; }

  public Team()
  {
   GameTeams = new List<GameTeam>();
   CreatedAt = DateTime.UtcNow;
  }
 }
}