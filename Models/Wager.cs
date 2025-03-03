using TriviaApp.Models;


namespace TriviaApp.Models
{

  public class Wager
  {
    public int Id { get; set; }
    public int QuestionNumber { get; set; }
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int Value { get; set; }
    public DateTime SubmittedAt { get; set; }
  }
}