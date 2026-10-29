using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Common;

/// <summary>
/// Foto EXIBIDA do jogador (SPEC-20261029-1725, D12): a da conta vinculada (<see cref="Person.UserId"/>), se ela tiver
/// foto; senão a do cadastro no home game (<see cref="Person.PhotoUrl"/>). É a única regra de foto de jogador do
/// servidor: toda resposta que mostra a foto de um jogador passa por aqui (lista e ficha do jogador, inscrições do
/// painel e do terminal, mesas, pagamentos, dealers do torneio, membros e ranking, inclusive o público).
/// <para>
/// Duas formas: <see cref="Of(Person)"/> para a pessoa carregada — exige <c>Person.User</c> incluído
/// (<c>Include(..).ThenInclude(p =&gt; p.User)</c>; <see cref="EntryQueries.EntriesWithPerson"/> já inclui) — e
/// <see cref="Of(string?, string?)"/> dentro de projeções do EF, com <c>p.User!.PhotoUrl</c> e <c>p.PhotoUrl</c> como
/// argumentos: o EF traduz os dois (LEFT JOIN em users) e chama o método na projeção final.
/// </para>
/// </summary>
public static class PlayerPhoto
{
    /// <summary>Foto da conta (se houver) ou a do cadastro.</summary>
    public static string? Of(string? accountPhotoUrl, string? ownPhotoUrl) =>
        string.IsNullOrWhiteSpace(accountPhotoUrl) ? ownPhotoUrl : accountPhotoUrl;

    /// <summary>Foto exibida da pessoa carregada (com <c>User</c> incluído).</summary>
    public static string? Of(Person person) => Of(person.User?.PhotoUrl, person.PhotoUrl);

    /// <summary>A foto exibida é a da conta vinculada (para o aviso "A foto vem da conta de Fulano").</summary>
    public static bool FromAccount(string? accountPhotoUrl) => !string.IsNullOrWhiteSpace(accountPhotoUrl);

    /// <summary>A foto exibida da pessoa carregada (com <c>User</c> incluído) é a da conta vinculada.</summary>
    public static bool FromAccount(Person person) => FromAccount(person.User?.PhotoUrl);
}
