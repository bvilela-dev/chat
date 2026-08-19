using IdentityService.Application.Contracts;
using IdentityService.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

/// <summary>
/// Diretório de usuários, usado pelo frontend para montar a lista de contatos.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users")]
[Produces("application/json")]
public sealed class UsersController(ISender sender) : ControllerBase
{
    /// <summary>Lista os usuários cadastrados.</summary>
    /// <remarks>
    /// Exige autenticação: a lista de quem usa a plataforma é informação de
    /// negócio e não deve ficar acessível anonimamente.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<UserDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<UserDto>> GetAll(CancellationToken cancellationToken)
    {
        return sender.Send(new GetUsersQuery(), cancellationToken);
    }

    /// <summary>Busca um usuário pelo identificador.</summary>
    /// <response code="200">Usuário encontrado.</response>
    /// <response code="404">Nenhum usuário com este identificador.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await sender.Send(new GetUserByIdQuery(id), cancellationToken);

        // Tradução de "não encontrado" (conceito de domínio) para 404 (conceito
        // de HTTP). É exatamente esse tipo de conversão que justifica a
        // existência da camada de API.
        return user is null ? NotFound() : Ok(user);
    }
}
