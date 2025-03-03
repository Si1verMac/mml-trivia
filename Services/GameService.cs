using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TriviaApp.Models;

namespace TriviaApp.Services
{
    public class GameService
    {
        private readonly QuestionService _questionService;
        private readonly TriviaDbContext _context;
        private readonly ILogger<GameService> _logger;

        public GameService(QuestionService questionService, TriviaDbContext context, ILogger<GameService> logger)
        {
            _questionService = questionService;
            _context = context;
            _logger = logger;
        }

        // Retrieve a game by ID
        public async Task<Game> GetGame(int gameId)
        {
            return await _context.Games
                .Include(g => g.GameTeams).ThenInclude(gt => gt.Team)
                .Include(g => g.GameQuestions).ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId)
                ?? throw new Exception($"Game {gameId} not found.");
        }

        // Start a new game
        public async Task<int> StartGameAsync(List<int> teamIds)
        {
            if (teamIds == null || !teamIds.Any())
                throw new ArgumentException("TeamIds cannot be null or empty.");

            var teams = await _context.Teams
                .Where(t => teamIds.Contains(t.Id))
                .ToListAsync();
            if (teams.Count != teamIds.Count)
                throw new Exception("Some teams not found in the database.");

            var questions = await _questionService.GetQuestionsAsync(2);
            if (questions.Count < 2)
                throw new Exception("Not enough questions available.");

            var game = new Game
            {
                Status = "Active",
                GameTeams = teams.Select(team => new GameTeam
                {
                    Team = team
                }).ToList(),
                GameQuestions = questions.Select((q, index) => new GameQuestion
                {
                    Question = q,
                    Order = index + 1
                }).ToList()
            };

            _context.Games.Add(game);
            await _context.SaveChangesAsync();
            return game.Id;
        }

        // Start an existing game
        public async Task<Game> StartGame(int gameId)
        {
            var game = await _context.Games
                .Include(g => g.GameQuestions)
                .FirstOrDefaultAsync(g => g.Id == gameId)
                ?? throw new Exception($"Game {gameId} not found.");

            if (!game.GameQuestions.Any())
            {
                var questions = await _questionService.GetQuestionsAsync(2);
                if (questions.Count < 2)
                    throw new Exception("Not enough questions available.");
                game.GameQuestions = questions.Select((q, index) => new GameQuestion
                {
                    Question = q,
                    Order = index + 1
                }).ToList();
            }

            game.Status = "Active";
            await _context.SaveChangesAsync();
            return game;
        }

        // Join a game
        public async Task JoinGame(int gameId, int teamId)
        {
            var game = await _context.Games
                .Include(g => g.GameTeams)
                .FirstOrDefaultAsync(g => g.Id == gameId)
                ?? throw new Exception($"Game {gameId} not found.");

            if (game.GameTeams.Any(gt => gt.TeamId == teamId))
            {
                _logger.LogInformation("Team {TeamId} already joined game {GameId}", teamId, gameId);
                return;
            }

            var team = await _context.Teams.FindAsync(teamId)
                ?? throw new Exception($"Team {teamId} not found.");

            var gameTeam = new GameTeam
            {
                Game = game,
                Team = team
            };

            game.GameTeams.Add(gameTeam);
            await _context.SaveChangesAsync();
        }

        // Submit a wager (Stores wager in Answer table)
        public async Task SubmitWager(int gameId, int teamId, int wagerValue, int questionNumber)
        {
            try
            {
                var answer = await _context.Answers
                    .FirstOrDefaultAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == questionNumber);

                if (answer == null)
                {
                    answer = new Answer
                    {
                        GameId = gameId,
                        TeamId = teamId,
                        QuestionId = questionNumber,
                        Wager = wagerValue,
                        SubmittedAt = DateTime.UtcNow
                    };
                    _context.Answers.Add(answer);
                }
                else
                {
                    answer.Wager = wagerValue;
                    answer.SubmittedAt = DateTime.UtcNow;
                    _context.Answers.Update(answer);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving wager for team {teamId} in game {gameId}: {ex.Message}", ex);
            }
        }

        // Check if all teams have submitted a wager
        public async Task<bool> AllTeamsWagered(int gameId, int questionNumber)
        {
            var game = await _context.Games
                .Include(g => g.GameTeams)
                .FirstOrDefaultAsync(g => g.Id == gameId)
                ?? throw new Exception($"Game {gameId} not found.");

            var allTeams = game.GameTeams.Select(gt => gt.TeamId).ToList();
            var teamsThatWagered = await _context.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == questionNumber && a.Wager.HasValue)
                .Select(a => a.TeamId)
                .ToListAsync();

            return allTeams.All(teamId => teamsThatWagered.Contains(teamId));
        }

        // Get the current question for a game
        public async Task<Question> GetCurrentQuestion(int gameId, int questionNumber)
        {
            var game = await _context.Games
                .Include(g => g.GameQuestions).ThenInclude(gq => gq.Question)
                .FirstOrDefaultAsync(g => g.Id == gameId)
                ?? throw new Exception($"Game {gameId} not found.");

            var gameQuestion = game.GameQuestions
                .FirstOrDefault(gq => gq.Order == questionNumber)
                ?? throw new Exception($"Question {questionNumber} not found in game {gameId}.");

            return gameQuestion.Question;
        }
    }
}