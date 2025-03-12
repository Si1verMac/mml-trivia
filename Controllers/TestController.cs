using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TriviaApp.Controllers    // Adjust the namespace to match your project
{
 //[Authorize(Policy = "OperatorPolicy")]

 [Route("api/[controller]")]

 [ApiController]

 public class TestController : ControllerBase
 {
  [HttpGet("test")]
  public IActionResult Get()
  {
   return Ok("Authorized");
  }
 }
}