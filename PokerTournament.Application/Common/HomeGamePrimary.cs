using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Common;

/// <summary>
/// Home game principal da conta (SPEC-20261029-1723, D13/D14/D18 do plano de 2026-10-29). Toda conta confirmada tem
/// um: criado junto da confirmação (cadastro pelo link, Google, confirmação manual do Admin, redefinição de senha que
/// confirma) ou pela migration <c>HomeGamePrincipal</c> (contas existentes). Nunca é excluído pela tela do home game
/// (<see cref="Services.HomeGameDeletionService"/> recusa); sai só junto com a conta (<c>DELETE me</c>).
///
/// No Mesa e no Clube ele é o único e a interface não fala em "home game": a conta tem torneios, estruturas e
/// jogadores direto (no Clube também rankings, membros e o "nome do clube" — o <see cref="HomeGame.Name"/>). Só a Liga
/// cria outros (até 3 com o principal; ver <see cref="Services.PlanService.EnsureCanCreateHomeGameAsync"/>). Por baixo
/// nada muda: torneios, jogadores, rankings, limites e o filtro de tenant continuam pendurados no home game.
/// </summary>
public static class HomeGamePrimary
{
    /// <summary>Índice único parcial (owner_id) WHERE is_primary AND deleted_at IS NULL.</summary>
    public const string UniqueIndex = "ix_home_games_owner_primary";

    /// <summary>Recusa do DELETE home-games/{id} (e do resumo da exclusão) para o principal.</summary>
    public const string NotDeletableMessage = "O home game principal da conta não pode ser excluído.";

    /// <summary>Começo do nome padrão: "Clube de " + nome da conta.</summary>
    public const string DefaultNamePrefix = "Clube de ";

    /// <summary>Nome padrão quando a conta não tem nome (não deveria acontecer: o nome é obrigatório).</summary>
    public const string FallbackName = "Meu clube";

    private const int MaxNameLength = 200;

    /// <summary>
    /// "Clube de &lt;nome da conta&gt;", cortado em 200 caracteres. A migration HomeGamePrincipal grava os principais novos
    /// com a MESMA regra em SQL (<c>'Clube de ' || left(btrim(full_name), 191)</c>; "Meu clube" sem nome). Se a regra
    /// mudar aqui, os nomes que a migration já gravou deixam de contar como padrão no <see cref="IsDefaultName"/>.
    /// </summary>
    public static string DefaultName(string? ownerFullName)
    {
        var name = (ownerFullName ?? string.Empty).Trim();
        if (name.Length == 0)
            return FallbackName;
        var max = MaxNameLength - DefaultNamePrefix.Length;
        return DefaultNamePrefix + (name.Length > max ? name[..max] : name);
    }

    /// <summary>O nome ainda é o padrão "Clube de &lt;nome da conta&gt;" (a dica "Dê um nome ao seu clube" usa isso).</summary>
    public static bool IsDefaultName(string? name, string? ownerFullName) =>
        name is not null && string.Equals(name, DefaultName(ownerFullName), StringComparison.Ordinal);

    /// <summary>
    /// Nome que a interface mostra: null no principal de dono Mesa (no Mesa não existe nome de clube — quem precisa de
    /// um rótulo usa o nome da conta); senão o próprio nome (nome do clube no Clube/Liga; os home games extras, inclusive
    /// os de uma ex-Liga rebaixada, continuam com o nome deles).
    /// </summary>
    public static string? DisplayNameFor(bool isPrimary, string ownerEffectivePlan, string name) =>
        isPrimary && ownerEffectivePlan == PlanCatalog.Mesa ? null : name;

    /// <summary>
    /// Rótulo do home game na lista de torneios do pareamento da TV: o nome da conta no principal de dono Mesa, senão o
    /// nome do home game (o nome do clube).
    /// </summary>
    public static string LabelFor(bool isPrimary, string ownerEffectivePlan, string name, string? ownerName) =>
        DisplayNameFor(isPrimary, ownerEffectivePlan, name)
        ?? (string.IsNullOrWhiteSpace(ownerName) ? name : ownerName.Trim());

    /// <summary>
    /// Preenche <c>displayName</c> e <c>isNameDefault</c> das respostas. Chamar DEPOIS do
    /// <see cref="Services.PlanService.ApplyOwnerPlanAsync(IEnumerable{HomeGameResponse}, CancellationToken)"/> (usa o
    /// <c>ownerPlan</c>). Uma consulta pelos nomes dos donos dos principais.
    /// </summary>
    public static async Task ApplyAsync(IAppDbContext db, IReadOnlyCollection<HomeGameResponse> responses, CancellationToken ct)
    {
        var ownerIds = responses.Where(r => r.IsPrimary).Select(r => r.OwnerId).Distinct().ToList();
        var ownerNames = ownerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => ownerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        foreach (var response in responses)
        {
            response.IsNameDefault = response.IsPrimary
                && ownerNames.TryGetValue(response.OwnerId, out var ownerName)
                && IsDefaultName(response.Name, ownerName);
            response.DisplayName = DisplayNameFor(response.IsPrimary, response.OwnerPlan, response.Name);
        }
    }

    /// <summary>Id do principal da conta (não excluído), ou null.</summary>
    public static Task<Guid?> FindIdAsync(IAppDbContext db, Guid ownerId, CancellationToken ct) =>
        db.HomeGames.AsNoTracking()
            .Where(h => h.OwnerId == ownerId && h.IsPrimary)
            .Select(h => (Guid?)h.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Garante o principal da conta que acabou de ser confirmada: se ela ainda não tem um, ADICIONA ao contexto um home
    /// game "Clube de &lt;nome&gt;" (dono = a conta, <c>IsPrimary</c>) — quem chama grava no mesmo SaveChanges da
    /// confirmação, dentro da transação dele. Idempotente (conta com principal não ganha outro; o índice
    /// <see cref="UniqueIndex"/> segura a corrida). Nome já usado por outro home game da conta → "Clube de X (2)".
    /// <paramref name="user"/> precisa ter Id (conta já gravada). Devolve o home game novo, ou null se já havia principal.
    /// </summary>
    public static async Task<HomeGame?> EnsureForAccountAsync(IAppDbContext db, User user, CancellationToken ct)
    {
        if (user.Id == Guid.Empty)
            throw new InvalidOperationException("O principal só pode ser criado para uma conta já gravada.");
        if (await db.HomeGames.AnyAsync(h => h.OwnerId == user.Id && h.IsPrimary, ct))
            return null;
        // Já adicionado neste mesmo contexto (dois caminhos de confirmação na mesma requisição)
        var pending = db.HomeGames.Local.FirstOrDefault(h => h.OwnerId == user.Id && h.IsPrimary);
        if (pending is not null)
            return null;

        var baseName = DefaultName(user.FullName);
        var name = baseName;
        for (var n = 2; await db.HomeGames.AnyAsync(h => h.OwnerId == user.Id && h.Name == name, ct); n++)
        {
            var suffix = $" ({n})";
            name = (baseName.Length + suffix.Length > MaxNameLength ? baseName[..(MaxNameLength - suffix.Length)] : baseName) + suffix;
        }

        var homeGame = new HomeGame
        {
            OwnerId = user.Id,
            Name = name,
            IsPrimary = true,
            IsActive = true,
        };
        db.HomeGames.Add(homeGame);
        return homeGame;
    }
}
