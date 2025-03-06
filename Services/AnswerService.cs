using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using TriviaApp.Data;
using TriviaApp.Hubs;
using TriviaApp.Models;

namespace TriviaApp.Services
{
    public class AnswerService
    {
        private readonly TriviaDbContext _context;
        private readonly IHubContext<TriviaHub> _hubContext;
        private readonly string _connectionString;
        private readonly ILogger<AnswerService> _logger;

        public AnswerService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, IConfiguration configuration, ILogger<AnswerService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// Upserts an answer record (including wager) for a given game, team, and question.
        /// Marks the corresponding GameQuestion as answered.
        /// Then, it counts distinct active submissions (using the hub's active team count) and, if all active teams have submitted,
        /// broadcasts the correct answer via SignalR.
        /// </summary>
        public async Task SubmitAnswerAsync(int gameId, int teamId, int questionId, string selectedAnswer, int? wager)
        {
            // Fetch the question.
            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
                throw new Exception("Question not found");

            bool isCorrect = selectedAnswer.Trim().Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // Check if an answer record exists for this game, team, and question.
            var sqlCheck = "SELECT id FROM answers WHERE gameid = @gameId AND teamid = @teamId AND questionid = @questionId";
            await using var cmdCheck = new NpgsqlCommand(sqlCheck, conn);
            cmdCheck.Parameters.AddWithValue("@gameId", gameId);
            cmdCheck.Parameters.AddWithValue("@teamId", teamId);
            cmdCheck.Parameters.AddWithValue("@questionId", questionId);
            var existingIdObj = await cmdCheck.ExecuteScalarAsync();

            if (existingIdObj != null)
            {
                // Update existing record.
                var sqlUpdate = @"
                    UPDATE answers 
                    SET selectedanswer = @selectedAnswer, 
                        wager = @wager, 
                        iscorrect = (SELECT correctanswer = @selectedAnswer FROM questions WHERE id = @questionId),
                        submittedat = NOW()
                    WHERE id = @id";
                await using var cmdUpdate = new NpgsqlCommand(sqlUpdate, conn);
                cmdUpdate.Parameters.AddWithValue("@selectedAnswer", selectedAnswer);
                cmdUpdate.Parameters.AddWithValue("@wager", (object)wager ?? DBNull.Value);
                cmdUpdate.Parameters.AddWithValue("@questionId", questionId);
                cmdUpdate.Parameters.AddWithValue("@id", existingIdObj);
                await cmdUpdate.ExecuteNonQueryAsync();
            }
            else
            {
                // Insert new record.
                var sqlInsert = @"
                    INSERT INTO answers (gameid, teamid, questionid, selectedanswer, wager, iscorrect, submittedat)
                    VALUES (@gameId, @teamId, @questionId, @selectedAnswer, @wager, 
                            (SELECT correctanswer = @selectedAnswer FROM questions WHERE id = @questionId), NOW())";
                await using var cmdInsert = new NpgsqlCommand(sqlInsert, conn);
                cmdInsert.Parameters.AddWithValue("@gameId", gameId);
                cmdInsert.Parameters.AddWithValue("@teamid", teamId);
                cmdInsert.Parameters.AddWithValue("@questionid", questionId);
                cmdInsert.Parameters.AddWithValue("@selectedAnswer", selectedAnswer);
                cmdInsert.Parameters.AddWithValue("@wager", (object)wager ?? DBNull.Value);
                await cmdInsert.ExecuteNonQueryAsync();
            }

            // Mark the corresponding GameQuestion as answered.
            var gameQuestion = await _context.GameQuestions.FirstOrDefaultAsync(gq => gq.GameId == gameId && gq.QuestionId == questionId);
            if (gameQuestion != null)
            {
                gameQuestion.IsAnswered = true;
                await _context.SaveChangesAsync();
            }

            // Count active teams using the hub's helper.
            int totalActiveTeams = TriviaHub.GetActiveTeamCount(gameId);
            // Count distinct team submissions for this question from Answers.
            int submittedCount = await _context.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == questionId)
                .Select(a => a.TeamId)
                .Distinct()
                .CountAsync();

            _logger.LogInformation("Game {GameId}: {SubmittedCount} of {TotalActiveTeams} active teams submitted answer for question {QuestionId}.",
                gameId, submittedCount, totalActiveTeams, questionId);

            // When all active teams have submitted, broadcast the correct answer.
            if (submittedCount >= totalActiveTeams && totalActiveTeams > 0)
            {
                _logger.LogInformation("All active teams submitted answers for game {GameId}, question {QuestionId}. Broadcasting correct answer.", gameId, questionId);
                await _hubContext.Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
            }
        }

        // Legacy method for score update, if needed.
        public async Task<bool> SubmitAnswer(int gameId, int teamId, string selectedAnswer)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                using var cmdGetQuestion = new NpgsqlCommand(
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

                using var cmdUpdateScore = new NpgsqlCommand(
                    "UPDATE gameteams SET score = score + @scoreChange WHERE game_id = @gameId AND team_id = @teamId", connection);
                cmdUpdateScore.Parameters.AddWithValue("@scoreChange", scoreChange);
                cmdUpdateScore.Parameters.AddWithValue("@gameId", gameId);
                cmdUpdateScore.Parameters.AddWithValue("@teamId", teamId);

                await cmdUpdateScore.ExecuteNonQueryAsync();

                _logger.LogInformation("Team {TeamId} submitted answer for game {GameId}. Correct: {IsCorrect}, Points: {Points}",
                    teamId, gameId, isCorrect, scoreChange);

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