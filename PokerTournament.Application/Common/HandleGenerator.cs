using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Common;

/// <summary>
/// Apelido único da URL do perfil público (<c>/jogador/{handle}</c>, SPEC-20261108-1636): minúsculas, 3–30
/// caracteres entre letras sem acento, números e hífen (sem hífen nas pontas nem dois seguidos). Gerado do apelido
/// (senão do primeiro nome) no cadastro — <see cref="FromName"/> + <see cref="NextAvailableAsync"/> — e editável no
/// Meu perfil (<see cref="Normalize"/> valida o que a pessoa digitou: formato, palavras e prefixos reservados). A
/// migration <c>PerfilDoJogador</c> aplicou a regra de FORMATO em PL/pgSQL para as contas existentes (já rodou em
/// produção; a lista de reservados cresceu depois dela e vale só daqui para a frente, pelo <see cref="Normalize"/>).
/// </summary>
public static partial class HandleGenerator
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    /// <summary>Base quando o nome não rende nada (só símbolos, por exemplo).</summary>
    public const string Fallback = "jogador";

    public const string InvalidMessage =
        "O apelido do perfil deve ter de 3 a 30 caracteres: letras minúsculas sem acento, números e hífen (sem hífen no começo nem no fim).";
    public const string ReservedMessage = "Esse apelido de perfil é reservado. Escolha outro.";
    public const string ReservedPrefixMessage =
        "Apelidos de perfil que começam com “excluida-” ou “pokerdagalera-” são reservados. Escolha outro.";
    public const string TakenMessage = "Esse apelido de perfil já está em uso. Escolha outro.";

    /// <summary>Palavras que ninguém pode usar como handle (rotas, marca, papéis e equipe do sistema — revisão da onda 2, B4).</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "administrador", "administracao", "moderador", "moderacao", "oficial", "suporte", "ajuda", "contato",
        "equipe", "staff", "sistema", "poker", "pokerdagalera", "galera", "api", "me", "eu", "perfil", "login",
        "entrar", "cadastro", "root", "termos", "privacidade", "manual", "inicio", "tv",
        "dealer", "amigos", "mensagens", "anonimo", "convidado", "null", "undefined",
    };

    /// <summary>Prefixo do handle da conta excluída (<see cref="ForDeletedAccount"/>): ninguém escolhe um handle assim.</summary>
    public const string DeletedPrefix = "excluida-";

    /// <summary>
    /// Começos que ninguém pode usar (revisão da onda 2, A1/B4): <c>excluida-</c> é o da conta anonimizada — se um
    /// terceiro pegasse o handle que a exclusão vai gravar, a exclusão da vítima falharia no índice único — e
    /// <c>pokerdagalera-</c> é a marca.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedPrefixes = [DeletedPrefix, "pokerdagalera-"];

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidPattern();

    /// <summary>O texto já normalizado é um handle válido (formato e tamanho; não confere reservado nem duplicado).</summary>
    public static bool IsValid(string handle) =>
        handle.Length is >= MinLength and <= MaxLength && ValidPattern().IsMatch(handle);

    public static bool IsReserved(string handle) => Reserved.Contains(handle) || HasReservedPrefix(handle);

    public static bool HasReservedPrefix(string handle) =>
        ReservedPrefixes.Any(prefix => handle.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// O que a pessoa digitou no Meu perfil: tira espaços e um "@" inicial, passa para minúsculas e valida
    /// (<see cref="DomainException"/> com a mensagem quando não serve). Não tira acento nem troca caractere: quem
    /// edita escolhe exatamente o que fica.
    /// </summary>
    public static string Normalize(string? input)
    {
        var handle = (input ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
        if (!IsValid(handle))
            throw new DomainException(InvalidMessage);
        if (HasReservedPrefix(handle))
            throw new DomainException(ReservedPrefixMessage);
        if (IsReserved(handle))
            throw new DomainException(ReservedMessage);
        return handle;
    }

    /// <summary>
    /// Base do handle a partir do apelido (senão do primeiro nome): minúsculas, sem acento, tudo que não é letra/número
    /// vira um hífen. Vazia → "jogador"; curta (menos de 3) ou reservada → "jogador-&lt;base&gt;"; cortada em 30.
    /// </summary>
    public static string FromName(string? nickname, string? fullName)
    {
        var source = (nickname ?? string.Empty).Trim();
        if (source.Length == 0)
        {
            var name = (fullName ?? string.Empty).Trim();
            var space = name.IndexOf(' ');
            source = space > 0 ? name[..space] : name;
        }

        var decomposed = source.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingDash = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                pendingDash = false;
                builder.Append(ch);
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length == 0)
            return Fallback;
        if (slug.Length < MinLength || IsReserved(slug))
            slug = Fallback + "-" + slug;
        return Cut(slug, MaxLength);
    }

    /// <summary>A base ou, se já usada, base-2, base-3… (sempre dentro de 30 caracteres).</summary>
    public static string MakeUnique(string baseHandle, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(baseHandle))
            return baseHandle;
        for (var i = 2; ; i++)
        {
            var suffix = $"-{i}";
            var candidate = Cut(baseHandle, MaxLength - suffix.Length) + suffix;
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Handle livre para uma conta nova (cadastro com senha ou pelo Google): a base do apelido/nome e, em colisão, o
    /// sufixo numérico. Lê só os handles que começam com a base (não a tabela inteira).
    /// </summary>
    public static async Task<string> NextAvailableAsync(IAppDbContext db, string? nickname, string? fullName, CancellationToken ct)
    {
        var baseHandle = FromName(nickname, fullName);
        var prefix = baseHandle + "-";
        var taken = await db.Users
            .Where(u => u.Handle == baseHandle || u.Handle.StartsWith(prefix))
            .Select(u => u.Handle)
            .ToListAsync(ct);
        return MakeUnique(baseHandle, new HashSet<string>(taken, StringComparer.Ordinal));
    }

    /// <summary>
    /// Handle da conta anonimizada (DELETE me): libera o anterior e continua único. <c>excluida-</c> + 21 hex do id
    /// (30 caracteres); em colisão com uma linha antiga (antes do prefixo ser reservado), a tentativa 2, 3… troca o fim
    /// do hex por <c>-2</c>, <c>-3</c>… — sempre dentro dos 30.
    /// </summary>
    public static string ForDeletedAccount(Guid userId, int attempt = 1)
    {
        var hex = userId.ToString("N");
        var room = MaxLength - DeletedPrefix.Length;
        if (attempt <= 1)
            return DeletedPrefix + hex[..room];
        var suffix = $"-{attempt}";
        return DeletedPrefix + hex[..(room - suffix.Length)] + suffix;
    }

    private static string Cut(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd('-');
}
