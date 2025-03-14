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
        private readonly IHubContext<TriviaHub> _hubContext;

        public AnswerService(TriviaDbContext context, IHubContext<TriviaHub> hubContext, ILogger<AnswerService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _hubContext = hubContext;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            Console.WriteLine(question.CorrectAnswer.Trim(new Char[] { ' ', '"', '.' }) + " Question Correct Answer>>>>>>>>>>>>>>>>>>>>");
            Console.WriteLine(selectedAnswer.Trim(new Char[] { ' ', '"', '.' }) + " Selected Answer>>>>>>>>>>>>>>");

            bool isCorrect = selectedAnswer != null && selectedAnswer.Trim(new Char[] { ' ', '"', '.' }).Equals(question.CorrectAnswer.Trim(new Char[] { ' ', '"', '.' }), StringComparison.OrdinalIgnoreCase);

            // Calculate score change based on wager
            int scoreChange = isCorrect ? wager ?? 0 : -(wager ?? 0); // Add wager if correct, subtract if incorrect

            // Save or update answer
            var answer = await _context.Answers.FirstOrDefaultAsync(a => a.GameId == gameId && a.TeamId == teamId && a.QuestionId == questionId);
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

            // Update team score
            var gameTeam = await _context.GameTeams.FirstOrDefaultAsync(gt => gt.GameId == gameId && gt.TeamId == teamId);
            if (gameTeam != null)
            {
                gameTeam.Score += scoreChange;
                _context.GameTeams.Update(gameTeam);
            }

            await _context.SaveChangesAsync();

            // Fetch and broadcast updated scores
            // var scores = await _context.GameTeams
            //     .Where(gt => gt.GameId == gameId)
            //     .Select(gt => new { gt.TeamId, gt.Score })
            //     .ToListAsync();
            // await _hubContext.Clients.Group(gameId.ToString()).SendAsync("ScoresUpdated", scores);


            int totalActiveTeams = TriviaHub.GetActiveTeamCount(gameId);
            int submittedCount = await _context.Answers
                .Where(a => a.GameId == gameId && a.QuestionId == questionId)
                .Select(a => a.TeamId)
                .Distinct()
                .CountAsync();

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
    }
}