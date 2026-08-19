using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers;

/// <summary>
/// Endpoint de descoberta que descreve as rotas expostas pelo gateway.
/// </summary>
/// <remarks>
/// Útil para diagnóstico rápido ("o gateway está no ar e roteando o quê?") sem
/// precisar abrir a configuração. Anônimo de propósito: não revela nada além do
/// desenho de rotas, que já é visível para qualquer cliente do frontend.
/// </remarks>
[ApiController]
[Route("api/gateway")]
[Produces("application/json")]
public sealed class GatewayController : ControllerBase
{
    /// <summary>Descreve o gateway e suas rotas.</summary>
    [HttpGet("info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
                "/ws/chat/{**catch-all}"
            }
        });
    }
}
