using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly TriviaDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly PasswordHasher<Team> _passwordHasher;

    public AuthController(TriviaDbContext context, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _passwordHasher = new PasswordHasher<Team>();
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] TeamDto dto)
    {
        if (string.IsNullOrEmpty(dto.Name) || string.IsNullOrEmpty(dto.Password))
        {
            return BadRequest(new { error = "Team name and password are required." });
        }

        if (await _context.Teams.AnyAsync(t => t.Name == dto.Name))
        {
            return BadRequest(new { error = "Team name already taken." });
        }

        var team = new Team { Name = dto.Name };
        team.PasswordHash = _passwordHasher.HashPassword(team, dto.Password);

        _context.Teams.Add(team);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New team registered: {TeamName}", team.Name);
        return Ok(new { teamId = team.Id, message = "Team registered successfully." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] TeamDto dto)
    {
        if (string.IsNullOrEmpty(dto.Name) || string.IsNullOrEmpty(dto.Password))
        {
            return BadRequest(new { error = "Team name and password are required." });
        }

        var team = await _context.Teams.FirstOrDefaultAsync(t => t.Name == dto.Name);
        if (team == null || _passwordHasher.VerifyHashedPassword(team, team.PasswordHash, dto.Password) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed login attempt for team: {TeamName}", dto.Name);
            return Unauthorized(new { error = "Invalid team name or password." });
        }

        var token = GenerateJwtToken(team);
        _logger.LogInformation("Token generated for team: {TeamName}", team.Name);
        return Ok(new { token });
    }

    private string GenerateJwtToken(Team team)
    {
        var jwtKey = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, team.Name),
            new Claim("teamId", team.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        _logger.LogDebug("Generated Token: {Token}", tokenString);
        return tokenString;
    }
}

public class TeamDto
{
    public string Name { get; set; }
    public string Password { get; set; }
}