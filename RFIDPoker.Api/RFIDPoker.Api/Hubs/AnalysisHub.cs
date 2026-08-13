using Microsoft.AspNetCore.SignalR;

namespace RFIDPoker.Api.Hubs;

public class AnalysisHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
