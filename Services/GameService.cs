using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriviaApp.Data;
using TriviaApp.Hubs;
using TriviaApp.Models;

namespace TriviaApp.Services
{
    public class GameService
    {
        private readonly TriviaDbContext _context;
        private readonly IHubContext<TriviaHub> _hubContext;
        private readonly ILogger<GameService> _logger;

        public GameService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, ILogger<GameService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // Helper method: get an open game (status "Created") or create one if none exists.
        public async Task<Game> GetOrCreateOpenGameAsync()
        {
            var openGame = await _context.Games
                .Include(g => g.GameTeams)
                .Include(g => g.GameQuestions)
                    .ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Status == "Created");

            if (openGame != null)
            {
                _logger.LogInformation("Found open game with ID: {GameId}", openGame.Id);
                return openGame;
            }

            return await CreateGameAsync("New Game");
        }

        /// <summary>
        /// Creates a new game and populates its questions in order.
        /// </summary>
        public async Task<Game> CreateGameAsync(string name)
        {
            var game = new Game
            {
                Name = name,
                Status = "Created",
                CreatedAt = DateTime.UtcNow
            };

            _context.Games.Add(game);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Game created with ID: {GameId}", game.Id);

            // Retrieve all questions in order (by Id – adjust if you have a dedicated ordering column)
            var questions = await _context.Questions.OrderBy(q => q.Id).ToListAsync();
            int orderIndex = 1;
            foreach (var question in questions)
            {
                var gameQuestion = new GameQuestion
                {
                    GameId = game.Id,
                    QuestionId = question.Id,
                    OrderIndex = orderIndex,
                    IsAnswered = false
                };
                _context.GameQuestions.Add(gameQuestion);
                orderIndex++;
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("GameQuestions populated for game ID: {GameId}", game.Id);

            return game;
        }

        /// <summary>
        /// Adds a team to an existing game.
        /// </summary>
        public async Task<Game> AddTeamToGameAsync(int gameId, int teamId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            // Only add the team if not already in the game
            if (!await _context.GameTeams.AnyAsync(gt => gt.GameId == gameId && gt.TeamId == teamId))
            {
                var team = await _context.Teams.FindAsync(teamId);
                if (team == null)
                {
                    _logger.LogError("Team with ID {TeamId} not found.", teamId);
                    throw new Exception("Team not found");
                }

                var gameTeam = new GameTeam
                {
                    GameId = gameId,
                    TeamId = teamId,
                    Score = 0
                };

                _context.GameTeams.Add(gameTeam);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Team with ID {TeamId} added to game with ID {GameId}.", teamId, gameId);
            }
            return game;
        }

        /// <summary>
        /// Starts the game: sets the status and the first question.
        /// </summary>
        public async Task<Game> StartGameAsync(int gameId)
        {
            var game = await _context.Games
                .Include(g => g.GameTeams)
                .Include(g => g.GameQuestions)
                    .ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            if (!game.GameQuestions.Any())
            {
                _logger.LogWarning("No questions available for game {GameId}", gameId);
            }
            else
            {
                // Set the first unanswered question based on OrderIndex
                var firstGameQuestion = game.GameQuestions.OrderBy(gq => gq.OrderIndex).FirstOrDefault();
                if (firstGameQuestion != null)
                {
                    game.CurrentQuestionId = firstGameQuestion.QuestionId;
                }
            }

            game.Status = "InProgress";
            game.StartedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Send the first question to all clients
            if (game.CurrentQuestionId.HasValue)
            {
                var firstQuestion = game.GameQuestions
                    .OrderBy(gq => gq.OrderIndex)
                    .FirstOrDefault()?.Question;
                if (firstQuestion != null)
                {
                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", firstQuestion);
                }
            }
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameStarted", game);

            _logger.LogInformation("Game with ID {GameId} started successfully.", gameId);
            return game;
        }

        public async Task<Game> EndGameAsync(int gameId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            game.Status = "Completed";
            game.EndedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameEnded", game);
            _logger.LogInformation("Game with ID {GameId} ended.", gameId);
            return game;
        }
    }
}