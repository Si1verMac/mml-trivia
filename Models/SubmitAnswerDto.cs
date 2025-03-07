public class SubmitAnswerDto
{
 public int GameId { get; set; }
 public int TeamId { get; set; }
 public int QuestionId { get; set; }
 public string SelectedAnswer { get; set; }
 public int Wager { get; set; }
}