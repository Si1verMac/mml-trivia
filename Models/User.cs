using Microsoft.AspNetCore.Identity;
using TriviaApp.Models;


namespace TriviaApp.Models{
 public class User : IdentityUser
{
 public int TeamId { get; set; }
 public Team Team { get; set; }
}
}