using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class GameService
{
 private readonly TriviaDbContext _context;
 private readonly QuestionService _questionService;

 public GameService(TriviaDbContext context, QuestionService questionService)
 {
  _context = context;
  _questionService = questionService;
 }

 public async Task<int> StartGameAsync(List<int> teamIds)
 {
  if (teamIds == null || !teamIds.Any())
   throw new ArgumentException("TeamIds cannot be null or empty.");

  // Validate team IDs exist
  var invalidTeamIds = teamIds.Where(id => !_context.Teams.Any(t => t.Id == id)).ToList();
  if (invalidTeamIds.Any())
   throw new ArgumentException($"Invalid Team IDs: {string.Join(", ", invalidTeamIds)}");

  var game = new Game { Status = "Active", GameTeams = new List<GameTeam>() };
  foreach (var teamId in teamIds)
  {
   game.GameTeams.Add(new GameTeam { TeamId = teamId });
  }

  var questions = await _questionService.GetQuestionsAsync(5);
  if (questions == null || !questions.Any())
   throw new InvalidOperationException("No questions available.");

  game.GameQuestions = questions.Select((q, index) => new GameQuestion { QuestionId = q.Id, Order = index + 1 }).ToList();

  _context.Games.Add(game);
  await _context.SaveChangesAsync();
  return game.Id;
 }
}