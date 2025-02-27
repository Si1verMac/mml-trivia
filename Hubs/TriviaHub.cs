using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

public class TriviaHub : Hub
{
 public async Task JoinGame(string gameId)
 {
  await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
 }
}