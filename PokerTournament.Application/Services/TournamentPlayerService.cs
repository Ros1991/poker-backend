using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Jogadores pelo torneio (SPEC-20261023-1213): cadastrar jogador novo no home game do torneio e pôr/trocar a foto,
/// pelo organizador ou pelo dealer autorizado (o controller libera o token de dealer). Registra no histórico do
/// torneio "Fulano foi cadastrado" e "Foto de Fulano alterada" com o autor (usuário ou pedido de dealer).
/// Nenhuma checagem de plano (F6): jogadores fazem parte de todos os planos.
/// </summary>
public class TournamentPlayerService
{
    public const string DuplicateNameMessage = "Já existe um jogador com esse nome no home game. Procure na lista.";
    public const string DuplicateWhatsappMessage = "Esse WhatsApp já está cadastrado em outro jogador do home game.";
    public const string DuplicateEmailMessage = "Esse e-mail já está cadastrado em outro jogador do home game.";
    public const string EmailTooLongMessage = "O e-mail pode ter no máximo 200 caracteres.";
    public const string DealerPhotoForbiddenMessage = "O dealer só pode pôr foto em quem está no torneio ou ainda não tem foto.";

    /// <summary>Teto de cadastros de um mesmo dealer autorizado (pedido aprovado) num torneio: limita lixo e sondagem.</summary>
    public const int ScopedTokenCreateLimit = 100;
    public static readonly string ScopedTokenCreateLimitMessage =
        $"O dealer já cadastrou {ScopedTokenCreateLimit} jogadores neste torneio. Peça ao organizador para cadastrar os próximos.";

    private readonly IAppDbContext _db;
    private readonly PhotoService _photos;

    public TournamentPlayerService(IAppDbContext db, PhotoService photos)
    {
        _db = db;
        _photos = photos;
    }

    private async Task<Guid> HomeGameOfAsync(Guid tournamentId, CancellationToken ct) =>
        await _db.Tournaments
            .Where(t => t.Id == tournamentId)
            .Select(t => (Guid?)t.HomeGameId)
            .FirstOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("Torneio não encontrado.");

    /// <summary>
    /// Cadastra o jogador no home game do torneio. Nome 2–120 (espaços normalizados), apelido até 100, WhatsApp
    /// normalizado (10–11 dígitos). Nome repetido no home game (ignorando maiúsculas e espaços) → 400. WhatsApp já
    /// usado → 400 para o organizador; para o token de escopo restrito (<paramref name="scopedToken"/>, dealer por QR)
    /// o jogador é gravado SEM o WhatsApp repetido e a resposta é a mesma de um número novo — senão o dealer, que nunca
    /// lê contato (ScopedTokenContactFilter), descobriria número a número quais telefones são do clube. O dealer também
    /// tem um teto de <see cref="ScopedTokenCreateLimit"/> cadastros por torneio. Não inscreve no torneio: quem chama
    /// inscreve pelo lote (POST entries/bulk).
    /// </summary>
    public async Task<AvailablePlayerResponse> CreateAsync(
        Guid tournamentId, CreateTournamentPlayerRequest request, Guid? createdBy, bool scopedToken = false,
        CancellationToken ct = default)
    {
        var homeGameId = await HomeGameOfAsync(tournamentId, ct);

        if (scopedToken && createdBy is { } dealerId
            && await _db.TournamentActivities.CountAsync(a => a.TournamentId == tournamentId
                && a.Type == nameof(TournamentActivityType.PlayerCreated) && a.CreatedBy == dealerId, ct) >= ScopedTokenCreateLimit)
            throw new DomainException(ScopedTokenCreateLimitMessage);

        var fullName = PlayerNames.NormalizeSpaces(request.FullName);
        if (fullName.Length < 2 || fullName.Length > 120)
            throw new DomainException("Informe o nome completo do jogador (de 2 a 120 caracteres).");

        var nickname = PlayerNames.NormalizeSpaces(request.Nickname);
        if (nickname.Length > 100)
            throw new DomainException("O apelido pode ter no máximo 100 caracteres.");

        var whatsapp = PhoneNumbers.NormalizeLocal(request.Whatsapp);
        if (whatsapp is not null && !PhoneNumbers.IsValidLocal(whatsapp))
            throw new DomainException(PhoneNumbers.InvalidWhatsappMessage);

        // E-mail (SPEC-20261029-1725): só do organizador. O dealer por QR não informa e-mail (o campo nem aparece para
        // ele): o e-mail liga o jogador à conta e a resposta já sairia com a foto dela — um oráculo de contas.
        var email = scopedToken || string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is { Length: > 200 })
            throw new DomainException(EmailTooLongMessage);

        await EnsureUniqueNameAsync(homeGameId, fullName, ct);
        if (whatsapp is not null && await WhatsappInUseAsync(homeGameId, whatsapp, ct))
        {
            if (!scopedToken)
                throw new DomainException(DuplicateWhatsappMessage);
            whatsapp = null; // dealer: sem oráculo — grava sem o número repetido (o organizador completa no painel)
        }
        if (email is not null && await PlayerLinkService.EmailInUseAsync(_db, homeGameId, email, null, ct))
            throw new DomainException(DuplicateEmailMessage);

        var person = new Person
        {
            HomeGameId = homeGameId,
            Type = "Jogador",
            FullName = fullName,
            Nickname = nickname.Length == 0 ? null : nickname,
            Email = email,
            Whatsapp = whatsapp,
            IsActive = true
        };
        // E-mail igual ao de uma conta confirmada → jogador vinculado a ela; a resposta já sai com a foto da conta
        await PlayerLinkService.RelinkPersonAsync(_db, person, ct);
        _db.Persons.Add(person);
        // O Id da pessoa é atribuído no SaveChanges: a atividade não aponta para ela (sem EntryId), só o texto
        _db.TournamentActivities.Add(new TournamentActivity
        {
            TournamentId = tournamentId,
            Type = nameof(TournamentActivityType.PlayerCreated),
            Description = $"{EntryService.PlayerDisplayName(person)} foi cadastrado",
            CreatedBy = createdBy
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (email is not null
            && ex.InnerException?.Message.Contains("ix_persons_home_game_email", StringComparison.Ordinal) == true)
        {
            // Dois cadastros ao mesmo tempo com o mesmo e-mail (só o organizador informa e-mail)
            throw new DomainException(DuplicateEmailMessage);
        }
        catch (DbUpdateException) when (whatsapp is not null && !scopedToken)
        {
            // Dois cadastros ao mesmo tempo com o mesmo WhatsApp: o índice único do banco segura o segundo
            throw new DomainException(DuplicateWhatsappMessage);
        }
        catch (DbUpdateException) when (whatsapp is not null)
        {
            // Dealer na mesma corrida: como no número já cadastrado, grava sem o WhatsApp (mesma resposta)
            person.Whatsapp = null;
            await _db.SaveChangesAsync(ct);
        }

        return ToResponse(person);
    }

    /// <summary>Nome (chave sem maiúsculas/espaços) único entre os jogadores do home game.</summary>
    private async Task EnsureUniqueNameAsync(Guid homeGameId, string fullName, CancellationToken ct)
    {
        var existingNames = await _db.Persons
            .Where(p => p.HomeGameId == homeGameId)
            .Select(p => p.FullName)
            .ToListAsync(ct);
        var key = PlayerNames.NameKey(fullName);
        if (existingNames.Any(n => PlayerNames.NameKey(n) == key))
            throw new DomainException(DuplicateNameMessage);
    }

    /// <summary>
    /// WhatsApp já usado por outro jogador (não apagado) do home game — o mesmo recorte do índice único
    /// ix_persons_home_game_whatsapp (whatsapp IS NOT NULL AND deleted_at IS NULL).
    /// </summary>
    private Task<bool> WhatsappInUseAsync(Guid homeGameId, string whatsapp, CancellationToken ct) =>
        _db.Persons.AnyAsync(p => p.HomeGameId == homeGameId && p.Whatsapp == whatsapp, ct);

    /// <summary>
    /// Regra F5 para o token de dealer: pode pôr/trocar a foto de quem é do home game do torneio e ainda NÃO tem
    /// foto ou ESTÁ inscrito neste torneio. Null = a pessoa não é do home game do torneio (404).
    /// </summary>
    public async Task<bool?> CanScopedTokenSetPhotoAsync(Guid tournamentId, Guid personId, CancellationToken ct = default)
    {
        var homeGameId = await HomeGameOfAsync(tournamentId, ct);
        var person = await _db.Persons
            .AsNoTracking()
            .Where(p => p.Id == personId && p.HomeGameId == homeGameId)
            .Select(p => new { p.PhotoUrl })
            .FirstOrDefaultAsync(ct);
        if (person is null)
            return null;
        if (string.IsNullOrEmpty(person.PhotoUrl))
            return true;
        return await _db.TournamentEntries.AnyAsync(e => e.TournamentId == tournamentId && e.PersonId == personId, ct);
    }

    /// <summary>
    /// Põe/troca a foto de um jogador do home game do torneio (pessoa de fora → 404) e registra
    /// "Foto de Fulano alterada" no histórico (com a inscrição, se houver).
    /// </summary>
    public async Task<AvailablePlayerResponse> SetPhotoAsync(
        Guid tournamentId, Guid personId, Stream data, long length, Guid? actor, CancellationToken ct = default)
    {
        var homeGameId = await HomeGameOfAsync(tournamentId, ct);
        var person = await _db.Persons
            .Include(p => p.User) // resposta com a foto exibida (a da conta, se tiver)
            .FirstOrDefaultAsync(p => p.Id == personId && p.HomeGameId == homeGameId, ct)
            ?? throw new KeyNotFoundException("Jogador não encontrado neste home game.");

        var entryId = await _db.TournamentEntries
            .Where(e => e.TournamentId == tournamentId && e.PersonId == personId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        await _photos.SetPersonPhotoAsync(person, data, length, () =>
            _db.TournamentActivities.Add(new TournamentActivity
            {
                TournamentId = tournamentId,
                EntryId = entryId,
                Type = nameof(TournamentActivityType.PlayerPhotoChanged),
                Description = $"Foto de {EntryService.PlayerDisplayName(person)} alterada",
                CreatedBy = actor
            }), ct);

        return ToResponse(person);
    }

    /// <summary>Foto EXIBIDA (a da conta vinculada, se tiver — SPEC-20261029-1725): exige <c>person.User</c> carregado.</summary>
    private static AvailablePlayerResponse ToResponse(Person person) => new()
    {
        Id = person.Id,
        FullName = person.FullName,
        Nickname = person.Nickname,
        PhotoUrl = PlayerPhoto.Of(person)
    };
}
