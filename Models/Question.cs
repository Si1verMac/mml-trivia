using System.Collections.Generic;
using TriviaApp.Models;


namespace TriviaApp.Models
{
 public class Question{
 public int Id { get; set; }
 public string Text { get; set; }
 public List<string> Options { get; set; } // Stored as JSON in DB
 public string CorrectAnswer { get; set; }
 public string Type { get; set; } // e.g., "Regular", "Wager", "Lightning"
 public int? DefaultPointValue { get; set; }
}
}