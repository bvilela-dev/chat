using BuildingBlocks.Contracts.Grpc;
using Grpc.Core;
using IdentityService.Application;
using MediatR;

namespace IdentityService.API.Grpc;

public sealed class UserValidationGrpcService(ISender sender) : UserValidationGrpc.UserValidationGrpcBase
{
    public override async Task<ValidateUserResponse> ValidateUser(ValidateUserRequest request, ServerCallContext context)
    {
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