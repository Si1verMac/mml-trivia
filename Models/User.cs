using Microsoft.AspNetCore.Identity;

public class User : IdentityUser
{
 public int TeamId { get; set; }
 public Team Team { get; set; }
}