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
    private static readonly Dictionary<string, DateTime> _lastGameStateRequest = new();
    private static readonly TimeSpan _gameStateRequestThrottle = TimeSpan.FromSeconds(1);

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
        _logger.LogInformation("Processing JoinGame request for team {TeamId} in game {GameId}, connection {ConnectionId}",
            teamId, gameId, Context.ConnectionId);

        bool alreadyJoined = false;
        lock (_connectionMappings)
        {
          if (_connectionMappings.TryGetValue(Context.ConnectionId, out var mapping))
          {
            if (mapping.gameId == gameId && mapping.teamId == teamId)
            {
              _logger.LogInformation("Connection {ConnectionId} already joined for team {TeamId} in game {GameId}, skipping duplicate join",
                  Context.ConnectionId, teamId, gameId);
              alreadyJoined = true;
            }
          }
        }

        if (!alreadyJoined)
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

          await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { GameId = gameId, TeamId = teamId });
        }

        var game = await _dbContext.Games
            .Include(g => g.CurrentQuestion)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game != null)
        {
          _logger.LogInformation("Sending GameState for game {GameId} to team {TeamId}: Status={Status}",
              gameId, teamId, game.Status);

          await Clients.Caller.SendAsync("GameState", new
          {
            GameId = game.Id,
            Status = game.Status,
            Name = game.Name
          });
        }
        else
        {
          _logger.LogWarning("Game {GameId} not found during JoinGame", gameId);
          return;
        }

        if (game.Status == "InProgress" && game.CurrentQuestion != null)
        {
          bool hasAnswered = false;

          if (game.CurrentQuestionId.HasValue)
          {
            hasAnswered = await _dbContext.Answers
                .AnyAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == game.CurrentQuestionId);

            _logger.LogInformation("Team {TeamId} joining game {GameId} - has already answered current question {QuestionId}: {HasAnswered}",
                teamId, gameId, game.CurrentQuestionId, hasAnswered);

            int totalActiveTeams = GetActiveTeamCount(gameId);
            int submittedCount = await _dbContext.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == game.CurrentQuestionId)
                .Select(a => a.TeamId)
                .Distinct()
                .CountAsync();

            bool allTeamsSubmitted = submittedCount >= totalActiveTeams && totalActiveTeams > 0;

            bool sendQuestion = true;
            bool sendAnswerSubmitted = hasAnswered;
            bool sendDisplayAnswer = false;

            _logger.LogInformation("Game {GameId}, question {QuestionId}: {SubmittedCount}/{TotalActiveTeams} teams have submitted answers",
                gameId, game.CurrentQuestionId, submittedCount, totalActiveTeams);

            if (allTeamsSubmitted)
            {
              _logger.LogInformation("All teams have answered question {QuestionId} in game {GameId} - sending DisplayAnswer for reconnection",
                  game.CurrentQuestionId, gameId);
              sendDisplayAnswer = true;
              if (hasAnswered) sendQuestion = false;
            }

            if (sendQuestion)
            {
              _logger.LogInformation("Sending Question event to team {TeamId} for question {QuestionId}",
                  teamId, game.CurrentQuestionId);

              var questionData = new
              {
                Id = game.CurrentQuestion.Id,
                Text = game.CurrentQuestion.Text,
                Options = game.CurrentQuestion.Options,
                Round = game.CurrentRound,
                QuestionNumber = game.CurrentQuestionNumber,
                QuestionType = game.CurrentQuestion.Type // Added for client-side component rendering
              };

              await Clients.Caller.SendAsync("Question", questionData);
            }

            if (sendAnswerSubmitted)
            {
              _logger.LogInformation("Sending AnswerSubmitted event to team {TeamId} for question {QuestionId}",
                  teamId, game.CurrentQuestionId);
              await Clients.Caller.SendAsync("AnswerSubmitted", new { TeamId = teamId, IsCorrect = true });
            }

            if (sendDisplayAnswer)
            {
              _logger.LogInformation("Sending DisplayAnswer to team {TeamId} for question {QuestionId}, answer: {Answer}",
                  teamId, game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);
              await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);

              bool hasExplicitlySignaledReady = false;
              lock (_teamsReadyForNext)
              {
                hasExplicitlySignaledReady = _teamsReadyForNext.ContainsKey(gameId) &&
                                            _teamsReadyForNext[gameId].Contains(teamId);
              }

              if (hasExplicitlySignaledReady)
              {
                _logger.LogInformation("Team {TeamId} previously clicked ready for game {GameId}, sending TeamSignaledReady",
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
        _logger.LogInformation("Team {TeamId} submitted wager {Wager} for game {GameId}, question {QuestionId}",
            teamId, wager, gameId, questionId);
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
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting SubmitAnswer processing for team {TeamId}, game {GameId}, question {QuestionId}",
            teamId, gameId, questionId);

        var question = await _dbContext.Questions.FindAsync(questionId);
        _logger.LogInformation("Submitting answer for question type: {QuestionType}", question?.Type);

        var (isCorrect, allSubmitted, scoreChange) = await _answerService.SubmitAnswerAsync(gameId, teamId, questionId, selectedAnswer, wager);

        lock (_teamSubmissions)
        {
          if (!_teamSubmissions.ContainsKey(gameId))
            _teamSubmissions[gameId] = new Dictionary<int, string>();
          _teamSubmissions[gameId][teamId] = selectedAnswer;
        }

        await Clients.Caller.SendAsync("AnswerSubmitted", new { TeamId = teamId, IsCorrect = isCorrect, Points = scoreChange });

        if (allSubmitted && question != null)
        {
          _logger.LogInformation("All teams submitted for game {GameId}, question {QuestionId}. Sending DisplayAnswer.",
              gameId, questionId);
          await Task.Delay(500);
          await Clients.Group(gameId.ToString()).SendAsync("DisplayAnswer", questionId, question.CorrectAnswer);
          await Clients.Caller.SendAsync("DisplayAnswer", questionId, question.CorrectAnswer, isCorrect, wager, scoreChange);

          lock (_teamSubmissions)
          {
            _teamSubmissions.Remove(gameId);
          }
        }

        var scores = await _dbContext.GameTeams
            .Where(gt => gt.GameId == gameId)
            .Select(gt => new { gt.TeamId, gt.Score })
            .ToListAsync();

        _logger.LogInformation("Broadcasting updated scores to operators for game {GameId}", gameId);
        await Clients.Group("Operators").SendAsync("ScoresUpdated", gameId, scores);

        var totalTime = DateTime.UtcNow - startTime;
        _logger.LogInformation("Completed SubmitAnswer in {TotalMs}ms for team {TeamId}, game {GameId}",
            totalTime.TotalMilliseconds, teamId, gameId);
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

        // Get the current game to check if it's a halftime break
        var game = await _dbContext.Games
            .Include(g => g.CurrentQuestion)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null)
        {
          _logger.LogWarning("HandleTimerExpiry: Game {GameId} not found", gameId);
          return;
        }

        if (game.CurrentQuestion == null)
        {
          _logger.LogWarning("HandleTimerExpiry: Current question is null for game {GameId}", gameId);
          return;
        }

        string questionType = game.CurrentQuestion.Type?.ToLowerInvariant() ?? "";
        _logger.LogInformation("HandleTimerExpiry: Question type is '{QuestionType}' for game {GameId}",
            questionType, gameId);

        // Check if this is a halftime break expiry (with more robust type checking)
        bool isHalftimeBreak = questionType == "halftimebreak" ||
                              questionType == "halftime_break" ||
                              questionType == "halftime-break" ||
                              questionType == "halftime break" ||
                              questionType.Contains("halftime") && questionType.Contains("break");

        if (isHalftimeBreak)
        {
          _logger.LogInformation("HandleTimerExpiry detected halftime break for game {GameId}", gameId);

          // Mark all teams as ready to advance from halftime break
          lock (_teamsReadyForNext)
          {
            if (!_teamsReadyForNext.ContainsKey(gameId))
            {
              _teamsReadyForNext[gameId] = new HashSet<int>();
            }

            // Get all active teams for this game
            lock (_activeTeams)
            {
              if (_activeTeams.ContainsKey(gameId))
              {
                foreach (var teamId in _activeTeams[gameId])
                {
                  _teamsReadyForNext[gameId].Add(teamId);
                  _logger.LogInformation("Auto-signaling team {TeamId} as ready after halftime break expiry", teamId);
                }
              }
            }
          }

          // Notify all clients that the halftime break timer has expired
          await Clients.Group(gameId.ToString()).SendAsync("HalftimeBreakExpired");

          // Advance to next question automatically
          _logger.LogInformation("Advancing to next question after halftime break timer expiry for game {GameId}", gameId);
          await _gameService.AdvanceToNextQuestionAsync(gameId);
          return;
        }

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
        _logger.LogInformation("Teams that haven't submitted for game {GameId}: {NonSubmittedTeams}",
            gameId, string.Join(", ", nonSubmittedTeams));

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
        _logger.LogInformation("Team {TeamId} explicitly signaled ready (button clicked) for game {GameId}", teamId, gameId);

        bool needToBroadcast = false;
        lock (_teamsReadyForNext)
        {
          if (!_teamsReadyForNext.ContainsKey(gameId))
            _teamsReadyForNext[gameId] = new HashSet<int>();

          if (!_teamsReadyForNext[gameId].Contains(teamId))
          {
            _teamsReadyForNext[gameId].Add(teamId);
            _logger.LogInformation("Team {TeamId} signaled ready for game {GameId}. Ready teams: {ReadyCount}/{TotalActive}",
                teamId, gameId, _teamsReadyForNext[gameId].Count, GetActiveTeamCount(gameId));
            needToBroadcast = true;
          }
          else
          {
            _logger.LogInformation("Team {TeamId} already signaled ready for game {GameId}, ignoring duplicate", teamId, gameId);
          }
        }

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

      lock (_lastGameStateRequest)
      {
        _lastGameStateRequest.Remove(Context.ConnectionId);
      }

      await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinOperatorGroup()
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, "Operators");
      _logger.LogInformation("Added connection {ConnectionId} to Operators group", Context.ConnectionId);
      await Clients.Caller.SendAsync("JoinedOperatorGroup", "You have joined the Operators group.");
    }

    public async Task RequestNextQuestion(int gameId)
    {
      try
      {
        var connection = Context.ConnectionId;
        if (!_connectionMappings.ContainsKey(connection))
        {
          _logger.LogWarning("Connection {ConnectionId} tried to request next question without joining a game", connection);
          return;
        }

        var (_, teamId) = _connectionMappings[connection];
        var team = await _dbContext.Teams.FindAsync(teamId);
        if (team == null || !team.IsOperator)
        {
          _logger.LogWarning("Non-operator team {TeamId} tried to request next question for game {GameId}", teamId, gameId);
          return;
        }

        _logger.LogInformation("Operator {TeamId} requested to force advance to next question in game {GameId}", teamId, gameId);

        var game = await _dbContext.Games
            .Include(g => g.GameQuestions)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null)
        {
          _logger.LogWarning("Game {GameId} not found for RequestNextQuestion", gameId);
          return;
        }

        if (game.CurrentQuestionId.HasValue)
        {
          var currentGameQuestion = game.GameQuestions
              .FirstOrDefault(gq => gq.QuestionId == game.CurrentQuestionId);

          if (currentGameQuestion != null && !currentGameQuestion.IsAnswered)
          {
            _logger.LogInformation("Marking current question {QuestionId} as answered by operator request",
                game.CurrentQuestionId);
            currentGameQuestion.IsAnswered = true;
            await _dbContext.SaveChangesAsync();
          }
        }

        _logger.LogInformation("Advancing to next question for game {GameId} by operator request", gameId);
        await _gameService.AdvanceToNextQuestionAsync(gameId);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in RequestNextQuestion for game {GameId}", gameId);
      }
    }

    public async Task RequestGameState(int gameId, int teamId)
    {
      try
      {
        bool shouldProcess = true;
        lock (_lastGameStateRequest)
        {
          if (_lastGameStateRequest.TryGetValue(Context.ConnectionId, out var lastRequest))
          {
            if (DateTime.UtcNow - lastRequest < _gameStateRequestThrottle)
            {
              _logger.LogInformation("Throttling RequestGameState for team {TeamId} in game {GameId} - too many requests",
                  teamId, gameId);
              shouldProcess = false;
            }
          }
          _lastGameStateRequest[Context.ConnectionId] = DateTime.UtcNow;
        }

        if (!shouldProcess)
        {
          return;
        }

        _logger.LogInformation("Team {TeamId} requested current game state for game {GameId}", teamId, gameId);

        var game = await _dbContext.Games
            .Include(g => g.CurrentQuestion)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null)
        {
          _logger.LogWarning("Game {GameId} not found when requesting state", gameId);
          return;
        }

        _logger.LogInformation("Sending GameState for game {GameId} to team {TeamId}: Status={Status}",
            gameId, teamId, game.Status);

        await Clients.Caller.SendAsync("GameState", new
        {
          GameId = game.Id,
          Status = game.Status,
          Name = game.Name
        });

        if (game.Status == "InProgress" && game.CurrentQuestion != null)
        {
          _logger.LogInformation("Game {GameId} is in progress with question {QuestionId}",
              gameId, game.CurrentQuestionId);

          var hasAnswered = await _dbContext.Answers
              .AnyAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == game.CurrentQuestionId);

          _logger.LogInformation("Team {TeamId} in game {GameId} has answered current question {QuestionId}: {HasAnswered}",
              teamId, gameId, game.CurrentQuestionId, hasAnswered);

          var totalActiveTeams = GetActiveTeamCount(gameId);
          var submittedCount = await _dbContext.Answers
              .Where(a => a.GameId == gameId && a.QuestionId == game.CurrentQuestionId)
              .Select(a => a.TeamId)
              .Distinct()
              .CountAsync();

          _logger.LogInformation("Game {GameId}: {SubmittedCount}/{TotalActiveTeams} teams have submitted answers",
              gameId, submittedCount, totalActiveTeams);

          bool allTeamsSubmitted = submittedCount >= totalActiveTeams && totalActiveTeams > 0;
          bool sendQuestion = true;
          bool sendAnswerSubmitted = hasAnswered;
          bool sendDisplayAnswer = false;

          if (allTeamsSubmitted)
          {
            _logger.LogInformation("All teams have answered question {QuestionId} in game {GameId} - sending DisplayAnswer for state request",
                game.CurrentQuestionId, gameId);
            sendDisplayAnswer = true;
            if (hasAnswered) sendQuestion = false;
          }

          if (sendQuestion)
          {
            var questionData = new
            {
              Id = game.CurrentQuestion.Id,
              Text = game.CurrentQuestion.Text,
              Options = game.CurrentQuestion.Options,
              Round = game.CurrentRound,
              QuestionNumber = game.CurrentQuestionNumber,
              QuestionType = game.CurrentQuestion.Type // Added for client-side component rendering
            };

            _logger.LogInformation("Sending Question data to team {TeamId} for question {QuestionId}",
                teamId, game.CurrentQuestionId);
            await Clients.Caller.SendAsync("Question", questionData);
          }

          if (sendAnswerSubmitted)
          {
            _logger.LogInformation("Sending AnswerSubmitted event to team {TeamId} in response to state request", teamId);
            await Clients.Caller.SendAsync("AnswerSubmitted", new { TeamId = teamId, IsCorrect = true });
          }

          if (sendDisplayAnswer)
          {
            _logger.LogInformation("Sending DisplayAnswer to team {TeamId} for question {QuestionId}",
                teamId, game.CurrentQuestionId);
            await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);

            bool hasExplicitlySignaledReady = false;
            lock (_teamsReadyForNext)
            {
              hasExplicitlySignaledReady = _teamsReadyForNext.ContainsKey(gameId) &&
                                          _teamsReadyForNext[gameId].Contains(teamId);
            }

            if (hasExplicitlySignaledReady)
            {
              _logger.LogInformation("Team {TeamId} previously signaled ready - sending TeamSignaledReady event", teamId);
              await Clients.Caller.SendAsync("TeamSignaledReady", teamId);
            }
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in RequestGameState for game {GameId}, team {TeamId}", gameId, teamId);
      }
    }

    public async Task JoinGameSilently(int gameId, int teamId)
    {
      try
      {
        _logger.LogInformation("Processing JoinGameSilently request for team {TeamId} in game {GameId}, connection {ConnectionId}",
            teamId, gameId, Context.ConnectionId);

        bool alreadyJoined = false;
        lock (_connectionMappings)
        {
          if (_connectionMappings.TryGetValue(Context.ConnectionId, out var mapping))
          {
            if (mapping.gameId == gameId && mapping.teamId == teamId)
            {
              _logger.LogInformation("Connection {ConnectionId} already joined for team {TeamId} in game {GameId}, skipping duplicate join",
                  Context.ConnectionId, teamId, gameId);
              alreadyJoined = true;
            }
          }
        }

        if (!alreadyJoined)
        {
          await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
          _logger.LogInformation("Team {TeamId} joined game {GameId} silently, ConnectionId: {ConnectionId}",
              teamId, gameId, Context.ConnectionId);

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

          // When joining silently (e.g., toggling from operator view), don't broadcast to other teams
          // await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { GameId = gameId, TeamId = teamId });
        }

        // Always send the full game state to the client during silent join,
        // as this is likely a toggle between views
        var game = await _dbContext.Games
            .Include(g => g.CurrentQuestion)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game != null)
        {
          _logger.LogInformation("Sending full GameState for silent join of team {TeamId} in game {GameId}: Status={Status}",
              teamId, gameId, game.Status);

          // Step 1: Send general game state
          await Clients.Caller.SendAsync("GameState", new
          {
            GameId = game.Id,
            Status = game.Status,
            Name = game.Name
          });

          // Step 2: If in progress, send current question
          if (game.Status == "InProgress" && game.CurrentQuestion != null)
          {
            bool hasAnswered = false;

            if (game.CurrentQuestionId.HasValue)
            {
              hasAnswered = await _dbContext.Answers
                  .AnyAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == game.CurrentQuestionId);

              _logger.LogInformation("Team {TeamId} silent join to game {GameId} - has already answered current question {QuestionId}: {HasAnswered}",
                  teamId, gameId, game.CurrentQuestionId, hasAnswered);

              int totalActiveTeams = GetActiveTeamCount(gameId);
              int submittedCount = await _dbContext.Answers
                  .Where(a => a.GameId == gameId && a.QuestionId == game.CurrentQuestionId)
                  .Select(a => a.TeamId)
                  .Distinct()
                  .CountAsync();

              bool allTeamsSubmitted = submittedCount >= totalActiveTeams && totalActiveTeams > 0;

              // Step 3: Send question data regardless of phase for toggle consistency
              var questionData = new
              {
                Id = game.CurrentQuestion.Id,
                Text = game.CurrentQuestion.Text,
                Options = game.CurrentQuestion.Options,
                Round = game.CurrentRound,
                QuestionNumber = game.CurrentQuestionNumber,
                QuestionType = game.CurrentQuestion.Type
              };

              await Clients.Caller.SendAsync("Question", questionData);
              _logger.LogInformation("Sent current question {QuestionId} data during silent join for team {TeamId}",
                  game.CurrentQuestionId, teamId);

              // Step 4: If team has already answered, send the submission status
              if (hasAnswered)
              {
                _logger.LogInformation("Sending AnswerSubmitted for team {TeamId} during silent join", teamId);
                await Clients.Caller.SendAsync("AnswerSubmitted", new { TeamId = teamId, IsCorrect = true });
              }

              // Step 5: If all teams have answered, send the correct answer
              if (allTeamsSubmitted || game.CurrentQuestion.Type?.ToLowerInvariant() == "halftimebreak")
              {
                _logger.LogInformation("Sending DisplayAnswer during silent join for team {TeamId}, question {QuestionId}",
                    teamId, game.CurrentQuestionId);
                await Clients.Caller.SendAsync("DisplayAnswer", game.CurrentQuestionId, game.CurrentQuestion.CorrectAnswer);

                // Step 6: Check if team has signaled ready
                bool hasExplicitlySignaledReady = false;
                lock (_teamsReadyForNext)
                {
                  hasExplicitlySignaledReady = _teamsReadyForNext.ContainsKey(gameId) &&
                                              _teamsReadyForNext[gameId].Contains(teamId);
                }

                if (hasExplicitlySignaledReady)
                {
                  _logger.LogInformation("Team {TeamId} previously ready for game {GameId}, sending TeamSignaledReady during silent join",
                      teamId, gameId);
                  await Clients.Caller.SendAsync("TeamSignaledReady", teamId);
                }
              }
            }
          }
        }
        else
        {
          _logger.LogWarning("Game {GameId} not found during JoinGameSilently", gameId);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error in JoinGameSilently for game {GameId}, team {TeamId}", gameId, teamId);
      }
    }
  }
}