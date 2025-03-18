using System;
using System.Collections.Generic;
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
                CreatedAt = DateTime.UtcNow,
                CurrentRound = 1,
                CurrentQuestionNumber = 1
            };

            _context.Games.Add(game);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created new game with ID: {GameId}", game.Id);
            return game;
        }

        public async Task<Game> StartGameAsync(int gameId)
        {
            _logger.LogInformation("Starting game with ID: {GameId}", gameId);

            try
            {
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

                _logger.LogInformation("Game {GameId} is in state: {Status} with {TeamCount} teams",
                    gameId, game.Status, game.GameTeams?.Count ?? 0);

                if (game.GameQuestions.Any())
                {
                    _context.GameQuestions.RemoveRange(game.GameQuestions);
                    await _context.SaveChangesAsync();
                    game.GameQuestions.Clear();
                }

                _logger.LogInformation("Fetching questions for game {GameId}...", gameId);

                var questionCount = await _context.Questions.CountAsync();
                _logger.LogInformation("Found {Count} total questions in database", questionCount);

                if (questionCount < 3)
                {
                    _logger.LogError("Not enough questions in database. Found {Count}, need at least 3.", questionCount);
                    throw new Exception("Not enough questions available to start the game. Need at least 3.");
                }

                var allQuestions = await _context.Questions.ToListAsync();
                _logger.LogInformation("Loaded {Count} questions", allQuestions.Count);

                if (allQuestions.Count == 0)
                {
                    _logger.LogError("No questions loaded from database.");
                    throw new Exception("No questions available to start the game.");
                }

                int totalQuestions = allQuestions.Count;
                _logger.LogInformation("Planning game with {Count} total questions", totalQuestions);

                var gameQuestions = new List<Question>();
                foreach (var question in allQuestions)
                {
                    gameQuestions.Add(question);
                    _logger.LogInformation("Adding question {Id}: {Text}", question.Id, question.Text);
                }

                int order = 1;
                foreach (var question in gameQuestions)
                {
                    var gameQuestion = new GameQuestion
                    {
                        GameId = game.Id,
                        QuestionId = question.Id,
                        OrderIndex = order++,
                        IsAnswered = false
                    };
                    _context.GameQuestions.Add(gameQuestion);
                    _logger.LogInformation("Added question {Id} to game {GameId} at position {Order}",
                        question.Id, gameId, order - 1);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Saved {Count} questions to game {GameId}", gameQuestions.Count, gameId);

                game = await _context.Games
                    .Include(g => g.GameQuestions)
                    .ThenInclude(gq => gq.Question)
                    .FirstOrDefaultAsync(g => g.Id == gameId);

                _logger.LogInformation("Loaded {Count} questions for game {GameId}", game.GameQuestions.Count, gameId);

                var firstGameQuestion = game.GameQuestions
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

                var gameData = new { GameId = gameId };
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameStarted", gameData);

                if (game.CurrentQuestion != null)
                {
                    var questionData = new
                    {
                        Id = game.CurrentQuestion.Id,
                        Text = game.CurrentQuestion.Text,
                        Options = game.CurrentQuestion.Options,
                        Round = game.CurrentRound,
                        QuestionNumber = game.CurrentQuestionNumber,
                        QuestionType = game.CurrentQuestion.Type // Include database-driven type
                    };

                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", questionData);
                    _logger.LogInformation("Sent first question for game {GameId}", gameId);
                }

                _logger.LogInformation("Game {GameId} started successfully.", gameId);
                return game;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting game {GameId}: {Message}", gameId, ex.Message);
                throw;
            }
        }

        public async Task<Game> GetOrCreateOpenGameAsync()
        {
            var openGame = await _context.Games
                .Where(g => g.Status == "Created" && g.CreatedAt > DateTime.UtcNow.AddMinutes(-5))
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
            try
            {
                _logger.LogInformation("Advancing to next question for game {GameId}", gameId);

                var game = await _context.Games
                    .Include(g => g.GameQuestions)
                    .ThenInclude(gq => gq.Question)
                    .FirstOrDefaultAsync(g => g.Id == gameId);

                if (game == null)
                {
                    _logger.LogError("Game {GameId} not found", gameId);
                    throw new Exception($"Game {gameId} not found.");
                }

                if (game.Status != "InProgress")
                {
                    _logger.LogWarning("Game {GameId} is not in progress. Current status: {Status}", gameId, game.Status);
                    throw new Exception("Game is not in progress.");
                }

                var currentGQ = game.GameQuestions
                    .FirstOrDefault(gq => gq.QuestionId == game.CurrentQuestionId);
                if (currentGQ != null)
                {
                    currentGQ.IsAnswered = true;
                    _logger.LogInformation("Marked question {QuestionId} as answered", currentGQ.QuestionId);
                }
                else
                {
                    _logger.LogWarning("Current question not found for game {GameId}", gameId);
                }

                var allGameQuestions = game.GameQuestions
                    .OrderBy(gq => gq.OrderIndex)
                    .ToList();

                int currentIndex = -1;
                if (currentGQ != null)
                {
                    currentIndex = allGameQuestions.FindIndex(gq => gq.QuestionId == currentGQ.QuestionId);
                    _logger.LogInformation("Current question index: {Index} of {Total}", currentIndex, allGameQuestions.Count);
                }

                GameQuestion nextGQ = null;
                if (currentIndex >= 0 && currentIndex < allGameQuestions.Count - 1)
                {
                    nextGQ = allGameQuestions[currentIndex + 1];
                    _logger.LogInformation("Found next question at index {Index}: {QuestionId}",
                        currentIndex + 1, nextGQ.QuestionId);
                }

                if (nextGQ != null)
                {
                    // Use the database-driven question type
                    string questionType = nextGQ.Question.Type?.ToLowerInvariant() ?? "regular";
                    _logger.LogInformation("Determined question type for next question {QuestionId}: {QuestionType}",
                        nextGQ.QuestionId, questionType);

                    // Update round and question number based on question type or progression
                    switch (questionType)
                    {
                        case "lightning":
                            game.CurrentRound = 4;
                            game.CurrentQuestionNumber = 1;
                            _logger.LogInformation("Moving to Lightning Bonus Question");
                            break;
                        case "halftimebonus":
                        case "halftime_bonus":
                        case "halftime-bonus":
                        case "halftimeBonus":
                            game.CurrentRound = 4;
                            game.CurrentQuestionNumber = 2;
                            _logger.LogInformation("Moving to Halftime Bonus Round");
                            break;
                        case "halftimebreak":
                        case "halftime_break":
                        case "halftime-break":
                        case "halftimeBreak":
                            game.CurrentRound = 4;
                            game.CurrentQuestionNumber = 3;
                            _logger.LogInformation("Moving to Halftime Break");
                            break;
                        case "multiquestion":
                        case "multiquestions":
                        case "multi-question":
                        case "multi_question":
                        case "multi question":
                        case "multiQuestion":
                            game.CurrentRound = 5;
                            game.CurrentQuestionNumber = 1;
                            _logger.LogInformation("Moving to Multi-Question Round (Round 5)");
                            break;
                        case "finalwager":
                        case "final-wager":
                        case "final_wager":
                        case "final":
                            game.CurrentRound = 8;
                            game.CurrentQuestionNumber = 1;
                            _logger.LogInformation("Moving to Final Wager Question");
                            break;
                        default: // "regular" or unrecognized type
                            // Check if we're advancing after Round 5 multiQuestion
                            if (game.CurrentRound == 5 && currentGQ?.Question?.Type?.ToLowerInvariant()?.Contains("multi") == true)
                            {
                                // Force advancement to Round 6 after multiQuestion
                                game.CurrentRound = 6;
                                game.CurrentQuestionNumber = 1;
                                _logger.LogInformation("Advancing to Round 6 after completing Multi-Question round");
                            }
                            // Check if it's a multi-question using contains checks
                            else if (questionType.Contains("multi") && (questionType.Contains("question") || questionType.Contains("round") || questionType.Contains("5")))
                            {
                                _logger.LogInformation("Detected multi-question via string matching: {QuestionType}", questionType);
                                game.CurrentRound = 5;
                                game.CurrentQuestionNumber = 1;
                                _logger.LogInformation("Moving to Multi-Question Round (Round 5) via pattern matching");
                            }
                            else if (currentIndex == -1) // First question
                            {
                                game.CurrentRound = 1;
                                game.CurrentQuestionNumber = 1;
                            }
                            else if (game.CurrentQuestionNumber >= 3) // End of a round
                            {
                                game.CurrentRound++;
                                game.CurrentQuestionNumber = 1;
                                _logger.LogInformation("Moving to Round {Round}", game.CurrentRound);
                            }
                            else
                            {
                                game.CurrentQuestionNumber++;
                                _logger.LogInformation("Moving to Question {Round}.{Number}",
                                    game.CurrentRound, game.CurrentQuestionNumber);
                            }
                            // Don't override the questionType for unrecognized types,
                            // instead let the client determine what to do with it
                            if (questionType == "regular")
                            {
                                // Only override for actually regular questions
                                questionType = "regular";
                            }
                            break;
                    }

                    game.CurrentQuestionId = nextGQ.QuestionId;
                    await _context.SaveChangesAsync();

                    var questionData = new
                    {
                        Id = nextGQ.Question.Id,
                        Text = nextGQ.Question.Text,
                        Options = nextGQ.Question.Options,
                        Round = game.CurrentRound,
                        QuestionNumber = game.CurrentQuestionNumber,
                        QuestionType = nextGQ.Question.Type // Use database type directly
                    };

                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", questionData);
                    _logger.LogInformation("Sent next question data: {Type} for round {Round}.{Number}",
                        questionType, game.CurrentRound, game.CurrentQuestionNumber);

                    return game;
                }
                else
                {
                    _logger.LogWarning("No more questions for game {GameId}. Total: {Total}, Current: {Current}",
                        gameId, allGameQuestions.Count, currentIndex);

                    game.Status = "Completed";
                    game.EndedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameEnded");
                    _logger.LogInformation("Game {GameId} ended - no more questions", gameId);

                    return game;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error advancing to next question for game {GameId}: {Message}",
                    gameId, ex.Message);
                throw;
            }
        }

        public async Task TriggerTimerExpiry(int gameId, int questionId)
        {
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("TimerExpired");
            await _hubContext.Clients.All.SendAsync("HandleTimerExpiry", gameId, questionId);
        }

        public async Task<Game> EndGameAsync(int gameId)
        {
            _logger.LogInformation("Ending game with ID: {GameId}", gameId);

            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .FirstOrDefaultAsync(g => g.Id == gameId);

            if (game == null)
            {
                _logger.LogError("Game with ID {GameId} not found.", gameId);
                throw new Exception("Game not found");
            }

            if (game.Status != "InProgress")
            {
                _logger.LogWarning("Game with ID {GameId} cannot be ended; it is not in 'InProgress' state.", gameId);
                throw new Exception("Game cannot be ended; it is not in 'InProgress' state");
            }

            game.Status = "Completed";
            game.EndedAt = DateTime.UtcNow;

            _logger.LogInformation("Resetting question status for game {GameId}...", gameId);

            int resetCount = 0;
            foreach (var gameQuestion in game.GameQuestions)
            {
                gameQuestion.IsAnswered = false;
                resetCount++;
            }

            _logger.LogInformation("Reset {Count} questions for game {GameId}", resetCount, gameId);

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("GameEnded");
            _logger.LogInformation("Game {GameId} ended successfully", gameId);

            return game;
        }
    }
}