using Microsoft.AspNetCore.SignalR;
using TriviaApp.Models;
using TriviaApp.Services;

namespace TriviaApp.Hubs
{
    public class TriviaHub : Hub
    {
        private readonly GameService _gameService;
        private readonly ILogger<TriviaHub> _logger;

        public TriviaHub(GameService gameService, ILogger<TriviaHub> logger)
        {
            _gameService = gameService;
            _logger = logger;
        }

        public async Task JoinGame(int gameId, int teamId)
        {
            try
            {
                _logger.LogInformation("Team {TeamId} attempting to join game {GameId}", teamId, gameId);
                await _gameService.JoinGame(gameId, teamId);
                await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
                await Clients.Group(gameId.ToString()).SendAsync("TeamJoined", new { TeamId = teamId, GameId = gameId });
                _logger.LogInformation("Team {TeamId} joined game {GameId}", teamId, gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JoinGame for team {TeamId} in game {GameId}: {Message}", teamId, gameId, ex.Message);
                await Clients.Caller.SendAsync("Error", ex.Message);
                throw;
            }
        }

        public async Task StartGame(int gameId)
        {
            try
            {
                var game = await _gameService.StartGame(gameId);
                await Clients.Group(game.Id.ToString()).SendAsync("GameStarted", game);
                _logger.LogInformation("Game {GameId} started", game.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting game {GameId}", gameId);
                throw;
            }
        }

        public async Task SubmitWager(int gameId, int teamId, int wagerValue, int questionNumber)
        {
            try
            {
                // Call the fixed GameService method to store the wager
                await _gameService.SubmitWager(gameId, teamId, wagerValue, questionNumber);

                // Notify all clients that the wager has been placed
                await Clients.Group(gameId.ToString())
                    .SendAsync("WagerPlaced", teamId, wagerValue, questionNumber);

                // If all teams have submitted wagers, send the next question
                if (await _gameService.AllTeamsWagered(gameId, questionNumber))
                {
                    var question = await _gameService.GetCurrentQuestion(gameId, questionNumber);
                    await Clients.Group(gameId.ToString())
                        .SendAsync("Question", question);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting wager for team {TeamId} in game {GameId}", teamId, gameId);
                await Clients.Caller.SendAsync("Error", ex.Message);
                throw;
            }
        }
    }
}