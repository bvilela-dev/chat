using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers;

[ApiController]
[Route("api/gateway")]
public sealed class GatewayController : ControllerBase
{
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            name = "chat-api-gateway",
            routes = new[]
            {
                "/identity/{**catch-all}",
                "/messages/{**catch-all}",
                "/presence/{**catch-all}",
                "/notifications/{**catch-all}",
                "/chat/{**catch-all}"
            }
        });
    }
}