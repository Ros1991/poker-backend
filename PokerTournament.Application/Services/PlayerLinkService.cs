using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Services;

/// <summary>
/// Jogador vinculado à conta pelo e-mail (SPEC-20261029-1725, D10/D11): <see cref="Person.UserId"/> liga o jogador de
/// um home game à conta cujo e-mail CONFIRMADO é igual ao e-mail do cadastro. Só o e-mail é chave (sem CPF nem
/// telefone) e a conta é o hub entre home games — nada de tabela de perfil.
/// <para>
/// Regra única de comparação: <c>Trim().ToLowerInvariant()</c> dos dois lados (o e-mail da conta já é gravado assim
/// desde a SPEC-20261023-1201; o do jogador é gravado como digitado, só sem espaços nas pontas) — a mesma do backfill
/// da migration <c>PersonUserId</c> (<c>lower(btrim(..))</c>). Conta que pode receber vínculo
/// (<see cref="Linkable"/>): e-mail confirmado e ativa (a anonimizada é inativa e tem o e-mail marcador
/// <c>@excluida.invalid</c>). Com duas contas antigas que só diferem por maiúsculas, vale a mais antiga.
/// </para>
/// <para>
/// Quando recalcula: ao cadastrar um jogador e ao trocar o e-mail dele (<see cref="RelinkPersonAsync"/>, em todo caminho:
/// /Persons e cadastro pelo torneio no painel e no terminal); ao confirmar a conta — link do e-mail, Google (conta nova
/// ou posse de conta não confirmada), confirmação manual do Admin, redefinição de senha que confirma, reativação e troca
/// de e-mail pela Administração — (<see cref="LinkUserAsync"/>, todos os jogadores com aquele e-mail, em qualquer home
/// game, que ainda não têm conta). "Vincular membro ↔ jogador" grava direto (HomeGamesController). Conta excluída
/// (anonimizada) solta os seus (<see cref="UnlinkUserAsync"/>). Nada aqui desfaz vínculo por troca do e-mail da conta.
/// </para>
/// Estático (como <see cref="HomeGameAccess"/>): quem chama passa o próprio contexto, dentro da própria transação.
/// </summary>
public static class PlayerLinkService
{
    /// <summary>Marcador do e-mail da conta excluída (AuthService.DeleteAccountAsync).</summary>
    public const string DeletedAccountEmailSuffix = "@excluida.invalid";

    /// <summary>E-mail na forma de comparação (sem espaços nas pontas, minúsculo); vazio → null.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var normalized = UserEmail.Normalize(email);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>Contas que podem receber vínculo: e-mail confirmado, ativa e não anonimizada.</summary>
    public static IQueryable<User> Linkable(IQueryable<User> users) =>
        users.Where(u => u.IsActive && u.EmailConfirmedAt != null && !u.Email.EndsWith(DeletedAccountEmailSuffix));

    /// <summary>
    /// Outro jogador (não excluído) do home game já tem este e-mail, comparado na forma do vínculo (sem distinguir
    /// maiúsculas nem espaços nas pontas): o e-mail é a chave da conta, então "Ana@x.com" e "ana@x.com" no mesmo home
    /// game seriam dois cadastros da mesma pessoa.
    /// </summary>
    public static Task<bool> EmailInUseAsync(
        IAppDbContext db, Guid homeGameId, string? email, Guid? exceptPersonId, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            return Task.FromResult(false);
        var query = db.Persons.Where(p => p.HomeGameId == homeGameId && p.Email != null && p.Email.Trim().ToLower() == normalized);
        if (exceptPersonId is { } id)
            query = query.Where(p => p.Id != id);
        return query.AnyAsync(ct);
    }

    /// <summary>A conta vinculável com este e-mail (a mais antiga, se houver duas), ou null.</summary>
    public static Task<User?> FindLinkableAccountAsync(IAppDbContext db, string? email, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            return Task.FromResult<User?>(null);
        return Linkable(db.Users)
            .Where(u => u.Email.Trim().ToLower() == normalized)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Recalcula o vínculo da pessoa pelo e-mail dela: achou conta vinculável → <c>UserId</c> (e a navegação, para a
    /// resposta já sair com a foto da conta); não achou (ou sem e-mail) → null. Chamado ao cadastrar e quando o e-mail
    /// MUDA — editar outros campos não mexe, para não desfazer o "vincular membro ↔ jogador" feito com outro e-mail.
    /// Quem chama salva.
    /// </summary>
    public static async Task RelinkPersonAsync(IAppDbContext db, Person person, CancellationToken ct = default)
    {
        var account = await FindLinkableAccountAsync(db, person.Email, ct);
        person.User = account;
        person.UserId = account?.Id;
    }

    /// <summary>
    /// A conta acabou de ficar confirmada (ou voltou a valer): vincula todos os jogadores, em qualquer home game, com o
    /// e-mail dela e ainda sem conta (não rouba vínculo feito pelo "vincular membro ↔ jogador"). Excluídos (soft delete)
    /// ficam de fora. UPDATE direto no banco, dentro da transação de quem chama. Devolve quantos vinculou.
    /// </summary>
    public static Task<int> LinkUserAsync(IAppDbContext db, User user, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(user.Email);
        if (!user.IsActive || user.EmailConfirmedAt is null || normalized is null
            || normalized.EndsWith(DeletedAccountEmailSuffix, StringComparison.Ordinal))
            return Task.FromResult(0);

        var now = DateTimeOffset.UtcNow;
        var userId = user.Id;
        return db.Persons
            .Where(p => p.UserId == null && p.Email != null && p.Email.Trim().ToLower() == normalized)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.UserId, userId)
                .SetProperty(p => p.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Conta excluída (anonimizada): solta os jogadores dela em todo home game, inclusive os excluídos do cadastro, para
    /// a foto removida não continuar aparecendo. Só marca as pessoas (rastreadas): grava no SaveChanges de quem chama,
    /// junto com a anonimização. Devolve quantas soltou.
    /// </summary>
    public static async Task<int> UnlinkUserAsync(IAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var persons = await db.Persons
            .IgnoreQueryFilters()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);
        foreach (var person in persons)
        {
            person.User = null;
            person.UserId = null;
        }
        return persons.Count;
    }

    /// <summary>
    /// "Já conhecemos este jogador" (GET persons/lookup-email): nome, apelido e foto da conta vinculável com este
    /// e-mail; sem conta → <c>Found = false</c>. Quem chama confere que o usuário gerencia o home game.
    /// </summary>
    public static async Task<AccountLookupResponse> LookupAsync(IAppDbContext db, string? email, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
            return new AccountLookupResponse();
        var account = await Linkable(db.Users.AsNoTracking())
            .Where(u => u.Email.Trim().ToLower() == normalized)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new { u.FullName, u.Nickname, u.PhotoUrl })
            .FirstOrDefaultAsync(ct);
        return account is null
            ? new AccountLookupResponse()
            : new AccountLookupResponse
            {
                Found = true,
                FullName = account.FullName,
                Nickname = account.Nickname,
                PhotoUrl = account.PhotoUrl,
            };
    }
}
