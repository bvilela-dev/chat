using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityService.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada exclusivamente pelas ferramentas de linha de comando do EF Core.
/// </summary>
/// <remarks>
/// <para>
/// Comandos como <c>dotnet ef migrations add</c> precisam instanciar o
/// <see cref="IdentityDbContext"/> em tempo de projeto, sem subir a aplicação.
/// Sem esta fábrica, a ferramenta tentaria executar o <c>Program.cs</c> inteiro —
/// que exige RabbitMQ, Redis e variáveis de ambiente disponíveis só em runtime.
/// </para>
/// <para>
/// A connection string apontando para <c>localhost</c> não é um segredo vazado:
/// nenhuma consulta é executada ao gerar uma migration. O EF Core só precisa
/// saber <b>qual provedor</b> está em uso, para traduzir o modelo no dialeto SQL
/// correto.
/// </para>
/// </remarks>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();

        // Permite sobrescrever pela variável de ambiente quando o banco local
        // roda em outra porta (por exemplo, a 5433 exposta pelo docker compose).
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDatabase")
            ?? "Host=localhost;Port=5432;Database=chat_identity;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new IdentityDbContext(optionsBuilder.Options);
    }
}
