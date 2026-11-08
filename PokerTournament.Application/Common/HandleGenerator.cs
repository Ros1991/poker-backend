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
/// Meu perfil (<see cref="Normalize"/> valida o que a pessoa digitou). A migration <c>PerfilDoJogador</c> aplica a
/// MESMA regra em PL/pgSQL para as contas existentes: se a regra mudar aqui, mude lá também.
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
    public const string TakenMessage = "Esse apelido de perfil já está em uso. Escolha outro.";

    /// <summary>Palavras que ninguém pode usar como handle (rotas, marca e papéis do sistema).</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "administrador", "suporte", "poker", "pokerdagalera", "galera", "api", "me", "eu", "perfil", "login",
        "entrar", "cadastro", "sistema", "root", "ajuda", "contato", "termos", "privacidade", "manual", "inicio", "tv",
        "dealer", "amigos", "mensagens", "anonimo", "convidado", "null", "undefined",
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidPattern();

    /// <summary>O texto já normalizado é um handle válido (formato e tamanho; não confere reservado nem duplicado).</summary>
    public static bool IsValid(string handle) =>
        handle.Length is >= MinLength and <= MaxLength && ValidPattern().IsMatch(handle);

    public static bool IsReserved(string handle) => Reserved.Contains(handle);

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

    /// <summary>Handle da conta anonimizada (DELETE me): libera o anterior e continua único.</summary>
    public static string ForDeletedAccount(Guid userId) => "excluida-" + userId.ToString("N")[..12];

    private static string Cut(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd('-');
}
