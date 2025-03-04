using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using TriviaApp.Models;
using TriviaApp.Hubs;


namespace TriviaApp.Services{

 public class QuestionService
 {
  private readonly TriviaDbContext _context;

  public QuestionService(TriviaDbContext context)
  {
   _context = context;
  }

  public async Task<List<Question>> GetQuestionsAsync(int count)
  {
   return await _context.Questions.OrderBy(q => Guid.NewGuid()).Take(count).ToListAsync();
  }
 }
}