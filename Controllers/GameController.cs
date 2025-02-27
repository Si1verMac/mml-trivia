using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class GameController : ControllerBase
{
 private readonly GameService _gameService;

 public GameController(GameService gameService)
 {
  _gameService = gameService;
 }

 [HttpPost("start")]
 [Authorize]
 public async Task<IActionResult> StartGame([FromBody] StartGameDto dto)
 {
  try
  {
   if (dto?.TeamIds == null || !dto.TeamIds.Any())
   {
    Console.WriteLine("TeamIds is null or empty.");
    return BadRequest(new { error = "TeamIds are required." });
   }
   Console.WriteLine($"Starting game with TeamIds: {string.Join(", ", dto.TeamIds)}");
   var gameId = await _gameService.StartGameAsync(dto.TeamIds);
   Console.WriteLine($"Game started with ID: {gameId}");
   return Ok(new { gameId });
  }
  catch (Exception ex)
  {
   Console.WriteLine($"Error starting game: {ex.Message}");
   return StatusCode(500, new { error = ex.Message });
  }
 }

 public class StartGameDto
 {
  public List<int> TeamIds { get; set; }
 }
}