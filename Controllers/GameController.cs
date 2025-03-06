using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TriviaApp.Models;
using TriviaApp.Services;

namespace TriviaApp.Controllers
{
 [Authorize]
 [Route("api/[controller]")]
 [ApiController]
 public class GameController : ControllerBase
 {
  private readonly GameService _gameService;
  private readonly AnswerService _answerService;
  private readonly ILogger<GameController> _logger;

  public GameController(GameService gameService, AnswerService answerService, ILogger<GameController> logger)
  {
   _gameService = gameService;
   _answerService = answerService;
   _logger = logger;
  }

  // Endpoint for teams to join an open game.
  [HttpPost("join")]
  public async Task<IActionResult> JoinGame([FromBody] JoinGameDto dto)
  {
   try
   {
    if (dto.TeamIds == null || !dto.TeamIds.Any())
    {
     _logger.LogWarning("Attempted to join a game without teams.");
     return BadRequest(new { error = "No teams provided to join the game." });
    }

    _logger.LogInformation("Teams joining game: {TeamIds}", string.Join(", ", dto.TeamIds));

    // Get or create an open game (with status "Created")
    var game = await _gameService.GetOrCreateOpenGameAsync();

    // Add each team to the game if not already added
    foreach (var teamId in dto.TeamIds)
    {
     await _gameService.AddTeamToGameAsync(game.Id, teamId);
    }

    _logger.LogInformation("Teams joined game with ID: {GameId}", game.Id);
    return Ok(new { gameId = game.Id });
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error joining game.");
    return StatusCode(500, new { error = "Failed to join game." });
   }
  }

  // Endpoint to start a game (should be triggered once teams are joined)
  [HttpPost("start")]
  public async Task<IActionResult> StartGame([FromBody] StartGameDto dto)
  {
   try
   {
    if (dto.GameId <= 0)
    {
     return BadRequest(new { error = "Invalid gameId provided for starting the game." });
    }

    var game = await _gameService.StartGameAsync(dto.GameId);
    _logger.LogInformation("Game started successfully with ID: {GameId}", game.Id);
    return Ok(new { gameId = game.Id });
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error starting game.");
    return StatusCode(500, new { error = "Failed to start game." });
   }
  }

  [HttpPost("submit-answer")]
  public async Task<IActionResult> SubmitAnswer([FromBody] SubmitAnswerDto dto)
  {
   try
   {
    _logger.LogInformation("Submitting answer for game: {GameId}, team: {TeamId}, question: {QuestionId}",
        dto.gameId, dto.teamId, dto.questionId);

    await _answerService.SubmitAnswerAsync(
        dto.gameId,
        dto.teamId,
        dto.questionId,
        dto.selectedAnswer,
        dto.wager
    );

    _logger.LogInformation("Answer submitted successfully.");
    return Ok(new { message = "Answer submitted successfully" });
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error submitting answer.");
    return StatusCode(500, new { error = "Failed to submit answer." });
   }
  }

  public class JoinGameDto
  {
   public List<int> TeamIds { get; set; }
  }

  public class StartGameDto
  {
   public int GameId { get; set; }
  }

  public class SubmitAnswerDto
  {
   [Required]
   [JsonPropertyName("gameId")]
   public int gameId { get; set; }

   [Required]
   [JsonPropertyName("teamId")]
   public int teamId { get; set; }

   [Required]
   [JsonPropertyName("questionId")]
   public int questionId { get; set; }

   [Required]
   public string selectedAnswer { get; set; }

   [JsonPropertyName("wager")]
   public int? wager { get; set; }
  }
 }
}