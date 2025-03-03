using System;
using TriviaApp.Models;


namespace TriviaApp.Models
{
 public class Answer
 {
  public int Id { get; set; }
  public int GameId { get; set; }
  public int TeamId { get; set; }
  public int QuestionId { get; set; }
  public string? SelectedAnswer { get; set; }
  public int? Wager { get; set; }
  public bool? IsCorrect { get; set; }
  public DateTime SubmittedAt { get; set; }
 }
}