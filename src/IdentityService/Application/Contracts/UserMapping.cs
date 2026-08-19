using IdentityService.Domain;

namespace IdentityService.Application.Contracts;

/// <summary>
/// Conversão de entidades de domínio para DTOs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que aqui não há AutoMapper.</b> O projeto usava AutoMapper para
/// exatamente dois mapeamentos triviais. A biblioteca foi removida por três
/// motivos concretos:
/// </para>
/// <list type="number">
///   <item><description>
///   <b>Segurança.</b> A versão em uso (13.0.1) tinha uma vulnerabilidade de
///   severidade alta em aberto (GHSA-rvv3-g6hj-g44x).
///   </description></item>
///   <item><description>
///   <b>Licenciamento.</b> A partir da versão 14 o AutoMapper passou a exigir
///   licença comercial acima de um teto de faturamento — uma decisão que
///   precisaria ser levada à área jurídica antes de qualquer atualização.
///   </description></item>
///   <item><description>
///   <b>Custo maior que o benefício.</b> Mapeamento por reflexão troca um erro
///   de compilação por um erro em tempo de execução: renomear uma propriedade
///   do DTO continua compilando e passa a devolver <c>null</c> em produção.
///   O método abaixo, além de mais rápido, quebra o build no instante em que
///   os tipos divergem.
///   </description></item>
/// </list>
/// <para>
/// Isso não significa que AutoMapper nunca se justifique — em bases com
/// dezenas de mapeamentos profundos ele paga o próprio custo. Com dois
/// mapeamentos de quatro campos, não paga.
/// </para>
/// </remarks>
public static class UserMapping
{
    /// <summary>Projeta a entidade de domínio no DTO exposto pela API.</summary>
    public static UserDto ToDto(this User user)
    {
        return new UserDto(user.Id, user.Name, user.Email, user.CreatedAtUtc);
    }

    /// <summary>Projeta uma coleção de entidades.</summary>
    public static IReadOnlyCollection<UserDto> ToDtos(this IEnumerable<User> users)
    {
        return [.. users.Select(ToDto)];
    }
}
