using Microsoft.AspNetCore.Mvc;
using ScriptRunner.Core;
using System.Collections.Concurrent;

namespace ScriptRunner.Service.Controllers;

[ApiController]
[Route("api/cancel")] 
public class CancelController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> Tokens = new();

    // Called by execute path to register CTS
    public static CancellationToken Register(Guid execId)
    {
        var cts = new CancellationTokenSource();
        Tokens[execId] = cts;
        return cts.Token;
    }

    public static void Complete(Guid execId)
    {
        Tokens.TryRemove(execId, out _);
    }

    [HttpPost("{executionId}")]
    public IActionResult Cancel(Guid executionId)
    {
        if (Tokens.TryGetValue(executionId, out var cts))
        {
            cts.Cancel();
            return Accepted();
        }
        return NotFound();
    }
}
