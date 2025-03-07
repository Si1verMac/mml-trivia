using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TriviaApp.Models;
using TriviaApp.Services;

namespace TriviaApp.Controllers
{
 [Route("api/[controller]")]
 [ApiController]
 public class GameController : ControllerBase
 {
  private readonly GameService _gameService;
  private readonly AnswerService _answerService;
  private readonly ILogger<GameController> _logger;

  public GameController(GameService gameService, AnswerService answerService, ILogger<GameController> logger)
  {
   _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
   _answerService = answerService ?? throw new ArgumentNullException(nameof(answerService));
   _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  [HttpPost("join")]
  public async Task<IActionResult> JoinGame([FromBody] JoinGameDto dto)
  {
   try
   {
    if (dto.TeamIds == null || !dto.TeamIds.Any())
    {
     _logger.LogWarning("No teams provided to join the game.");
     return BadRequest(new { error = "No teams provided to join the game." });
    }

    _logger.LogInformation("Teams joining game: {TeamIds}", string.Join(", ", dto.TeamIds));

    var game = await _gameService.GetOrCreateOpenGameAsync();

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



  [HttpPost("submit-answer")]
  public async Task<IActionResult> SubmitAnswer([FromBody] SubmitAnswerDto dto)
  {
   try
   {
    if (dto == null)
    {
     _logger.LogError("SubmitAnswerDto is null.");
     return BadRequest("Request body is missing or invalid.");
    }
    _logger.LogInformation("Received SubmitAnswerDto: {@Dto}", dto);
    await _answerService.SubmitAnswerAsync(dto.GameId, dto.TeamId, dto.QuestionId, dto.SelectedAnswer, dto.Wager);
    return Ok();
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error submitting answer.");
    return StatusCode(500, "Failed to submit answer.");
   }
  }

  // Endpoint to start the game(transition from lobby to active play).
  [HttpPost("start")]
  public async Task<IActionResult> StartGame([FromBody] StartGameDto dto)
  {
   try
   {
    if (dto.GameId <= 0)
    {
     return BadRequest(new { error = "Invalid gameId provided." });
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

  // Endpoint to end the game.
  [HttpPost("end")]
  public async Task<IActionResult> EndGame([FromBody] EndGameDto dto)
  {
   try
   {
    if (dto.GameId <= 0)
    {
     return BadRequest(new { error = "Invalid gameId provided." });
    }

    var game = await _gameService.EndGameAsync(dto.GameId);
    _logger.LogInformation("Game ended successfully with ID: {GameId}", game.Id);
    return Ok(new { gameId = game.Id });
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error ending game.");
    return StatusCode(500, new { error = "Failed to end game." });
   }
  }
 }





 public class JoinGameDto
 {
  public List<int> TeamIds { get; set; }
 }


 public class SubmitAnswerDto
 {
  public int GameId { get; set; }
  public int TeamId { get; set; }
  public int QuestionId { get; set; }
  public string SelectedAnswer { get; set; } // Can be null
  public int Wager { get; set; }
 }
}

public class StartGameDto
{
 [Required]
 public int GameId { get; set; }
}

public class EndGameDto
{
 [Required]
 public int GameId { get; set; }
}
