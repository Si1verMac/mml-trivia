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

        public async Task<Game> GetOrCreateOpenGameAsync()
        {
            var openGame = await _context.Games
                .Where(g => g.Status == "Created")
                .OrderByDescending(g => g.CreatedAt)
                .FirstOrDefaultAsync();

            return openGame ?? await CreateGameAsync("New Game");
        }

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
            _logger.LogInformation("Created new game with ID: {GameId}", game.Id);
            return game;
        }

        public async Task<Game> AddTeamToGameAsync(int gameId, int teamId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            bool exists = await _context.GameTeams.AnyAsync(gt => gt.GameId == gameId && gt.TeamId == teamId);
            if (!exists)
            {
                var gameTeam = new GameTeam
                {
                    GameId = gameId,
                    TeamId = teamId,
                    Score = 0
                };

                _context.GameTeams.Add(gameTeam);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Added team {TeamId} to game {GameId}", teamId, gameId);
            }
            return game;
        }

        public async Task<Game> StartGameAsync(int gameId)
        {
            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }
            if (game.Status != "Created")
            {
                throw new Exception("Game already started or completed");
            }

            if (game.GameQuestions.Any())
            {
                _context.GameQuestions.RemoveRange(game.GameQuestions);
                await _context.SaveChangesAsync();
                game.GameQuestions.Clear();
                _logger.LogInformation("Cleared existing questions for game {GameId}.", game.Id);
            }

            var questions = await _context.Questions.OrderBy(q => q.Id).ToListAsync();
            _logger.LogInformation("Loaded {Count} questions from the database.", questions.Count);

            if (!questions.Any())
            {
                throw new Exception("No questions available to start the game");
            }

            int order = 1;
            foreach (var q in questions)
            {
                var gameQuestion = new GameQuestion
                {
                    GameId = game.Id,
                    QuestionId = q.Id,
                    OrderIndex = order++,
                    IsAnswered = false
                };
                _context.GameQuestions.Add(gameQuestion);
            }
            await _context.SaveChangesAsync();

            game = await _context.Games
                .Include(g => g.GameQuestions)
                .ThenInclude(gq => gq.Question)
                .AsSplitQuery()
                .FirstOrDefaultAsync(g => g.Id == gameId);

            var unanswered = game.GameQuestions
                .Where(gq => !gq.IsAnswered)
                .OrderBy(gq => gq.OrderIndex)
                .ToList();

            _logger.LogInformation("Game {GameId} has {Count} unanswered questions after population.", gameId, unanswered.Count);

            var firstGameQuestion = unanswered.FirstOrDefault();
            if (firstGameQuestion != null)
            {
                game.CurrentQuestionId = firstGameQuestion.QuestionId;
                game.CurrentQuestion = firstGameQuestion.Question;
                _logger.LogInformation("Set current question ID to {QuestionId} for game {GameId}.", firstGameQuestion.QuestionId, game.Id);
            }
            else
            {
                throw new Exception("No questions available to start the game");
            }

            game.Status = "InProgress";
            game.StartedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Broadcast the first question
            if (game.CurrentQuestion != null)
            {
                await _hubContext.Clients.Group(gameId.ToString())
                    .SendAsync("Question", game.CurrentQuestion);
                _logger.LogInformation("Broadcasted first question {QuestionId} for game {GameId}.", game.CurrentQuestion.Id, gameId);
            }

            _logger.LogInformation("Game {GameId} started.", gameId);
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
            _logger.LogInformation("Game {GameId} ended.", gameId);
            return game;
        }
    }
}