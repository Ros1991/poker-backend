using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Pix;
using PokerTournament.Application.Services;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Api.Controllers;

/// <summary>
/// A rota é só {id} (sem {homeGameId}), então o TenantAccessFilter não age aqui: as leituras
/// por id conferem dono/membro/Admin via <see cref="HomeGameScopedControllerBase"/>.
/// Plano (SPEC-20261023-1205): criar home game e adicionar membro conferem o limite do plano (402
/// home_games_limit / members_limit — o Admin do sistema também segue, SPEC-20261026-1126); as respostas levam
/// ownerPlan e planFeatures (plano efetivo do dono). Editar, excluir e mexer em membro existente não conferem plano.
/// Chave PIX (SPEC-20261023-1218): criar/editar gravam a chave no formato do DICT (PixKeyNormalizer; fora de formato →
/// 400 com a mensagem) e o beneficiário com Trim (vazio → null), sem checagem de plano. O PUT copia o pedido inteiro:
/// o frontend manda chave, beneficiário e os três valores padrão para não apagá-los.
/// Home game principal (SPEC-20261029-1723): toda conta tem um (<see cref="HomeGamePrimary"/>); as respostas levam
/// isPrimary, isNameDefault e displayName (null no principal de dono Mesa). Criar outro só na Liga (o principal não conta
/// no limite; Mesa e Clube → 402); o PUT renomeia o principal (é o "nome do clube" no Clube/Liga); o DELETE recusa o
/// principal (400).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class HomeGamesController : HomeGameScopedControllerBase
{
    private readonly IMapper _mapper;
    private readonly PlanService _plans;

    public HomeGamesController(IAppDbContext db, IMapper mapper, PlanService plans) : base(db)
    {
        _mapper = mapper;
        _plans = plans;
    }

    // Map + contagens + plano efetivo do dono (ownerPlan/planFeatures) + nome exibido do principal
    private async Task<HomeGameResponse> ToResponseAsync(HomeGame homeGame, CancellationToken ct)
    {
        var response = _mapper.Map<HomeGameResponse>(homeGame);
        await ApplyCountsAsync([response], ct);
        await ApplyCanManageAsync([response], ct);
        await _plans.ApplyOwnerPlanAsync([response], ct);
        await HomeGamePrimary.ApplyAsync(_db, [response], ct);
        return response;
    }

    /// <summary>
    /// <c>canManage</c> de quem está logado (SPEC-20261029-1724): dono, membro administrador ou Admin do sistema — a
    /// regra do <see cref="HomeGameAccess.CanManageAsync"/>, numa consulta só para todos os home games da resposta.
    /// </summary>
    private async Task ApplyCanManageAsync(IReadOnlyList<HomeGameResponse> responses, CancellationToken ct)
    {
        if (responses.Count == 0)
            return;
        var userId = GetUserId();
        var isAdmin = IsAdmin;
        var ids = responses.Select(r => r.Id).Distinct().ToList();
        var adminOf = isAdmin
            ? []
            : await _db.HomeGameMembers.AsNoTracking()
                .Where(m => ids.Contains(m.HomeGameId) && m.UserId == userId && m.IsAdmin)
                .Select(m => m.HomeGameId)
                .ToListAsync(ct);
        foreach (var response in responses)
            response.CanManage = isAdmin || response.OwnerId == userId || adminOf.Contains(response.Id);
    }

    /// <summary>
    /// Contagens do cabeçalho e dos cards (SPEC-20261026-1907): as MESMAS listas que a tela mostra — torneios da aba
    /// Torneios (GET tournaments: sem cancelados; excluídos já saem pelo filtro global) e membros da aba Membros
    /// (GET members). Uma consulta agrupada por contagem para todos os home games da resposta, em todo endpoint que
    /// devolve HomeGameResponse (antes o editar respondia 0/0 por não carregar as coleções).
    /// </summary>
    private async Task ApplyCountsAsync(IReadOnlyList<HomeGameResponse> responses, CancellationToken ct)
    {
        if (responses.Count == 0)
            return;
        var ids = responses.Select(r => r.Id).Distinct().ToList();
        var cancelled = nameof(TournamentStatus.Cancelled);
        var tournaments = await _db.Tournaments.AsNoTracking()
            .Where(t => ids.Contains(t.HomeGameId) && t.Status != cancelled)
            .GroupBy(t => t.HomeGameId)
            .Select(g => new { HomeGameId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.HomeGameId, x => x.Count, ct);
        var members = await _db.HomeGameMembers.AsNoTracking()
            .Where(m => ids.Contains(m.HomeGameId))
            .GroupBy(m => m.HomeGameId)
            .Select(g => new { HomeGameId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.HomeGameId, x => x.Count, ct);
        foreach (var response in responses)
        {
            response.TournamentCount = tournaments.GetValueOrDefault(response.Id);
            response.MemberCount = members.GetValueOrDefault(response.Id);
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<HomeGameResponse>>> GetAll(CancellationToken ct)
    {
        var userId = GetUserId();

        // O principal da própria conta primeiro (SPEC-20261029-1723), depois por nome
        var homeGames = await _db.HomeGames
            .Where(h => h.IsActive && (
                h.OwnerId == userId ||
                h.Members.Any(m => m.UserId == userId )
            ))
            .OrderByDescending(h => h.OwnerId == userId && h.IsPrimary)
            .ThenBy(h => h.Name)
            .ToListAsync(ct);

        var responses = _mapper.Map<List<HomeGameResponse>>(homeGames);
        await ApplyCountsAsync(responses, ct);
        await ApplyCanManageAsync(responses, ct);
        await _plans.ApplyOwnerPlanAsync(responses, ct);
        await HomeGamePrimary.ApplyAsync(_db, responses, ct);
        return Ok(responses);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HomeGameResponse>> GetById(Guid id, CancellationToken ct)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);

        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        // 403 (e não 404) como o filtro: o id não é segredo, o conteúdo (PIX, defaults) é
        if (!await CanAccessAsync(id, ct))
            return Forbidden();

        return Ok(await ToResponseAsync(homeGame, ct));
    }

    [HttpPost]
    public async Task<ActionResult<HomeGameResponse>> Create(
        [FromBody] CreateHomeGameRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        // Limite de home games como dono (trava por dono na transação: dois cliques não passam juntos). O principal não
        // conta (SPEC-20261029-1723); conta sem principal (só fora do fluxo normal) faz deste o principal.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var becomesPrimary = await _plans.EnsureCanCreateHomeGameAsync(userId, ct);

        var homeGame = _mapper.Map<HomeGame>(request);
        homeGame.OwnerId = userId;
        homeGame.IsPrimary = becomesPrimary;
        homeGame.PixKey = PixKeyNormalizer.NormalizeForWrite(request.PixKey, request.PixKeyType);
        homeGame.PixBeneficiary = NullIfEmpty(request.PixBeneficiary);

        // Nome único por dono (trava por dono + checagem; a corrida que escapar cai no índice → mesma mensagem)
        await HomeGameNames.EnsureAvailableAsync(_db, userId, homeGame.Name, exceptId: null, ct);

        _db.HomeGames.Add(homeGame);
        await SaveNameCheckedAsync(homeGame.Name, ct);
        await tx.CommitAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = homeGame.Id },
            await ToResponseAsync(homeGame, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HomeGameResponse>> Update(
        Guid id, [FromBody] CreateHomeGameRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        // Transação: a checagem do nome único por dono trava o dono até gravar
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);

        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
            return Forbid();

        var currentPixKey = homeGame.PixKey;
        _mapper.Map(request, homeGame);
        // Chave nova no formato do DICT; a MESMA chave antiga que não normaliza fica como está (seção escondida no Mesa)
        homeGame.PixKey = PixKeyNormalizer.NormalizeForUpdate(request.PixKey, request.PixKeyType, currentPixKey);
        homeGame.PixBeneficiary = NullIfEmpty(request.PixBeneficiary);
        await HomeGameNames.EnsureAvailableAsync(_db, homeGame.OwnerId, homeGame.Name, exceptId: homeGame.Id, ct);
        await SaveNameCheckedAsync(homeGame.Name, ct);
        await tx.CommitAsync(ct);

        return Ok(await ToResponseAsync(homeGame, ct));
    }

    /// <summary>
    /// SaveChanges do criar/editar: a violação do índice (owner_id, name) — corrida que escapou da trava — vira o mesmo
    /// 400 da checagem, nunca 500.
    /// </summary>
    private async Task SaveNameCheckedAsync(string name, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (HomeGameNames.IsDuplicateName(ex))
        {
            throw new DomainException(HomeGameNames.DuplicateMessage(name));
        }
    }

    /// <summary>
    /// O que some junto com o home game (SPEC-20261026-1126) — torneios, jogadores, rankings, membros — e o que impede a
    /// exclusão (torneio em andamento ou, desde a SPEC-20261029-1723, ser o principal da conta: <c>isPrimary</c> +
    /// <c>blockedMessage</c>). Só o dono e o Admin do sistema (membro administrador não exclui).
    /// </summary>
    [HttpGet("{id:guid}/deletion-summary")]
    public async Task<ActionResult<HomeGameDeletionSummaryResponse>> GetDeletionSummary(
        Guid id, [FromServices] HomeGameDeletionService deletion, CancellationToken ct)
    {
        var access = await CanDeleteAsync(id, ct);
        if (access is not null)
            return access;

        var summary = await deletion.GetSummaryAsync(id, ct);
        return summary is null ? HomeGameNotFound() : Ok(summary);
    }

    /// <summary>
    /// Exclui o home game (SPEC-20261026-1126): dono ou Admin do sistema, com o nome digitado no corpo
    /// ({ "confirmName": "..." }, sem diferenciar maiúsculas e ignorando espaços nas pontas). Exclusão lógica em cascata
    /// (torneios, jogadores, rankings; membros saem; TVs e dealers desconectados; AuditLog HomeGameDeleted) — ver
    /// <see cref="HomeGameDeletionService"/>. 404 inexistente/já excluído; 403 quem não é dono nem Admin (membro,
    /// outro grupo; o token de dealer nem chega aqui); 400 principal da conta (SPEC-20261029-1723, também para o Admin),
    /// nome vazio/diferente ou torneio em andamento; 204 excluído.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DeleteHomeGameRequest? request,
        [FromServices] HomeGameDeletionService deletion,
        CancellationToken ct)
    {
        var access = await CanDeleteAsync(id, ct);
        if (access is not null)
            return access;

        var deleted = await deletion.DeleteAsync(
            id, request?.ConfirmName, GetUserId(), IsAdmin,
            PokerTournament.Api.Http.ClientIp.Resolve(HttpContext),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            ct);
        return deleted is null ? HomeGameNotFound() : NoContent();
    }

    // Excluir: 404 se não existe (ou já foi excluído); 403 se quem chama não é o dono nem o Admin do sistema
    private async Task<ActionResult?> CanDeleteAsync(Guid id, CancellationToken ct)
    {
        var owner = await _db.HomeGames.AsNoTracking()
            .Where(h => h.Id == id)
            .Select(h => (Guid?)h.OwnerId)
            .FirstOrDefaultAsync(ct);
        if (owner is null)
            return HomeGameNotFound();
        var userId = GetUserId();
        if (owner == userId || IsAdmin)
            return null;
        // Membro (mesmo administrador) vê o motivo; quem é de outro grupo recebe o 403 de sempre
        return await _db.HomeGameMembers.AnyAsync(m => m.HomeGameId == id && m.UserId == userId, ct)
            ? new ObjectResult(new { message = "Só o dono do home game pode excluí-lo." }) { StatusCode = 403 }
            : Forbidden();
    }

    private NotFoundObjectResult HomeGameNotFound() => NotFound(new { message = "Home game não encontrado." });

    /// <summary>
    /// Logo do clube (SPEC-20261023-1213, ajuste do orquestrador): aparece no telão e no ranking público no plano
    /// Liga. Dono, membro administrador ou Admin do sistema. JPEG/PNG/WebP até 2 MB (assinatura conferida); a logo
    /// vem do navegador na proporção original, lado maior até 512 px. Sem checagem de plano: guardar a logo vale em
    /// qualquer plano, quem decide se ela aparece é o plano do dono (DisplayService/ranking público).
    /// </summary>
    [HttpPost("{id:guid}/logo")]
    [RequestSizeLimit(PokerTournament.Application.Common.ImageFiles.MaxRequestBytes)]
    public async Task<ActionResult<HomeGameResponse>> UploadLogo(
        Guid id, IFormFile? file, [FromServices] PhotoService photos, CancellationToken ct)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });
        if (!await CanManageAsync(id, ct))
            return Forbidden();
        if (file is null || file.Length == 0)
            return BadRequest(new { message = PokerTournament.Application.Common.ImageFiles.InvalidLogoMessage });

        await using var stream = file.OpenReadStream();
        await photos.SetHomeGameLogoAsync(homeGame, stream, file.Length, ct);

        return Ok(await ToResponseAsync(homeGame, ct));
    }

    /// <summary>Remove a logo do clube (mesma regra do envio).</summary>
    [HttpDelete("{id:guid}/logo")]
    public async Task<ActionResult<HomeGameResponse>> RemoveLogo(
        Guid id, [FromServices] PhotoService photos, CancellationToken ct)
    {
        var homeGame = await _db.HomeGames
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });
        if (!await CanManageAsync(id, ct))
            return Forbidden();

        await photos.RemoveHomeGameLogoAsync(homeGame, ct);

        return Ok(await ToResponseAsync(homeGame, ct));
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<HomeGameMemberResponse>>> GetMembers(Guid id, CancellationToken ct)
    {
        if (!await _db.HomeGames.AnyAsync(h => h.Id == id, ct))
            return NotFound(new { message = "Home game não encontrado." });

        if (!await CanAccessAsync(id, ct))
            return Forbidden();

        var members = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person).ThenInclude(p => p!.User) // foto exibida do jogador (SPEC-20261029-1725)
            .Where(m => m.HomeGameId == id )
            .OrderBy(m => m.User.FullName)
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<HomeGameMemberResponse>>(members));
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<HomeGameMemberResponse>> AddMember(
        Guid id, [FromBody] AddHomeGameMemberRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        // Limite de membros do plano do dono (trava por home game na transação)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var exists = await _db.HomeGameMembers
            .AnyAsync(m => m.HomeGameId == id && m.UserId == request.UserId, ct);

        if (exists)
            return Conflict(new { message = "Usuário já é membro deste home game." });

        await _plans.EnsureCanAddMemberAsync(id, ct);

        // Vínculo só com jogador/dealer DESTE home game (pessoa apagada não conta: filtro de soft delete)
        Person? linkedPerson = null;
        if (request.PersonId is { } personId)
        {
            linkedPerson = await _db.Persons.FirstOrDefaultAsync(p => p.Id == personId && p.HomeGameId == id, ct);
            if (linkedPerson is null)
                return BadRequest(new { message = "Jogador não pertence a este home game." });
        }

        // "Vincular membro ↔ jogador" também liga o jogador à conta do membro (SPEC-20261029-1725), mesmo com outro
        // e-mail no cadastro (o modal avisa antes). Remover o membro depois não desfaz o vínculo do jogador.
        if (linkedPerson is not null)
            linkedPerson.UserId = request.UserId;

        var member = new HomeGameMember
        {
            HomeGameId = id,
            UserId = request.UserId,
            PersonId = request.PersonId,
            IsAdmin = request.IsAdmin,
            JoinedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.HomeGameMembers.Add(member);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        member = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person).ThenInclude(p => p!.User)
            .FirstAsync(m => m.Id == member.Id, ct);

        return Ok(_mapper.Map<HomeGameMemberResponse>(member));
    }

    [HttpPut("{id:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<HomeGameMemberResponse>> UpdateMember(
        Guid id, Guid memberId, [FromBody] UpdateHomeGameMemberRequest request, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        var member = await _db.HomeGameMembers
            .Include(m => m.User)
            .Include(m => m.Person).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.HomeGameId == id, ct);

        if (member is null)
            return NotFound(new { message = "Membro não encontrado." });

        if (request.IsAdmin.HasValue)
            member.IsAdmin = request.IsAdmin.Value;

        if (request.PersonId is { } personId)
        {
            // Vínculo só com jogador/dealer DESTE home game (pessoa apagada não conta: filtro de soft delete)
            var person = await _db.Persons
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == personId && p.HomeGameId == id, ct);
            if (person is null)
                return BadRequest(new { message = "Jogador não pertence a este home game." });

            member.PersonId = personId;
            member.Person = person;
            // Como no AddMember: o jogador passa a ser da conta do membro (SPEC-20261029-1725)
            person.UserId = member.UserId;
            person.User = member.User;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<HomeGameMemberResponse>(member));
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<ActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken ct)
    {
        var userId = GetUserId();

        var homeGame = await _db.HomeGames.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (homeGame is null)
            return NotFound(new { message = "Home game não encontrado." });

        if (homeGame.OwnerId != userId)
        {
            var isAdmin = await _db.HomeGameMembers
                .AnyAsync(m => m.HomeGameId == id && m.UserId == userId && m.IsAdmin , ct);
            if (!isAdmin)
                return Forbid();
        }

        var member = await _db.HomeGameMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.HomeGameId == id, ct);

        if (member is null)
            return NotFound(new { message = "Membro não encontrado." });

        // Hard delete - remove o membro completamente
        _db.HomeGameMembers.Remove(member);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class AddHomeGameMemberRequest
{
    public Guid UserId { get; set; }
    public Guid? PersonId { get; set; }
    public bool IsAdmin { get; set; }
}

public class UpdateHomeGameMemberRequest
{
    public bool? IsAdmin { get; set; }
    public Guid? PersonId { get; set; }
}
