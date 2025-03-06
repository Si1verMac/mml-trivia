using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriviaApp.Data;
using TriviaApp.Models;
using TriviaApp.Services;

namespace TriviaApp.Hubs
{
 public class TriviaHub : Hub
 {
  private readonly QuestionService _questionService;
  private readonly TriviaDbContext _dbContext;
  private readonly ILogger<TriviaHub> _logger;

  // In-memory storage to track active (currently connected) teams for each game.
  private static readonly Dictionary<int, HashSet<int>> _activeTeams = new Dictionary<int, HashSet<int>>();

  public TriviaHub(QuestionService questionService, TriviaDbContext dbContext, ILogger<TriviaHub> logger)
  {
   _questionService = questionService;
   _dbContext = dbContext;
   _logger = logger;
  }

  // Helper: Returns the count of active teams for a given game.
  public static int GetActiveTeamCount(int gameId)
  {
   lock (_activeTeams)
   {
    return _activeTeams.ContainsKey(gameId) ? _activeTeams[gameId].Count : 0;
   }
  }

  public async Task JoinGame(int gameId, int teamId)
  {
   try
   {
    await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
    lock (_activeTeams)
    {
     if (!_activeTeams.ContainsKey(gameId))
     {
      _activeTeams[gameId] = new HashSet<int>();
     }
     _activeTeams[gameId].Add(teamId);
    }
    await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { gameId, teamId });
   }
   catch (Exception ex)
   {
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }

  public async Task LeaveGame(int gameId, int teamId)
  {
   await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId.ToString());
   lock (_activeTeams)
   {
    if (_activeTeams.ContainsKey(gameId))
    {
     _activeTeams[gameId].Remove(teamId);
    }
   }
  }

  // This method is used for real-time notification when a team submits a wager.
  // It checks the Answers table (via EF Core) to ensure that a team cannot submit more than once per question.
  public async Task SubmitWager(int gameId, int teamId, int wager, int questionId)
  {
   try
   {
    // Check if an answer already exists for this game, team, and question.
    var existingAnswer = await _dbContext.Answers
        .FirstOrDefaultAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == questionId);
    if (existingAnswer != null)
    {
     _logger.LogWarning("Team {TeamId} has already submitted an answer for question {QuestionId} in game {GameId}.", teamId, questionId, gameId);
     return; // Prevent duplicate wager submissions.
    }

    // Broadcast real-time that this team submitted its wager.
    await Clients.Group(gameId.ToString()).SendAsync("WagerSubmitted", new { gameId, teamId, wager, questionId });

    // Count active teams for this game.
    int totalActiveTeams = GetActiveTeamCount(gameId);
    // Count distinct submissions for this question from the Answers table.
    int submittedCount = await _dbContext.Answers
        .Where(a => a.GameId == gameId && a.QuestionId == questionId)
        .Select(a => a.TeamId)
        .Distinct()
        .CountAsync();

    _logger.LogInformation("Game {GameId}: {SubmittedCount} of {TotalActiveTeams} active teams submitted answer for question {QuestionId}.",
        gameId, submittedCount, totalActiveTeams, questionId);

    if (submittedCount >= totalActiveTeams && totalActiveTeams > 0)
    {
     var question = await _dbContext.Questions.FindAsync(questionId);
     if (question != null)
     {
      _logger.LogInformation("All active teams submitted answers for game {GameId}, question {QuestionId}. Broadcasting correct answer.", gameId, questionId);
      await Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
     }
    }
   }
   catch (Exception ex)
   {
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }

  public async Task ReadyForNextQuestion(int gameId, int teamId, int round, int questionId)
  {
   try
   {
    var question = await _questionService.GetNextQuestion(gameId);
    if (question != null)
    {
     await Clients.Group(gameId.ToString()).SendAsync("Question", question);
    }
   }
   catch (Exception ex)
   {
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }
 }
}