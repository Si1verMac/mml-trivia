using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TriviaApp.Models;
using TriviaApp.Hubs;


namespace TriviaApp.Services{

public class AnswerService
{
    private readonly TriviaDbContext _context;
    private readonly IHubContext<TriviaHub> _hubContext;
    private readonly string _connectionString;

    public AnswerService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, IConfiguration configuration)
    {
        _context = context;
        _hubContext = hubContext;
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public async Task SubmitAnswerAsync(int gameId, int teamId, int questionId, string selectedAnswer, int? wager)
    {
        // Use EF Core to fetch the question (standard CRUD)
        var question = await _context.Questions.FindAsync(questionId);
        if (question == null) throw new Exception("Question not found");

        var isCorrect = selectedAnswer == question.CorrectAnswer;

        // New raw SQL for inserting the answer (replacing previous NpgsqlCommand)
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Replace the previous INSERT command with this optimized raw SQL
        var sql = @"
            INSERT INTO Answers (GameId, TeamId, QuestionId, SelectedAnswer, Wager, IsCorrect, SubmittedAt)
            VALUES (@gameId, @teamId, @questionId, @selectedAnswer, @wager, 
                    (SELECT CorrectAnswer = @selectedAnswer FROM Questions WHERE Id = @questionId), NOW())";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new NpgsqlParameter("gameId", gameId),
            new NpgsqlParameter("teamId", teamId),
            new NpgsqlParameter("questionId", questionId),
            new NpgsqlParameter("selectedAnswer", selectedAnswer),
            new NpgsqlParameter("wager", wager ?? (object)DBNull.Value));

        // Keep the existing raw SQL for checking total teams and answers submitted
        var totalTeamsCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM GameTeams WHERE GameId = @GameId", conn);
        totalTeamsCmd.Parameters.AddWithValue("GameId", gameId);
        var totalTeams = (long)await totalTeamsCmd.ExecuteScalarAsync();

        var answersSubmittedCmd = new NpgsqlCommand(
            "SELECT COUNT(DISTINCT TeamId) FROM Answers WHERE GameId = @GameId AND QuestionId = @QuestionId", conn);
        answersSubmittedCmd.Parameters.AddWithValue("GameId", gameId);
        answersSubmittedCmd.Parameters.AddWithValue("QuestionId", questionId);
        var answersSubmitted = (long)await answersSubmittedCmd.ExecuteScalarAsync();

        if (answersSubmitted == totalTeams)
        {
            await _hubContext.Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId);
        }
    }
}
}