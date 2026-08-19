using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MessageService.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada pelas ferramentas de linha de comando do EF Core para criar o
/// contexto em tempo de projeto, sem subir a aplicação.
/// </summary>
public sealed class MessageDbContextFactory : IDesignTimeDbContextFactory<MessageDbContext>
{
    /// <inheritdoc />
    public MessageDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MessageDbContext>();

        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__MessageDatabase")
            ?? "Host=localhost;Port=5432;Database=chat_message;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new MessageDbContext(optionsBuilder.Options);
    }
}
