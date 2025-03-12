using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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
    private readonly AnswerService _answerService;
    private readonly TriviaDbContext _dbContext;
    private readonly GameService _gameService;
    private readonly ILogger<TriviaHub> _logger;

    // In-memory state for active connections and submissions
    private static readonly Dictionary<int, HashSet<int>> _activeTeams = new();
    private static readonly Dictionary<string, (int gameId, int teamId)> _connectionMappings = new();
    private static readonly Dictionary<int, HashSet<int>> _teamsReadyForNext = new();
    private static readonly Dictionary<int, Dictionary<int, string>> _teamSubmissions = new();

    public TriviaHub(
    QuestionService questionService,
    AnswerService answerService,
    TriviaDbContext dbContext,
    GameService gameService,
    ILogger<TriviaHub> logger)
    {
      _questionService = questionService;
      _answerService = answerService;
      _dbContext = dbContext;
      _gameService = gameService;
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
        _logger.LogInformation("Team {TeamId} joined game {GameId}, ConnectionId: {ConnectionId}", teamId, gameId, Context.ConnectionId);

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

        // Send with PascalCase as expected by frontend
        await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { gameId, teamId });

        var game = await _dbContext.Games
          .Include(g => g.CurrentQuestion)
          .FirstOrDefaultAsync(g => g.Id == gameId);

        // Frontend doesn't use this event yet
        if (game != null)
        {
          await Clients.Caller.SendAsync("GameState", new
          {
            gameId = game.Id,
            status = game.Status,
            name = game.Name
          });
        }

        // Only send question if game is in progress - use PascalCase as expected by frontend
        if (game != null && game.Status == "InProgress" && game.CurrentQuestion != null)
        {
          var questionData = new
          {
            id = game.CurrentQuestion.Id,
            text = game.CurrentQuestion.Text,
            options = game.CurrentQuestion.Options,
            round = game.CurrentRound,
            questionNumber = game.CurrentQuestionNumber
          };

          await Clients.Caller.SendAsync("Question", questionData);
          
          // Check if this team has already answered the current question
          bool hasAnswered = false;
          if (game.CurrentQuestionId.HasValue)
          {
            hasAnswered = await _dbContext.Answers
              .AnyAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == game.CurrentQuestionId);
            
            _logger.LogInformation("Team {TeamId} joining game {GameId} - has already answered current question: {HasAnswered}", 
              teamId, gameId, hasAnswered);
              
            if (hasAnswered)
            {
              // Send the answer reveal if they've already answered
              await Clients.Caller.SendAsync("AnswerSubmitted", new { teamId, isCorrect = true });
              
              if (game.CurrentQuestion != null)
              {
                await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);
              }
              
              // Only check if they've explicitly clicked the ready button previously
              bool hasExplicitlySignaledReady = false;
              lock (_teamsReadyForNext)
              {
                hasExplicitlySignaledReady = _teamsReadyForNext.ContainsKey(gameId) && _teamsReadyForNext[gameId].Contains(teamId);
              }
              
              // Only send this event if they actually clicked the button before
              if (hasExplicitlySignaledReady)
              {
                _logger.LogInformation("Team {TeamId} previously clicked ready button for game {GameId}, sending TeamSignaledReady", 
                  teamId, gameId);
                await Clients.Caller.SendAsync("TeamSignaledReady", teamId);
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in JoinGame for game {GameId}, team {TeamId}", gameId, teamId);
        await Clients.Caller.SendAsync("Error", ex.Message);
      }
    }

    public async Task SubmitWager(int gameId, int teamId, int wager, int questionId)
    {
      try
      {
        _logger.LogInformation("Team {TeamId} submitted wager {Wager} for game {GameId}, question {QuestionId}", teamId, wager, gameId, questionId);
        // Example: Save to database or update game state
        // Optionally broadcast to clients if needed
        await Clients.Group($"Game_{gameId}").SendAsync("WagerSubmitted", teamId, wager);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in SubmitWager for game {GameId}, team {TeamId}", gameId, teamId);
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

    public async Task SubmitAnswer(int gameId, int teamId, int questionId, string selectedAnswer, int wager)
    {
      try
      {
        var (isCorrect, allSubmitted) = await _answerService.SubmitAnswerAsync(gameId, teamId, questionId, selectedAnswer, wager);

        lock (_teamSubmissions)
        {
          if (!_teamSubmissions.ContainsKey(gameId))
            _teamSubmissions[gameId] = new Dictionary<int, string>();
          _teamSubmissions[gameId][teamId] = selectedAnswer;
        }

        // Use PascalCase for AnswerSubmitted as expected by frontend
        await Clients.Caller.SendAsync("AnswerSubmitted", new { teamId, isCorrect });

        if (allSubmitted)
        {
          var question = await _dbContext.Questions.FindAsync(questionId);
          if (question != null)
          {
            _logger.LogInformation("All teams submitted for game {GameId}, question {QuestionId}. Sending DisplayAnswer.", gameId, questionId);
            
            // Add a small delay before sending DisplayAnswer to ensure the last submitting team has time to process their state change
            await Task.Delay(500);
            
            // Send to all clients including the one that just submitted
            _logger.LogInformation("Sending DisplayAnswer to all clients in game {GameId}", gameId);
            await Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
            
            // Also send directly to the caller to ensure they get it
            _logger.LogInformation("Sending additional DisplayAnswer directly to caller (Team {TeamId})", teamId);
            await Clients.Caller.SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
          }
          lock (_teamSubmissions)
          {
            _teamSubmissions.Remove(gameId);
          }
        }

        // Broadcast updated scores to operators
        var scores = await _dbContext.GameTeams
          .Where(gt => gt.GameId == gameId)
          .Select(gt => new { gt.TeamId, gt.Score })
          .ToListAsync();
        await Clients.Group("Operators").SendAsync("ScoresUpdated", scores);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in SubmitAnswer for game {GameId}, team {TeamId}", gameId, teamId);
        await Clients.Caller.SendAsync("Error", ex.Message);
      }
    }

    public async Task HandleTimerExpiry(int gameId, int questionId)
    {
      try
      {
        _logger.LogInformation("HandleTimerExpiry called for game {GameId}, question {QuestionId}", gameId, questionId);
        HashSet<int> activeTeams;
        lock (_activeTeams)
        {
          if (!_activeTeams.TryGetValue(gameId, out activeTeams))
          {
            _logger.LogWarning("No active teams for game {GameId}", gameId);
            return;
          }
        }

        List<int> submittedTeams;
        lock (_teamSubmissions)
        {
          submittedTeams = _teamSubmissions.ContainsKey(gameId) ? _teamSubmissions[gameId].Keys.ToList() : new List<int>();
        }

        var nonSubmittedTeams = activeTeams.Except(submittedTeams).ToList();
        _logger.LogInformation("Teams that haven't submitted for game {GameId}: {NonSubmittedTeams}", gameId, string.Join(", ", nonSubmittedTeams));

        var question = await _dbContext.Questions.FindAsync(questionId);
        if (question == null)
        {
          _logger.LogWarning("Question {QuestionId} not found", questionId);
          return;
        }

        var defaultAnswer = question.Options.FirstOrDefault() ?? "No Answer";
        int defaultWager = 1;

        foreach (var teamId in nonSubmittedTeams)
        {
          _logger.LogInformation("Submitting default answer for team {TeamId} in game {GameId}", teamId, gameId);
          await SubmitAnswer(gameId, teamId, questionId, defaultAnswer, defaultWager);
        }

        int totalActiveTeams = GetActiveTeamCount(gameId);
        int submittedCount = await _dbContext.Answers
        .Where(a => a.GameId == gameId && a.QuestionId == questionId)
        .Select(a => a.TeamId)
        .Distinct()
        .CountAsync();

        if (submittedCount >= totalActiveTeams && totalActiveTeams > 0)
        {
          _logger.LogInformation("All teams have submitted for game {GameId}, question {QuestionId}", gameId, questionId);
          await Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in HandleTimerExpiry for game {GameId}", gameId);
      }
    }

    public async Task SignalReadyForNext(int gameId, int teamId)
    {
      try
      {
        // IMPORTANT: This method should ONLY be called when a user explicitly clicks the "Ready" button
        // We track which teams have signaled ready in this dictionary, so it's critical that we only
        // add a team here when they've actually pressed the button, not through any automatic process
        _logger.LogInformation("Team {TeamId} explicitly signaled ready (button clicked) for game {GameId}", teamId, gameId);
        
        bool needToBroadcast = false;
        
        lock (_teamsReadyForNext)
        {
          if (!_teamsReadyForNext.ContainsKey(gameId))
            _teamsReadyForNext[gameId] = new HashSet<int>();
          
          // Only add the team if they're not already in the ready list
          if (!_teamsReadyForNext[gameId].Contains(teamId))
          {
            _teamsReadyForNext[gameId].Add(teamId);
            _logger.LogInformation("Team {TeamId} signaled ready for game {GameId}. Ready teams: {ReadyCount}/{TotalActive}", 
              teamId, gameId, _teamsReadyForNext[gameId].Count, GetActiveTeamCount(gameId));
            
            // Flag that we need to broadcast after exiting the lock
            needToBroadcast = true;
          }
          else
          {
            _logger.LogInformation("Team {TeamId} already signaled ready for game {GameId}, ignoring duplicate", teamId, gameId);
          }
        }
        
        // Only broadcast if this is a new ready signal - OUTSIDE the lock
        if (needToBroadcast)
        {
          await Clients.Group(gameId.ToString()).SendAsync("TeamSignaledReady", teamId);
        }

        int totalActiveTeams = GetActiveTeamCount(gameId);
        int readyCount;
        bool shouldAdvance = false;
        
        lock (_teamsReadyForNext)
        {
          readyCount = _teamsReadyForNext.ContainsKey(gameId) ? _teamsReadyForNext[gameId].Count : 0;
          shouldAdvance = readyCount >= totalActiveTeams && totalActiveTeams > 0;
          
          // If we should advance, clear the ready status now
          if (shouldAdvance)
          {
            _logger.LogInformation("All teams ready for game {GameId}, clearing ready state before advancing.", gameId);
            _teamsReadyForNext.Remove(gameId);
          }
        }

        if (shouldAdvance)
        {
          _logger.LogInformation("Advancing to next question for game {GameId}", gameId);
          await _gameService.AdvanceToNextQuestionAsync(gameId);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in SignalReadyForNext for game {GameId}", gameId);
        await Clients.Caller.SendAsync("Error", ex.Message);
      }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
      lock (_connectionMappings)
      {
        if (_connectionMappings.TryGetValue(Context.ConnectionId, out var mapping))
        {
          lock (_activeTeams)
          {
            if (_activeTeams.ContainsKey(mapping.gameId))
              _activeTeams[mapping.gameId].Remove(mapping.teamId);
          }
          _connectionMappings.Remove(Context.ConnectionId);
        }
      }
      await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinOperatorGroup()
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, "Operators");
      _logger.LogInformation("User added to Operators group.");
      await Clients.Caller.SendAsync("JoinedOperatorGroup", "You have joined the Operators group.");
    }

    public async Task RequestGameState(int gameId, int teamId)
    {
      try
      {
        _logger.LogInformation("Team {TeamId} requested current game state for game {GameId}", teamId, gameId);
        
        var game = await _dbContext.Games
          .Include(g => g.CurrentQuestion)
          .FirstOrDefaultAsync(g => g.Id == gameId);
          
        if (game == null)
        {
          _logger.LogWarning("Game {GameId} not found when requesting state", gameId);
          return;
        }
        
        // Send game state
        _logger.LogInformation("Sending GameState for game {GameId} to team {TeamId}: Status={Status}", 
          gameId, teamId, game.Status);
          
        await Clients.Caller.SendAsync("GameState", new
        {
          gameId = game.Id,
          status = game.Status,
          name = game.Name
        });
        
        // If game is in progress and has a current question, send it
        if (game.Status == "InProgress" && game.CurrentQuestion != null)
        {
          _logger.LogInformation("Game {GameId} is in progress with question {QuestionId}", 
            gameId, game.CurrentQuestionId);
            
          var questionData = new
          {
            id = game.CurrentQuestion.Id,
            text = game.CurrentQuestion.Text,
            options = game.CurrentQuestion.Options,
            round = game.CurrentRound,
            questionNumber = game.CurrentQuestionNumber
          };
          
          await Clients.Caller.SendAsync("Question", questionData);
          
          // Check if this team has already answered the current question
          var hasAnswered = await _dbContext.Answers
            .AnyAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == game.CurrentQuestionId);
            
          _logger.LogInformation("Team {TeamId} in game {GameId} has answered current question {QuestionId}: {HasAnswered}", 
            teamId, gameId, game.CurrentQuestionId, hasAnswered);
            
          if (hasAnswered)
          {
            // Let the client know they've already submitted an answer
            await Clients.Caller.SendAsync("AnswerSubmitted", new { teamId, isCorrect = true });
          }
          
          // Check if all teams have answered this question already
          var totalActiveTeams = GetActiveTeamCount(gameId);
          var submittedCount = await _dbContext.Answers
            .Where(a => a.GameId == gameId && a.QuestionId == game.CurrentQuestionId)
            .Select(a => a.TeamId)
            .Distinct()
            .CountAsync();
            
          _logger.LogInformation("Game {GameId}: {SubmittedCount}/{TotalActiveTeams} teams have submitted answers", 
            gameId, submittedCount, totalActiveTeams);
            
          if (submittedCount >= totalActiveTeams && totalActiveTeams > 0)
          {
            // All teams have answered, so this question should be in reveal state
            _logger.LogInformation("All teams have answered in game {GameId}, sending DisplayAnswer for question {QuestionId}",
              gameId, game.CurrentQuestionId);
              
            await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);
            
            // Only report ready status if they have EXPLICITLY signaled ready by clicking the button
            // Don't auto-mark teams as ready or automatically enable/disable buttons
            bool hasExplicitlySignaledReady = false;
            lock (_teamsReadyForNext)
            {
              hasExplicitlySignaledReady = _teamsReadyForNext.ContainsKey(gameId) && _teamsReadyForNext[gameId].Contains(teamId);
            }
            
            _logger.LogInformation("Team {TeamId} in game {GameId} has explicitly signaled ready: {HasSignaledReady}",
              teamId, gameId, hasExplicitlySignaledReady);
              
            // Only send this if they've actually clicked the button
            if (hasExplicitlySignaledReady)
            {
              _logger.LogInformation("Sending TeamSignaledReady to team {TeamId} as they previously clicked the ready button", teamId);
              // Let the client know they've already signaled they're ready
              await Clients.Caller.SendAsync("TeamSignaledReady", teamId);
            }
          }
          else if (hasAnswered)
          {
            // This team has answered but not all teams have - they should still see the answer
            _logger.LogInformation("Team {TeamId} has answered but waiting for other teams in game {GameId}", 
              teamId, gameId);
              
            await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in RequestGameState for game {GameId}, team {TeamId}", gameId, teamId);
      }
    }
  }
}