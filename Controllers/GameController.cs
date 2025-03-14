using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriviaApp.Data;
using TriviaApp.Hubs;
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
        private readonly TriviaDbContext _dbContext;
        private readonly IHubContext<TriviaHub> _hubContext;

        private readonly ILogger<GameController> _logger;

        public GameController(GameService gameService, AnswerService answerService, TriviaDbContext dbContext, IHubContext<TriviaHub> hubContext, ILogger<GameController> logger)
        {
            _gameService = gameService ?? throw new ArgumentNullException(nameof(gameService));
            _answerService = answerService ?? throw new ArgumentNullException(nameof(answerService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _hubContext = hubContext;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("join")]
        [Authorize]
        public async Task<IActionResult> JoinGame([FromBody] JoinGameDto dto)
        {
            try
            {
                if (dto.TeamIds == null || !dto.TeamIds.Any())
                {
                    _logger.LogWarning("No teams provided to join the game.");
                    return BadRequest(new { error = "No teams provided to join the game." });
                }

                Game game;
                if (dto.GameId.HasValue && dto.GameId > 0)
                {
                    game = await _dbContext.Games.FirstOrDefaultAsync(g => g.Id == dto.GameId.Value && g.Status == "InProgress");
                    if (game == null)
                    {
                        _logger.LogWarning("Attempted to join invalid or inactive game ID: {GameId}", dto.GameId);
                        return BadRequest(new { error = "Invalid or inactive game ID" });
                    }
                }
                else
                {
                    game = await _gameService.GetOrCreateOpenGameAsync();
                }

                foreach (var teamId in dto.TeamIds)
                {
                    await _gameService.AddTeamToGameAsync(game.Id, teamId);
                    await _hubContext.Clients.Group(game.Id.ToString()).SendAsync("TeamJoined", new { teamId, gameId = game.Id });
                }

                _logger.LogInformation("Teams {TeamIds} joined game with ID: {GameId}", string.Join(", ", dto.TeamIds), game.Id);
                return Ok(new { gameId = game.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining game with TeamIds: {TeamIds}", string.Join(", ", dto.TeamIds));
                return StatusCode(500, new { error = "Failed to join game." });
            }
        }

        [HttpGet("active")]
        [Authorize]
        public async Task<IActionResult> GetActiveGames()
        {
            try
            {
                var activeGames = await _dbContext.Games
                    .Where(g => g.Status == "InProgress")
                    .Select(g => new { g.Id, CreatedAt = g.CreatedAt })
                    .ToListAsync();

                _logger.LogInformation("Fetched active games: {@ActiveGames}", activeGames);
                return Ok(activeGames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active games");
                return StatusCode(500, new { error = "Failed to fetch active games" });
            }
        }

        [HttpGet("{gameId}/state")]
        [Authorize]
        public async Task<IActionResult> GetGameState(int gameId)
        {
            try
            {
                var game = await _dbContext.Games
                    .Where(g => g.Id == gameId)
                    .Select(g => new { g.CurrentRound, g.CurrentQuestionNumber })
                    .FirstOrDefaultAsync();
                if (game == null) return NotFound(new { error = "Game not found" });
                return Ok(game);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching game state for game {GameId}", gameId);
                return StatusCode(500, new { error = "Failed to fetch game state" });
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


        [HttpGet("all")]
        public async Task<ActionResult<List<Game>>> GetAllGames()
        {
            var games = await _dbContext.Games
                .Select(g => new { g.Id, g.Name, g.CreatedAt, g.Status })
                .OrderByDescending(g => g.CreatedAt) // Sort by creation time, most recent first
                .ToListAsync();
            return Ok(games);
        }

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

        [HttpGet("{gameId}/scores")]
        [Authorize(Policy = "OperatorPolicy")]
        public async Task<IActionResult> GetScores(int gameId)
        {
            try
            {
                var scores = await _dbContext.GameTeams
                    .Where(gt => gt.GameId == gameId)
                    .Select(gt => new { gt.TeamId, gt.Score })
                    .ToListAsync();

                _logger.LogInformation("Fetched scores for game {GameId}: {@Scores}", gameId, scores);
                return Ok(scores);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching scores for game {GameId}", gameId);
                return StatusCode(500, new { error = "Failed to fetch scores." });
            }
        }
    }

    public class JoinGameDto
    {
        public List<int> TeamIds { get; set; }
        public int? GameId { get; set; }
    }

    public class SubmitAnswerDto
    {
        public int GameId { get; set; }
        public int TeamId { get; set; }
        public int QuestionId { get; set; }
        public string SelectedAnswer { get; set; }
        public int Wager { get; set; }
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
}