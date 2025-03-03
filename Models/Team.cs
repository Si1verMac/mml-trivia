using System.Collections.Generic;
using TriviaApp.Models;


namespace TriviaApp.Models{
 public class Team
{
 public int Id { get; set; } // Auto-generated TeamId
 public string Name { get; set; } // Unique team name
 public string PasswordHash { get; set; } // Hashed password for team login
 public List<Wager> Wagers { get; set; } = new List<Wager>();
 }
}