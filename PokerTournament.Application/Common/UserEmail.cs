using Microsoft.EntityFrameworkCore;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Common;

/// <summary>
/// E-mail de conta: gravado sempre normalizado (Trim + minúsculas) e comparado sem distinguir
/// maiúsculas em TODA leitura (login, cadastro, lookup, esqueci minha senha). Contas antigas
/// que diferem só por maiúsculas podem coexistir — a migration EmailsMinusculos não apaga nem
/// renomeia ninguém e só cria o índice único em lower(email) quando não há colisão —, por isso
/// a busca é determinística: match exato primeiro, senão a conta mais antiga.
/// </summary>
public static class UserEmail
{
    /// <summary>Forma gravada no banco: sem espaços nas pontas e em minúsculas.</summary>
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Contas cujo e-mail é igual ao informado sem distinguir maiúsculas, da mais provável para
    /// a menos: igual ao digitado, igual em minúsculas, depois a mais antiga (CreatedAt).
    /// Quem chama filtra antes (ex.: só ativas) e pega a primeira.
    /// </summary>
    public static IOrderedQueryable<User> MatchingEmail(IQueryable<User> users, string? email)
    {
        var typed = (email ?? string.Empty).Trim();
        var normalized = typed.ToLowerInvariant();
        return users
            .Where(u => u.Email.ToLower() == normalized)
            .OrderBy(u => u.Email == typed ? 0 : u.Email == normalized ? 1 : 2)
            .ThenBy(u => u.CreatedAt);
    }

    /// <summary>
    /// Já existe conta (ativa ou não) com este e-mail, sem distinguir maiúsculas? Com
    /// <paramref name="exceptUserId"/> ignora a própria conta (edição).
    /// </summary>
    public static Task<bool> IsTakenAsync(
        IQueryable<User> users, string? email, Guid? exceptUserId, CancellationToken ct)
    {
        var normalized = Normalize(email);
        var query = users.Where(u => u.Email.ToLower() == normalized);
        if (exceptUserId is { } id)
            query = query.Where(u => u.Id != id);
        return query.AnyAsync(ct);
    }
}
