using System.Collections.Generic;

public class Team
{
 public int Id { get; set; } // Auto-generated TeamId
 public string Name { get; set; } // Unique team name
 public string PasswordHash { get; set; } // Hashed password for team login
}