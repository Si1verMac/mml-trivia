using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
  private readonly AnswerService _answerService; // Added
  private readonly TriviaDbContext _dbContext;
  private readonly ILogger<TriviaHub> _logger;

  private static readonly Dictionary<int, HashSet<int>> _activeTeams = new Dictionary<int, HashSet<int>>();
  private static readonly Dictionary<string, (int gameId, int teamId)> _connectionMappings = new Dictionary<string, (int, int)>();
  private static readonly Dictionary<int, HashSet<int>> _teamsReadyForNext = new Dictionary<int, HashSet<int>>();

  public TriviaHub(QuestionService questionService, AnswerService answerService, TriviaDbContext dbContext, ILogger<TriviaHub> logger)
  {
   _questionService = questionService;
   _answerService = answerService; // Added
   _dbContext = dbContext;
   _logger = logger;
  }

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
      _activeTeams[gameId] = new HashSet<int>();
     _activeTeams[gameId].Add(teamId);
    }
    lock (_connectionMappings)
    {
     _connectionMappings[Context.ConnectionId] = (gameId, teamId);
    }
    await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { gameId, teamId });

    var game = await _dbContext.Games
        .Include(g => g.CurrentQuestion)
        .FirstOrDefaultAsync(g => g.Id == gameId);
    if (game != null && game.Status == "InProgress" && game.CurrentQuestion != null)
    {
     await Clients.Caller.SendAsync("Question", game.CurrentQuestion);
    }
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
     _activeTeams[gameId].Remove(teamId);
   }
   lock (_connectionMappings)
   {
    _connectionMappings.Remove(Context.ConnectionId);
   }
  }

  public async Task SubmitWager(int gameId, int teamId, int wager, int questionId)
  {
   try
   {
    int totalActiveTeams = GetActiveTeamCount(gameId);
    int submittedCount = await _dbContext.Answers
        .Where(a => a.GameId == gameId && a.QuestionId == questionId && a.Wager != null)
        .Select(a => a.TeamId)
        .Distinct()
        .CountAsync();

    _logger.LogInformation("Game {GameId}: {SubmittedCount} of {TotalActiveTeams} active teams submitted wager for question {QuestionId}.", gameId, submittedCount, totalActiveTeams, questionId);
   }
   catch (Exception ex)
   {
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }

  // New SubmitAnswer method
  public async Task SubmitAnswer(int gameId, int teamId, int questionId, string selectedAnswer, int wager)
  {
   try
   {
    var (isCorrect, allSubmitted) = await _answerService.SubmitAnswerAsync(gameId, teamId, questionId, selectedAnswer, wager);

    // Notify only the submitting team
    await Clients.Caller.SendAsync("AnswerSubmitted", new { teamId, isCorrect });

    if (allSubmitted)
    {
     var question = await _dbContext.Questions.FindAsync(questionId);
     if (question != null)
     {
      await Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
     }
    }
   }
   catch (Exception ex)
   {
    _logger.LogError(ex, "Error in SubmitAnswer for game {GameId}, team {TeamId}", gameId, teamId);
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }

  public async Task SignalReadyForNext(int gameId, int teamId)
  {
   try
   {
    lock (_teamsReadyForNext)
    {
     if (!_teamsReadyForNext.ContainsKey(gameId))
      _teamsReadyForNext[gameId] = new HashSet<int>();
     if (_teamsReadyForNext[gameId].Add(teamId))
     {
      _logger.LogInformation("Team {TeamId} signaled ready for game {GameId}", teamId, gameId);
     }
     else
     {
      _logger.LogInformation("Team {TeamId} already signaled ready for game {GameId}", teamId, gameId);
     }
    }

    int totalActiveTeams = GetActiveTeamCount(gameId);
    int readyCount;
    lock (_teamsReadyForNext)
    {
     readyCount = _teamsReadyForNext.ContainsKey(gameId) ? _teamsReadyForNext[gameId].Count : 0;
    }

    _logger.LogInformation("Game {GameId}: {ReadyCount} of {TotalActiveTeams} teams ready for next question.", gameId, readyCount, totalActiveTeams);

    if (readyCount >= totalActiveTeams && totalActiveTeams > 0)
    {
     lock (_teamsReadyForNext)
     {
      _teamsReadyForNext.Remove(gameId);
     }
     var question = await _questionService.GetNextQuestion(gameId);
     if (question != null)
     {
      await Clients.Group(gameId.ToString()).SendAsync("Question", question);
      await Clients.Group(gameId.ToString()).SendAsync("AdvanceToNextQuestion");
     }
     else
     {
      await Clients.Group(gameId.ToString()).SendAsync("GameEnded");
     }
    }
   }
   catch (Exception ex)
   {
    await Clients.Caller.SendAsync("Error", ex.Message);
   }
  }

  public override async Task OnDisconnectedAsync(Exception? exception)
  {
   _logger.LogInformation("Connection {ConnectionId} disconnected", Context.ConnectionId);
   int? teamIdToRemove = null;
   int? gameIdToRemove = null;
   lock (_connectionMappings)
   {
    if (_connectionMappings.TryGetValue(Context.ConnectionId, out var mapping))
    {
     gameIdToRemove = mapping.gameId;
     teamIdToRemove = mapping.teamId;
     _connectionMappings.Remove(Context.ConnectionId);
    }
   }
   if (gameIdToRemove.HasValue && teamIdToRemove.HasValue)
   {
    lock (_activeTeams)
    {
     if (_activeTeams.ContainsKey(gameIdToRemove.Value))
      _activeTeams[gameIdToRemove.Value].Remove(teamIdToRemove.Value);
    }
    lock (_teamsReadyForNext)
    {
     if (_teamsReadyForNext.ContainsKey(gameIdToRemove.Value))
      _teamsReadyForNext[gameIdToRemove.Value].Remove(teamIdToRemove.Value);
    }
   }
   await base.OnDisconnectedAsync(exception);
  }
 }
}