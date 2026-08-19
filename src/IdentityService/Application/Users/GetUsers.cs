using IdentityService.Application.Abstractions;
using IdentityService.Application.Contracts;
using MediatR;

namespace IdentityService.Application.Users;

/// <summary>Query que lista os usuários disponíveis para iniciar conversa.</summary>
/// <remarks>
/// <para>
/// Este é o "diretório de contatos" do produto. Numa aplicação real com muitos
/// usuários, listar todo mundo não escala e nem é desejável do ponto de vista de
/// privacidade — o caminho seria busca paginada por nome/e-mail, ou uma lista de
/// contatos explícita. Aqui a listagem completa é uma simplificação consciente
/// do escopo de demonstração.
/// </para>
/// <para>
/// Note que a query <b>não</b> devolve <c>PasswordHash</c>: o
/// <see cref="UserMapping"/> projeta apenas os campos públicos.
/// </para>
/// </remarks>
public sealed record GetUsersQuery : IRequest<IReadOnlyCollection<UserDto>>;

/// <summary>Query que busca um usuário específico.</summary>
public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto?>;

/// <summary>Lista todos os usuários.</summary>
public sealed class GetUsersQueryHandler(IUserRepository userRepository)
    : IRequestHandler<GetUsersQuery, IReadOnlyCollection<UserDto>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await userRepository.GetAllAsync(cancellationToken);
        return users.ToDtos();
    }
}

/// <summary>Busca um usuário pelo identificador.</summary>
public sealed class GetUserByIdQueryHandler(IUserRepository userRepository)
    : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    /// <inheritdoc />
    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);

        // Devolve null em vez de lançar NotFoundException: "não encontrado" é um
        // resultado esperado de uma busca, não uma condição excepcional. Quem
        // traduz isso para 404 é o controller, que é a camada que fala HTTP.
        return user?.ToDto();
    }
}
