using System.Collections.Generic;

public class Game
{
 public int Id { get; set; }
 public string Status { get; set; } // e.g., "Active", "Completed"
 public List<GameTeam> GameTeams { get; set; }
 public List<GameQuestion> GameQuestions { get; set; }
}