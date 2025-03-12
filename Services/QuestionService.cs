using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriviaApp.Data;
using TriviaApp.Hubs;
using TriviaApp.Models;

namespace TriviaApp.Services
{
    public class QuestionService
    {
        private readonly TriviaDbContext _context;
        private readonly IHubContext<TriviaHub> _hubContext;
        private readonly ILogger<QuestionService> _logger;

        public QuestionService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, ILogger<QuestionService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves the next unanswered question for the game, sets it as current, and broadcasts it as a DTO.
        /// </summary>
        public async Task<Question?> GetNextQuestion(int gameId)
        {
            try
            {
                var game = await _context.Games
                    .Include(g => g.GameQuestions)
                    .ThenInclude(gq => gq.Question)
                    .FirstOrDefaultAsync(g => g.Id == gameId);

                if (game == null)
                {
                    _logger.LogWarning("Game {GameId} not found", gameId);
                    return null;
                }

                // Get first unanswered question FOR THIS SPECIFIC GAME.
                var nextGameQuestion = game.GameQuestions
                    .Where(gq => !gq.IsAnswered)
                    .OrderBy(gq => gq.OrderIndex)
                    .FirstOrDefault();

                if (nextGameQuestion == null)
                {
                    _logger.LogInformation("No more questions available for game {GameId}", gameId);
                    return null;
                }

                // Update the game's current question.
                game.CurrentQuestionId = nextGameQuestion.QuestionId;
                game.CurrentQuestion = nextGameQuestion.Question;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Retrieved next question {QuestionId} for game {GameId}", nextGameQuestion.QuestionId, gameId);
                // Notify clients in the game about the new question with both naming conventions
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("question", nextGameQuestion.Question);
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", nextGameQuestion.Question);
                return nextGameQuestion.Question;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next question for game {GameId}", gameId);
                throw;
            }
        }

        public async Task<IEnumerable<Question>> GetQuestionsForGameAsync(int gameId)
        {
            try
            {
                _logger.LogInformation("Getting questions for game {GameId}", gameId);

                var questions = await _context.GameQuestions
                    .Where(gq => gq.GameId == gameId)
                    .Include(gq => gq.Question)
                    .OrderBy(gq => gq.OrderIndex)
                    .Select(gq => gq.Question)
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} questions for game {GameId}", questions.Count, gameId);

                if (!questions.Any())
                {
                    _logger.LogWarning("No questions found for game {GameId}", gameId);
                }

                return questions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting questions for game {GameId}", gameId);
                return Enumerable.Empty<Question>();
            }
        }
    }
}