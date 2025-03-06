using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TriviaApp.Data;
using TriviaApp.Models;

namespace TriviaApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly TriviaDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IPasswordHasher<Team> _passwordHasher;

        public AuthController(
            TriviaDbContext context,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _passwordHasher = new PasswordHasher<Team>();
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Attempting to register team with name: {TeamName}", request.Name);

                var existingTeam = await _context.Teams.FirstOrDefaultAsync(t => t.Name == request.Name);
                if (existingTeam != null)
                {
                    _logger.LogWarning("Team name {TeamName} already exists", request.Name);
                    return BadRequest(new { error = "Team name already exists" });
                }

                var team = new Team
                {
                    Name = request.Name,
                    CreatedAt = DateTime.UtcNow
                };

                team.PasswordHash = _passwordHasher.HashPassword(team, request.Password);

                _context.Teams.Add(team);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Team registered successfully with ID: {TeamId}", team.Id);

                var token = GenerateJwtToken(team);
                return Ok(new { teamId = team.Id, name = team.Name, token });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering team");
                return StatusCode(500, new { error = "An error occurred while registering the team" });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                _logger.LogInformation("Attempting to log in team with name: {TeamName}", request.Name);

                var team = await _context.Teams.FirstOrDefaultAsync(t => t.Name == request.Name);
                if (team == null)
                {
                    _logger.LogWarning("Team not found with name: {TeamName}", request.Name);
                    return NotFound(new { error = "Team not found" });
                }

                var verificationResult = _passwordHasher.VerifyHashedPassword(team, team.PasswordHash, request.Password);
                if (verificationResult == PasswordVerificationResult.Failed)
                {
                    _logger.LogWarning("Invalid password for team: {TeamName}", request.Name);
                    return BadRequest(new { error = "Invalid password" });
                }

                _logger.LogInformation("Team logged in successfully with ID: {TeamId}", team.Id);

                var token = GenerateJwtToken(team);
                return Ok(new { teamId = team.Id, name = team.Name, token });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging in team");
                return StatusCode(500, new { error = "An error occurred while logging in" });
            }
        }

        private string GenerateJwtToken(Team team)
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
            {
                throw new InvalidOperationException("JWT key not found in configuration.");
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(jwtKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("teamId", team.Id.ToString()),
                    new Claim("name", team.Name)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }

    public class RegisterRequest
    {
        public required string Name { get; set; }
        public required string Password { get; set; }
    }

    public class LoginRequest
    {
        public required string Name { get; set; }
        public required string Password { get; set; }
    }
}