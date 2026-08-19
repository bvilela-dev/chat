using BuildingBlocks.Contracts.Grpc;
using Grpc.Core;
using IdentityService.Application.Users;
using MediatR;

namespace IdentityService.API.Grpc;

/// <summary>
/// Serviço gRPC interno que permite a outros microsserviços confirmar a
/// existência de um usuário.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que gRPC e não REST.</b> Este endpoint é consumido serviço a serviço,
/// nunca pelo navegador. Nesse cenário o gRPC ganha em três pontos: contrato
/// definido em <c>.proto</c> e verificado em tempo de compilação nos dois lados;
/// serialização binária (Protobuf), bem mais compacta que JSON; e HTTP/2 com
/// multiplexação, que reaproveita a conexão.
/// </para>
/// <para>
/// <b>Quando NÃO usar.</b> Uma chamada síncrona acopla os serviços em tempo de
/// execução: se o Identity Service cair, quem depende desta chamada cai junto.
/// Por isso ela é usada apenas para <i>consulta pontual de autorização</i>, onde
/// a resposta precisa ser imediata e correta. A propagação de <i>fatos</i>
/// ("usuário foi criado") continua sendo feita por evento assíncrono, que tolera
/// indisponibilidade.
/// </para>
/// </remarks>
public sealed class UserValidationGrpcService(ISender sender) : UserValidationGrpc.UserValidationGrpcBase
{
    /// <summary>Verifica se o usuário existe e devolve seus dados públicos.</summary>
    public override async Task<ValidateUserResponse> ValidateUser(
        ValidateUserRequest request,
        ServerCallContext context)
    {
        // Identificador malformado é resposta negativa, não erro. Lançar aqui
        // geraria um status gRPC de falha e faria o cliente acionar retry para
        // algo que jamais vai mudar.
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return new ValidateUserResponse { Exists = false };
        }

        var user = await sender.Send(new GetUserByIdQuery(userId), context.CancellationToken);

        if (user is null)
        {
            return new ValidateUserResponse { Exists = false };
        }

        return new ValidateUserResponse
        {
            Exists = true,
            UserId = user.Id.ToString(),
            Name = user.Name,
            Email = user.Email
        };
    }
}
