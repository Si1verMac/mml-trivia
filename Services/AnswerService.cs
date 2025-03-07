using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TriviaApp.Data;
using TriviaApp.Hubs;
using TriviaApp.Models;


namespace TriviaApp.Services
{
    public class AnswerService
    {
        private readonly TriviaDbContext _context;
        private readonly ILogger<AnswerService> _logger;

        public AnswerService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, IConfiguration configuration, ILogger<AnswerService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Note: _hubContext is kept for DI compatibility but not used here
        }

        public async Task<(bool isCorrect, bool allSubmitted)> SubmitAnswerAsync(int gameId, int teamId, int questionId, string selectedAnswer, int? wager)
        {
            _logger.LogInformation("Received answer submission: GameId={GameId}, TeamId={TeamId}, QuestionId={QuestionId}, SelectedAnswer={SelectedAnswer}, Wager={Wager}",
                gameId, teamId, questionId, selectedAnswer, wager);

            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
            {
                throw new Exception("Question not found");
            }

            bool isCorrect = selectedAnswer != null && selectedAnswer.Trim().Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);

            var answer = await _context.Answers
                .FirstOrDefaultAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == questionId);

            if (answer == null)
            {
                answer = new Answer
                {
                    GameId = gameId,
                    TeamId = teamId,
                    QuestionId = questionId,
                    SelectedAnswer = selectedAnswer,
                    Wager = wager,
                    IsCorrect = isCorrect,
                    SubmittedAt = DateTime.UtcNow
                };
                _context.Answers.Add(answer);
            }
            else
            {
                answer.SelectedAnswer = selectedAnswer;
                answer.Wager = wager;
                answer.IsCorrect = isCorrect;
                answer.SubmittedAt = DateTime.UtcNow;
                _context.Answers.Update(answer);
            }
            await _context.SaveChangesAsync();

            int totalActiveTeams = TriviaHub.GetActiveTeamCount(gameId);
            int submittedCount = await _context.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == questionId)
                .Select(a => a.TeamId)
                .Distinct()
                .CountAsync();

            _logger.LogInformation("Game {GameId}: {SubmittedCount} of {TotalActiveTeams} active teams submitted answer for question {QuestionId}.",
                gameId, submittedCount, totalActiveTeams, questionId);

            bool allSubmitted = submittedCount >= totalActiveTeams && totalActiveTeams > 0;

            if (allSubmitted)
            {
                var gameQuestion = await _context.GameQuestions
                    .FirstOrDefaultAsync(gq => gq.GameId == gameId && gq.QuestionId == questionId);
                if (gameQuestion != null)
                {
                    gameQuestion.IsAnswered = true;
                    await _context.SaveChangesAsync();
                }
                _logger.LogInformation("All active teams submitted answers for game {GameId}, question {QuestionId}.", gameId, questionId);
            }

            return (isCorrect, allSubmitted);
        }

        public async Task<bool> SubmitAnswer(int gameId, int teamId, string selectedAnswer)
        {
            try
            {
                using var connection = new Npgsql.NpgsqlConnection(_context.Database.GetDbConnection().ConnectionString);
                await connection.OpenAsync();

                using var cmdGetQuestion = new Npgsql.NpgsqlCommand(
                    "SELECT q.* FROM questions q INNER JOIN games g ON g.current_question_id = q.id WHERE g.id = @gameId", connection);
                cmdGetQuestion.Parameters.AddWithValue("@gameId", gameId);

                using var reader = await cmdGetQuestion.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    _logger.LogWarning("No current question found for game {GameId}", gameId);
                    return false;
                }

                var correctAnswer = reader.GetString(reader.GetOrdinal("correct_answer"));
                var points = reader.GetInt32(reader.GetOrdinal("points"));
                reader.Close();

                var isCorrect = string.Equals(selectedAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase);
                var scoreChange = isCorrect ? points : 0;

                using var cmdUpdateScore = new Npgsql.NpgsqlCommand(
                    "UPDATE gameteams SET score = score + @scoreChange WHERE game_id = @gameId AND team_id = @teamId", connection);
                cmdUpdateScore.Parameters.AddWithValue("@scoreChange", scoreChange);
                cmdUpdateScore.Parameters.AddWithValue("@gameId", gameId);
                cmdUpdateScore.Parameters.AddWithValue("@teamId", teamId);

                await cmdUpdateScore.ExecuteNonQueryAsync();

                _logger.LogInformation("Team {TeamId} submitted answer for game {GameId}. Correct: {IsCorrect}, Points: {Points}", teamId, gameId, isCorrect, scoreChange);
                return isCorrect;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting answer for game {GameId}, team {TeamId}", gameId, teamId);
                throw;
            }
        }
    }
}