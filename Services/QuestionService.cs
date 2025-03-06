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

                var nextQuestion = game.GameQuestions
                    .Where(gq => !gq.IsAnswered)
                    .OrderBy(gq => gq.OrderIndex)
                    .FirstOrDefault()
                    ?.Question;

                if (nextQuestion == null)
                {
                    _logger.LogInformation("No more questions available for game {GameId}", gameId);
                    return null;
                }

                game.CurrentQuestion = nextQuestion;
                game.CurrentQuestionId = nextQuestion.Id;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Retrieved next question {QuestionId} for game {GameId}", nextQuestion.Id, gameId);
                // Use "Question" as the event name instead of "NewQuestion"
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("Question", nextQuestion);
                return nextQuestion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next question for game {GameId}", gameId);
                throw;
            }
        }

        public async Task<IEnumerable<Question>> GetQuestionsForGameAsync(int gameId)
        {
            return await _context.GameQuestions
                .Where(gq => gq.GameId == gameId)
                .Include(gq => gq.Question)
                .OrderBy(gq => gq.OrderIndex)
                .Select(gq => gq.Question)
                .ToListAsync();
        }
    }
}