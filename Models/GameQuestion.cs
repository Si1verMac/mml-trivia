using TriviaApp.Models;


namespace TriviaApp.Models{

 public class GameQuestion
{
 public int GameId { get; set; }
 public Game Game { get; set; }
 public int QuestionId { get; set; }
 public Question Question { get; set; }
 public int Order { get; set; }
}
}