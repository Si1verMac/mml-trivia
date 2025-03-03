using TriviaApp.Models;


namespace TriviaApp.Models{

 public class GameTeam
{
 public int GameId { get; set; }
 public Game Game { get; set; }
 public int TeamId { get; set; }
 public Team Team { get; set; }
 public List<Wager> Wagers { get; set; } = new List<Wager>();
 }
}