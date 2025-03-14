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

        public async Task<Game> CreateGameAsync(string name)
        {
            var game = new Game
            {
                Name = name,
                Status = "Created",
                CreatedAt = DateTime.UtcNow, // Automatically set CreatedAt
                CurrentRound = 1,            // Start at round 1
                CurrentQuestionNumber = 1    // Start at question 1
            };

            _context.Games.Add(game);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created new game with ID: {GameId}", game.Id);
            return game;
        }

        public async Task<Game> StartGameAsync(int gameId)
        {
            _logger.LogInformation("Starting game with ID: {GameId}", gameId);

            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            if (game.Status == "InProgress")
            {
                _logger.LogInformation("Game {GameId} is already in progress.", gameId);
                return game;
            }

            if (game.Status != "Created")
            {
                _logger.LogWarning("Game with ID {GameId} cannot be started; it is in '{Status}' state.", gameId, game.Status);
                throw new Exception($"Game cannot be started; it is in '{game.Status}' state");
            }

            // Log the current status of the game
            _logger.LogInformation("Game {GameId} is in state: {Status} with {TeamCount} teams",
                gameId, game.Status, game.GameTeams?.Count ?? 0);

            // Clear existing GameQuestions if any
            if (game.GameQuestions.Any())
            {
                _context.GameQuestions.RemoveRange(game.GameQuestions);
                await _context.SaveChangesAsync();
                game.GameQuestions.Clear();
            }

            _logger.LogInformation("Fetching questions for game {GameId}...", gameId);

            // Count questions in the database
            var questionCount = await _context.Questions.CountAsync();
            _logger.LogInformation("Found {Count} total questions in database", questionCount);

            // Load questions (adjust the number to load as needed)
            var questions = await _context.Questions
                .OrderBy(q => q.Id) // Randomize the order
                .Take(Math.Min(10, questionCount)) // Take 10 or all questions if less than 10
                .ToListAsync();

            _logger.LogInformation("Retrieved {Count} questions for game {GameId}. First question ID: {FirstQuestionId}",
                questions.Count, gameId, questions.FirstOrDefault()?.Id ?? 0);

            if (!questions.Any())
            {
                _logger.LogWarning("No questions available to start game {GameId}.", gameId);
                throw new Exception("No questions available to start the game");
            }

            _logger.LogInformation("Adding {Count} questions to game {GameId}...", questions.Count, gameId);

            int order = 1;
            foreach (var q in questions)
            {
                _logger.LogDebug("Adding question ID {QuestionId}: {QuestionText}", q.Id, q.Text);
                var gameQuestion = new GameQuestion
                {
                    GameId = game.Id,
                    QuestionId = q.Id,
                    OrderIndex = order++,
                    IsAnswered = false
                };
                _context.GameQuestions.Add(gameQuestion);
                _logger.LogDebug("Added question {QuestionId} to game {GameId}, order {Order}", q.Id, gameId, order - 1);
            }
            await _context.SaveChangesAsync();

            // Reload game with updated questions
            game = await _context.Games
                .Include(g => g.GameQuestions)
                .ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            _logger.LogInformation("Loaded {Count} questions for game {GameId}", game.GameQuestions.Count, gameId);

            var firstGameQuestion = game.GameQuestions
                .Where(gq => !gq.IsAnswered)
                .OrderBy(gq => gq.OrderIndex)
                .FirstOrDefault();

            if (firstGameQuestion != null)
            {
                game.CurrentQuestionId = firstGameQuestion.QuestionId;
                game.CurrentQuestion = firstGameQuestion.Question;
                _logger.LogInformation("Set first question for game {GameId}: {QuestionId}",
                    gameId, firstGameQuestion.QuestionId);
            }
            else
            {
                _logger.LogWarning("No questions assigned to game {GameId}.", gameId);
                throw new Exception("No questions available to start the game");
            }

            game.Status = "InProgress";
            game.StartedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Broadcast to all clients that the game started - use PascalCase
            var gameData = new { gameId };
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameStarted", gameData);

            // Also send the first question
            if (game.CurrentQuestion != null)
            {
                // Include round & questionNumber if you want to display them immediately
                var questionData = new
                {
                    id = game.CurrentQuestion.Id,
                    text = game.CurrentQuestion.Text,
                    options = game.CurrentQuestion.Options,
                    round = game.CurrentRound,
                    questionNumber = game.CurrentQuestionNumber
                };

                // Use PascalCase for Question as expected by the frontend
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", questionData);

                _logger.LogInformation("Sent first question for game {GameId}", gameId);
            }

            _logger.LogInformation("Game {GameId} started successfully.", gameId);
            return game;
        }

        public async Task<Game> GetOrCreateOpenGameAsync()
        {
            var openGame = await _context.Games
                .Where(g => g.Status == "Created" && g.CreatedAt > DateTime.UtcNow.AddMinutes(-5)) // 5-minute window
                .OrderByDescending(g => g.CreatedAt)
                .FirstOrDefaultAsync();

            if (openGame != null)
            {
                _logger.LogInformation("Reusing open game with ID: {GameId}", openGame.Id);
                return openGame;
            }

            var newGame = await CreateGameAsync("New Game");
            _logger.LogInformation("Created new game with ID: {GameId} for teams to join.", newGame.Id);
            return newGame;
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

        public async Task<Game> AdvanceToNextQuestionAsync(int gameId)
        {
            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
                throw new Exception($"Game {gameId} not found.");

            if (game.Status != "InProgress")
                throw new Exception("Game is not in progress.");

            // Mark the current question as answered (if not already)
            var currentGQ = game.GameQuestions
                .FirstOrDefault(gq => gq.QuestionId == game.CurrentQuestionId && !gq.IsAnswered);
            if (currentGQ != null)
            {
                currentGQ.IsAnswered = true;
            }

            // Find the next unanswered question
            var nextGQ = game.GameQuestions
                .Where(gq => !gq.IsAnswered)
                .OrderBy(gq => gq.OrderIndex)
                .FirstOrDefault();

            if (nextGQ != null)
            {
                // Example logic: every 3 questions => increment round
                if (game.CurrentQuestionNumber >= 3)
                {
                    game.CurrentRound++;
                    game.CurrentQuestionNumber = 1;
                }
                else
                {
                    game.CurrentQuestionNumber++;
                }

                // Update the current question pointer
                game.CurrentQuestionId = nextGQ.QuestionId;

                await _context.SaveChangesAsync();

                // Now broadcast the next question, including round & questionNumber
                var questionData = new
                {
                    id = nextGQ.Question.Id,
                    text = nextGQ.Question.Text,
                    options = nextGQ.Question.Options,
                    round = game.CurrentRound,
                    questionNumber = game.CurrentQuestionNumber
                };

                // Use PascalCase for Question as expected by frontend
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", questionData);

                return game;
            }
            else
            {
                // No more questions, end the game
                game.Status = "Completed";
                game.EndedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Broadcast game ended event - use PascalCase for frontend
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameEnded");

                return game;
            }
        }

        public async Task TriggerTimerExpiry(int gameId, int questionId)
        {
            // Use PascalCase as expected by frontend
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TimerExpired");
            await _hubContext.Clients.All.SendAsync("HandleTimerExpiry", gameId, questionId);
        }
        public async Task<Game> EndGameAsync(int gameId)
        {
            _logger.LogInformation("Ending game with ID: {GameId}", gameId);

            // Retrieve the game from the database
            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            // Check if the game is in the correct state
            if (game.Status != "InProgress")
            {
                _logger.LogWarning("Game with ID {GameId} cannot be ended; it is not in 'InProgress' state.", gameId);
                throw new Exception("Game cannot be ended; it is not in 'InProgress' state");
            }

            // Mark as completed
            game.Status = "Completed";
            game.EndedAt = DateTime.UtcNow;

            _logger.LogInformation("Resetting question status for game {GameId}...", gameId);

            // Reset all the question status for this game, so they can be reused
            // THIS IS THE FIX: reset all IsAnswered flags to false
            int resetCount = 0;
            foreach (var gameQuestion in game.GameQuestions)
            {
                gameQuestion.IsAnswered = false;
                resetCount++;
            }

            _logger.LogInformation("Reset {Count} questions for game {GameId}", resetCount, gameId);

            // Save changes
            await _context.SaveChangesAsync();

            // Broadcast game ended event - use PascalCase for frontend
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameEnded");

            _logger.LogInformation("Game {GameId} ended successfully", gameId);

            return game;
        }
    }
}