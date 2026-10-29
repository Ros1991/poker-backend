using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Interfaces;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Enums;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// O servidor manda e-mail de verdade? Verdadeiro só com o Resend ligado (RESEND_APITOKEN fora de Development ou
/// Email:ForceResend). Falso = <c>LoggingEmailSender</c> (o link fica só no log): a administração responde 409 no modo
/// "Enviar e-mail" e no reenvio da confirmação. Registrado em Program.cs junto da escolha do <see cref="IEmailSender"/>.
/// </summary>
public sealed record EmailDeliveryStatus(bool Enabled);

/// <summary>Quem faz a ação administrativa (id do token do Admin, IP pelo último hop do X-Forwarded-For e User-Agent).</summary>
public sealed record AdminActor(Guid UserId, string? Ip, string? UserAgent);

/// <summary>
/// Administração geral (SPEC-20261023-1206), só para o Admin do sistema: listar contas (busca, filtros, paginação), ver
/// a ficha (acesso, home games, plano com histórico do audit_logs, e-mail, formas de entrar), editar dados e papel,
/// trocar o plano (pelo mesmo <see cref="PlanService.ChangePlanAsync"/> da SPEC-20261023-1205), redefinir a senha
/// (temporária, e-mail ou link de 24 h para WhatsApp), ativar/desativar e confirmar o e-mail manualmente ou reenviar a
/// confirmação. NÃO cria conta (D14). Toda ação grava AuditLog { EntityType = "User", EntityId = alvo } com o Admin
/// (UserId/UserName), IP e User-Agent — nunca a senha nem o token. Proteções: o Admin não desativa a própria conta,
/// não tira o próprio papel de Admin e o sistema nunca fica sem Admin ativo (trava consultiva única para as duas
/// ações, então dois Admins não se rebaixam ao mesmo tempo).
/// </summary>
public class AdminUserService
{
    public const string UserUpdatedAction = "UserUpdated";
    public const string PasswordResetAction = "PasswordReset";
    public const string StatusChangeAction = "StatusChange";
    public const string ConfirmationResentAction = "EmailConfirmationResent";
    public const string PlanChangeAction = "PlanChange";

    public const string ModeTemporary = "Temporary";
    public const string ModeEmail = "Email";
    public const string ModeLink = "Link";

    public const string UserNotFoundMessage = "Usuário não encontrado.";
    public const string SelfDemoteMessage = "Você não pode tirar o seu próprio papel de Administrador.";
    public const string SelfDeactivateMessage = "Você não pode desativar a sua própria conta.";
    public const string LastAdminMessage = "O sistema precisa de pelo menos um Administrador ativo.";
    public const string EmailNotConfiguredMessage =
        "Envio de e-mail não configurado; use senha temporária ou link.";
    public const string ConfirmationEmailNotConfiguredMessage =
        "Envio de e-mail não configurado; use Confirmar manualmente.";
    public const string EmailSendFailedMessage =
        "Não foi possível enviar o e-mail agora. Tente de novo ou use a senha temporária ou o link.";
    public const string InactiveAccountMessage = "Conta desativada: reative a conta antes.";
    public const string DeletedAccountMessage = "Esta conta foi excluída pela própria pessoa.";
    public const string AlreadyConfirmedMessage = "O e-mail desta conta já está confirmado.";
    public const string ExpiryInPastMessage = "O vencimento precisa ser no futuro. Para tirar o plano pago, escolha Mesa.";

    /// <summary>Marcador do e-mail de conta excluída (AuthService.DeleteAccountAsync).</summary>
    private const string DeletedEmailSuffix = "@excluida.invalid";
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int HistoryLimit = 50;
    // Espaço próprio da trava consultiva "Admins ativos" (pg_advisory_xact_lock(int, int))
    private const int AdminLockSpace = 1206;

    // Ações que só a administração grava: entram em "Ações administrativas" mesmo quando o Admin age sobre si mesmo
    private static readonly string[] AdminOnlyActions = [UserUpdatedAction, StatusChangeAction, ConfirmationResentAction];

    private readonly IAppDbContext _db;
    private readonly AuthService _auth;
    private readonly PlanService _plans;
    private readonly PasswordResetService _passwordReset;
    private readonly EmailConfirmationService _emailConfirmation;
    private readonly IEmailSender _emailSender;
    private readonly EmailDeliveryStatus _delivery;
    private readonly PublicAppOptions _publicApp;
    private readonly ILogger<AdminUserService> _logger;

    public AdminUserService(
        IAppDbContext db,
        AuthService auth,
        PlanService plans,
        PasswordResetService passwordReset,
        EmailConfirmationService emailConfirmation,
        IEmailSender emailSender,
        EmailDeliveryStatus delivery,
        IOptions<PublicAppOptions> publicApp,
        ILogger<AdminUserService> logger)
    {
        _db = db;
        _auth = auth;
        _plans = plans;
        _passwordReset = passwordReset;
        _emailConfirmation = emailConfirmation;
        _emailSender = emailSender;
        _delivery = delivery;
        _publicApp = publicApp.Value;
        _logger = logger;
    }

    // ================================================================== listagem

    /// <summary>
    /// <c>GET admin/users</c>: busca (contém, sem distinguir maiúsculas) em nome, apelido e e-mail; filtro por plano
    /// EFETIVO (Mesa inclui quem venceu), por status (active | inactive | all — padrão all) e "pediu plano" (pedido das
    /// boas-vindas ainda não atendido, mais recentes primeiro). Ordem por nome; 20 por página (máx. 100).
    /// Plano ou status desconhecido → 400.
    /// </summary>
    public async Task<PaginatedResponse<AdminUserListItemResponse>> ListAsync(
        string? search, int page, int pageSize, string? plan, string? status, bool requested, CancellationToken ct = default)
    {
        // Página fora da faixa não pode estourar o OFFSET: (page - 1) * pageSize em int virava negativo com page enorme
        // e o PostgreSQL respondia 2201X (500). Página limitada e offset em long (Paging); além do fim = lista vazia com 200.
        var paging = Paging.Normalize(page, pageSize, DefaultPageSize, MaxPageSize);
        page = paging.Page;
        pageSize = paging.PageSize;
        var now = DateTimeOffset.UtcNow;

        var query = _db.Users.AsNoTracking().AsQueryable();

        var term = (search ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length > 0)
            query = query.Where(u => u.FullName.ToLower().Contains(term)
                || (u.Nickname != null && u.Nickname.ToLower().Contains(term))
                || u.Email.ToLower().Contains(term));

        switch ((status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "all":
                break;
            case "active":
                query = query.Where(u => u.IsActive);
                break;
            case "inactive":
                query = query.Where(u => !u.IsActive);
                break;
            default:
                throw new DomainException("Status inválido. Use active, inactive ou all.");
        }

        if (!string.IsNullOrWhiteSpace(plan) && !string.Equals(plan.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var canonical = PlanCatalog.Normalize(plan) ?? throw new DomainException("Plano inválido. Use Mesa, Clube ou Liga.");
            const string clube = PlanCatalog.Clube, liga = PlanCatalog.Liga;
            query = canonical == PlanCatalog.Mesa
                // Mesa efetivo: gravado Mesa (ou fora do catálogo) ou plano pago já vencido
                ? query.Where(u => !(u.Plan == clube || u.Plan == liga) || (u.PlanExpiresAt != null && u.PlanExpiresAt <= now))
                : query.Where(u => u.Plan == canonical && (u.PlanExpiresAt == null || u.PlanExpiresAt > now));
        }

        if (requested)
        {
            const string liga = PlanCatalog.Liga;
            // Pedido pendente: o plano efetivo ainda não cobre o pedido (Liga cobre tudo; vencido vale Mesa)
            query = query.Where(u => u.RequestedPlan != null
                && !((u.PlanExpiresAt == null || u.PlanExpiresAt > now) && (u.Plan == liga || u.Plan == u.RequestedPlan)));
        }

        var total = await query.CountAsync(ct);
        if (paging.IsPastEnd(total))
            return new PaginatedResponse<AdminUserListItemResponse>([], page, pageSize, total);

        var ordered = requested
            ? query.OrderByDescending(u => u.RequestedPlanAt).ThenBy(u => u.FullName).ThenBy(u => u.Id)
            : query.OrderBy(u => u.FullName).ThenBy(u => u.Email).ThenBy(u => u.Id);

        var rows = await ordered
            .Skip(paging.Skip)
            .Take(pageSize)
            .Select(u => new
            {
                User = u,
                HasPassword = u.PasswordHash != null,
                LastSeenAt = _db.AuthSessions
                    .Where(s => s.UserId == u.Id && s.RevokedAt == null)
                    .Max(s => (DateTimeOffset?)(s.LastUsedAt ?? s.CreatedAt)),
                Owned = _db.HomeGames.Count(h => h.OwnerId == u.Id),
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var u = r.User;
            var effective = PlanCatalog.EffectivePlan(u);
            return new AdminUserListItemResponse
            {
                Id = u.Id,
                FullName = u.FullName,
                Nickname = u.Nickname,
                Email = u.Email,
                PhotoUrl = u.PhotoUrl,
                Role = u.Role,
                IsActive = u.IsActive,
                IsDeleted = IsDeletedAccount(u),
                HasPassword = r.HasPassword,
                EmailConfirmedAt = u.EmailConfirmedAt,
                NeedsOnboarding = u.OnboardedAt is null,
                Plan = PlanCatalog.Normalize(u.Plan) ?? PlanCatalog.Mesa,
                EffectivePlan = effective,
                PlanExpiresAt = u.PlanExpiresAt,
                PlanSource = u.PlanSource,
                PlanNote = u.PlanNote,
                RequestedPlan = u.RequestedPlan,
                RequestedPlanAt = u.RequestedPlanAt,
                PlanRequestPending = IsRequestPending(u.RequestedPlan, effective),
                LastLoginAt = u.LastLoginAt,
                LastSeenAt = r.LastSeenAt,
                CreatedAt = u.CreatedAt,
                OwnedHomeGamesCount = r.Owned,
            };
        }).ToList();

        return new PaginatedResponse<AdminUserListItemResponse>(items, page, pageSize, total);
    }

    // ================================================================== ficha

    /// <summary><c>GET admin/users/{id}</c>. Null = conta inexistente.</summary>
    public async Task<AdminUserDetailResponse?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var sessionRows = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == id && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => new { s.Id, s.UserAgent, s.CreatedAt, s.LastUsedAt, s.ExpiresAt })
            .ToListAsync(ct);
        var lastSeen = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .MaxAsync(s => (DateTimeOffset?)(s.LastUsedAt ?? s.CreatedAt), ct);

        var logins = await _db.UserLogins.AsNoTracking()
            .Where(l => l.UserId == id)
            .OrderByDescending(l => l.LastUsedAt ?? l.CreatedAt)
            .Select(l => new { l.Provider, l.Email, l.CreatedAt, l.LastUsedAt })
            .ToListAsync(ct);

        // O principal da conta primeiro (SPEC-20261029-1723), depois os demais por nome
        var owned = await _db.HomeGames.AsNoTracking()
            .Where(h => h.OwnerId == id)
            .OrderByDescending(h => h.IsPrimary)
            .ThenBy(h => h.Name)
            .Select(h => new AdminUserOwnedHomeGame
            {
                Id = h.Id,
                Name = h.Name,
                IsPrimary = h.IsPrimary,
                TournamentsCount = h.Tournaments.Count,
                MembersCount = h.Members.Count,
            })
            .ToListAsync(ct);

        // O filtro global tira os home games excluídos (o membro de um home game excluído some daqui)
        var memberships = await _db.HomeGameMembers.AsNoTracking()
            .Where(m => m.UserId == id)
            .OrderBy(m => m.HomeGame.Name)
            .Select(m => new AdminUserMembership
            {
                HomeGameId = m.HomeGameId,
                Name = m.HomeGame.Name,
                IsAdmin = m.IsAdmin,
                OwnerName = m.HomeGame.Owner.FullName,
            })
            .ToListAsync(ct);

        // audit_logs.user_id é quem FEZ; o alvo é entity_id
        var planAudits = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "User" && a.EntityId == id
                && (a.Action == PlanChangeAction || a.Action == AuthService.PlanRequestedAction))
            .OrderByDescending(a => a.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);
        var adminAudits = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "User" && a.EntityId == id
                && a.Action != PlanChangeAction && a.Action != AuthService.PlanRequestedAction
                && (a.UserId == null || a.UserId != id || AdminOnlyActions.Contains(a.Action)))
            .OrderByDescending(a => a.CreatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        var updatedByName = user.PlanUpdatedBy is { } by
            ? await _db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;
        var effective = PlanCatalog.EffectivePlan(user);

        return new AdminUserDetailResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Nickname = user.Nickname,
            Email = user.Email,
            Whatsapp = user.Whatsapp,
            PhotoUrl = user.PhotoUrl,
            Role = user.Role,
            IsActive = user.IsActive,
            IsDeleted = IsDeletedAccount(user),
            CreatedAt = user.CreatedAt,
            EmailConfirmedAt = user.EmailConfirmedAt,
            HasPassword = user.PasswordHash is not null,
            ExternalLogins = logins.Select(l => new AdminUserExternalLogin
            {
                Provider = ExternalLoginProviders.FromStoredName(l.Provider)?.DisplayName ?? l.Provider,
                Email = l.Email,
                ConnectedAt = l.CreatedAt,
                LastUsedAt = l.LastUsedAt,
            }).ToList(),
            EmailDeliveryEnabled = _delivery.Enabled,
            OnboardedAt = user.OnboardedAt,
            NeedsOnboarding = user.OnboardedAt is null,
            RequestedPlan = user.RequestedPlan,
            RequestedPlanAt = user.RequestedPlanAt,
            PlanRequestPending = IsRequestPending(user.RequestedPlan, effective),
            LastLoginAt = user.LastLoginAt,
            LastSeenAt = lastSeen,
            ActiveSessions = sessionRows.Count,
            Sessions = sessionRows
                .OrderByDescending(s => s.LastUsedAt ?? s.CreatedAt)
                .Select(s =>
                {
                    var parsed = UserAgentLabel.Parse(s.UserAgent);
                    return new AdminUserSession
                    {
                        Id = s.Id,
                        Label = parsed.Label,
                        DeviceType = parsed.DeviceType,
                        CreatedAt = s.CreatedAt,
                        LastUsedAt = s.LastUsedAt,
                        ExpiresAt = s.ExpiresAt,
                    };
                })
                .ToList(),
            OwnedHomeGames = owned,
            Memberships = memberships,
            Plan = new UserPlanResponse
            {
                Plan = PlanCatalog.Normalize(user.Plan) ?? PlanCatalog.Mesa,
                EffectivePlan = effective,
                ExpiresAt = user.PlanExpiresAt,
                Source = user.PlanSource,
                Note = user.PlanNote,
                UpdatedAt = user.PlanUpdatedAt,
                UpdatedBy = user.PlanUpdatedBy,
                UpdatedByName = updatedByName,
            },
            PlanHistory = planAudits.Select(ToPlanEntry).ToList(),
            AdminActions = adminAudits.Select(a => new UserAuditEntryResponse
            {
                Id = a.Id,
                Action = a.Action,
                CreatedAt = a.CreatedAt,
                ActorId = a.UserId,
                ActorName = a.UserName,
                OldValues = ParseElement(a.OldValues),
                NewValues = ParseElement(a.NewValues),
                ChangedFields = ParseElement(a.ChangedFields),
            }).ToList(),
        };
    }

    // ================================================================== editar dados

    /// <summary>
    /// <c>PUT admin/users/{id}</c>. Campo null = não mexe. E-mail novo: normalizado, e não pode ser de outra conta
    /// (sem distinguir maiúsculas) → 409. Papel novo derruba as sessões; o Admin não tira o próprio papel e o último Admin
    /// ativo não perde o papel (400). AuditLog <c>UserUpdated</c> com os campos alterados (papel com valor antigo/novo;
    /// dados pessoais só pelo nome do campo). Null = conta inexistente.
    /// </summary>
    public async Task<AdminUserDetailResponse?> UpdateAsync(
        Guid id, UpdateUserRequest request, AdminActor actor, CancellationToken ct = default)
    {
        // Tudo validado antes de abrir a transação
        string? fullName = null;
        if (request.FullName is not null)
        {
            fullName = request.FullName.Trim();
            if (fullName.Length == 0)
                throw new DomainException("Informe o nome.");
            if (fullName.Length > 200)
                throw new DomainException("O nome deve ter no máximo 200 caracteres.");
        }

        var nickname = NullIfBlank(request.Nickname);
        if (nickname is { Length: > 100 })
            throw new DomainException("O apelido deve ter no máximo 100 caracteres.");

        string? whatsapp = null;
        if (!string.IsNullOrWhiteSpace(request.Whatsapp))
        {
            // Mesma regra do perfil (PUT me): 55 + DDD + número
            if (request.Whatsapp.Any(char.IsLetter))
                throw new DomainException(PhoneNumbers.InvalidWhatsappMessage);
            whatsapp = PhoneNumbers.ToE164Br(request.Whatsapp)
                ?? throw new DomainException(PhoneNumbers.InvalidWhatsappMessage);
        }

        string? email = null;
        if (request.Email is not null)
        {
            email = UserEmail.Normalize(request.Email);
            ValidateEmail(email);
        }

        string? role = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
            role = Enum.GetNames<UserRole>().FirstOrDefault(n => string.Equals(n, request.Role.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException("Papel inválido.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;
        if (IsDeletedAccount(user))
            throw new DomainException(DeletedAccountMessage);

        var changed = new List<string>();
        string? oldRole = null;

        if (email is not null && email != user.Email)
        {
            // Mesma fila do cadastro/confirmação: ninguém cria conta com este e-mail no meio do caminho
            await EmailConfirmationService.LockEmailAsync(_db, email, ct);
            if (await UserEmail.IsTakenAsync(_db.Users, email, id, ct))
                throw new InvalidOperationException(AuthService.EmailInUseMessage);
            user.Email = email; // sempre em minúsculas no banco
            changed.Add(nameof(User.Email));
        }
        if (fullName is not null && fullName != user.FullName)
        {
            user.FullName = fullName;
            changed.Add(nameof(User.FullName));
        }
        if (request.Nickname is not null && nickname != user.Nickname)
        {
            user.Nickname = nickname;
            changed.Add(nameof(User.Nickname));
        }
        if (request.Whatsapp is not null && whatsapp != user.Whatsapp)
        {
            user.Whatsapp = whatsapp;
            changed.Add(nameof(User.Whatsapp));
        }
        if (role is not null && role != user.Role)
        {
            if (user.Role == nameof(UserRole.Admin))
            {
                if (id == actor.UserId)
                    throw new DomainException(SelfDemoteMessage);
                await EnsureAnotherActiveAdminAsync(id, ct);
            }
            oldRole = user.Role;
            user.Role = role;
            changed.Add(nameof(User.Role));
        }

        if (changed.Count > 0)
        {
            var actorName = await ActorNameAsync(actor, ct);
            AddAudit(user.Id, UserUpdatedAction,
                oldRole is null ? null : Json(new { role = oldRole }),
                oldRole is null ? null : Json(new { role }),
                Json(changed), actor, actorName);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
            {
                // Índice único em lower(email): outra conta pegou o e-mail ao mesmo tempo
                throw new InvalidOperationException(AuthService.EmailInUseMessage);
            }
            // Papel novo: o token antigo carrega o papel antigo — todos os aparelhos saem
            if (oldRole is not null)
                await _auth.RevokeAllSessionsAsync(user.Id, ct);
            // E-mail trocado pela Administração (a conta continua confirmada — quem responde pelo e-mail é o Admin): os
            // jogadores sem conta com o e-mail novo passam a ser dela; os vínculos do e-mail antigo ficam (SPEC-20261029-1725)
            if (changed.Contains(nameof(User.Email)))
                await PlayerLinkService.LinkUserAsync(_db, user, ct);
        }

        await tx.CommitAsync(ct);
        _logger.LogInformation("Administração: {Actor} editou a conta {UserId} ({Fields})",
            actor.UserId, id, string.Join(", ", changed));
        return await GetDetailAsync(id, ct);
    }

    // ================================================================== plano

    /// <summary>
    /// Troca o plano pelo caminho único da SPEC-20261023-1205 (<see cref="PlanService.ChangePlanAsync"/>: valida plano,
    /// origem e nota, grava AuditLog PlanChange com antigos/novos e invalida o cache do plano na hora) e completa a
    /// mesma linha de auditoria com o IP e o User-Agent do Admin. <paramref name="requireFutureExpiry"/>: a tela de
    /// administração exige vencimento no futuro (400); o alias <c>PUT users/{id}/plan</c> da SPEC-1205 aceita data
    /// passada (serve para simular vencimento). Null = conta inexistente.
    /// </summary>
    public async Task<User?> ChangePlanAsync(
        Guid id, ChangeUserPlanRequest request, AdminActor actor, bool requireFutureExpiry, CancellationToken ct = default)
    {
        if (requireFutureExpiry && request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw new DomainException(ExpiryInPastMessage);

        var by = actor.UserId == Guid.Empty ? (Guid?)null : actor.UserId;
        var user = await _plans.ChangePlanAsync(id, request.Plan, request.ExpiresAt, request.Source, request.Note, by, ct);
        if (user is null)
            return null;

        // Mesmo contexto (escopo da requisição): a linha que o PlanService acabou de gravar continua rastreada
        var entry = _db.AuditLogs.Local
            .Where(a => a.EntityId == id && a.Action == PlanChangeAction && a.IpAddress == null && a.UserId == by)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();
        if (entry is not null && (actor.Ip is not null || actor.UserAgent is not null))
        {
            entry.IpAddress = Cut(actor.Ip, 50);
            entry.UserAgent = Cut(actor.UserAgent, 500);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Administração: {Actor} trocou o plano da conta {UserId} para {Plan}", actor.UserId, id, user.Plan);
        return user;
    }

    // ================================================================== senha

    /// <summary>
    /// <c>POST admin/users/{id}/password</c>:
    /// <list type="bullet">
    /// <item><c>Temporary</c>: grava a senha definida pelo Admin (mín. 6), invalida os links de redefinição pendentes,
    /// derruba TODAS as sessões e — conta ainda não confirmada — descarta os cadastros pendentes presos aos links de
    /// confirmação (a senha da conta passa a ser esta). Serve também para conta só-social (passa a ter senha).</item>
    /// <item><c>Email</c>: link de redefinição de 1 h (o mesmo da SPEC-20261023-1203) enviado agora pelo
    /// <see cref="IEmailSender"/>; sem envio configurado → 409; falha do provedor → 409.</item>
    /// <item><c>Link</c>: link de uso único válido por 24 h (gerar outro revoga o anterior), devolvido para o Admin
    /// mandar por WhatsApp. O token puro nunca é gravado nem vai para o log.</item>
    /// </list>
    /// Os três gravam AuditLog <c>PasswordReset</c> com <c>{ mode }</c> (nunca a senha nem o token). Conta desativada
    /// ou excluída → 400. Null = conta inexistente.
    /// </summary>
    public async Task<AdminResetPasswordResponse?> ResetPasswordAsync(
        Guid id, AdminResetPasswordRequest request, AdminActor actor, CancellationToken ct = default)
    {
        var mode = new[] { ModeTemporary, ModeEmail, ModeLink }
            .FirstOrDefault(m => string.Equals(m, request.Mode?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException("Modo inválido. Use Temporary, Email ou Link.");

        if (mode == ModeTemporary)
        {
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
                throw new DomainException("A senha temporária deve ter pelo menos 6 caracteres.");
            if (request.Password.Length > 128)
                throw new DomainException("A senha temporária deve ter no máximo 128 caracteres.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;
        if (IsDeletedAccount(user))
            throw new DomainException(DeletedAccountMessage);
        if (!user.IsActive)
            throw new DomainException(InactiveAccountMessage);

        var actorName = await ActorNameAsync(actor, ct);
        var now = DateTimeOffset.UtcNow;

        switch (mode)
        {
            case ModeTemporary:
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                user.PasswordHash = AuthService.HashPassword(request.Password!);
                // O refresh legado na coluna do usuário viraria sessão em MigrateLegacyTokenAsync
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                await _db.PasswordResetTokens
                    .Where(t => t.UserId == id && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(t => t.RevokedAt, now)
                        .SetProperty(t => t.UpdatedAt, now), ct);
                // Conta não confirmada: nenhum cadastro pendente troca esta senha depois (D16.10)
                if (user.EmailConfirmedAt is null)
                    await _emailConfirmation.ClearPendingAsync(id, ct);
                AddAudit(id, PasswordResetAction, null, Json(new { mode }), null, actor, actorName);
                await _db.SaveChangesAsync(ct);
                await _auth.RevokeAllSessionsAsync(id, ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation("Administração: {Actor} definiu senha temporária para a conta {UserId}", actor.UserId, id);
                return new AdminResetPasswordResponse { Mode = mode };
            }

            case ModeEmail:
            {
                if (!_delivery.Enabled)
                    throw new InvalidOperationException(EmailNotConfiguredMessage);

                var (_, link) = await _passwordReset.IssueTokenAsync(id, actor.Ip, actor.UserAgent, ct);
                var message = PasswordResetEmail.Build(user.Email, link, _publicApp.BuildUrl("brand/logo-completa.png"));
                EmailSendResult result;
                try
                {
                    result = await _emailSender.SendAsync(message, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha no envio do e-mail de redefinição pela administração para {Email}",
                        PasswordResetEmail.Mask(user.Email));
                    result = new EmailSendResult(false, null, "Falha no envio.");
                }

                _db.NotificationLogs.Add(new NotificationLog
                {
                    Channel = "Email",
                    TemplateKey = PasswordResetEmail.TemplateKey,
                    Status = result.Sent ? "Sent" : "Failed",
                    SentAt = result.Sent ? DateTimeOffset.UtcNow : null,
                    ErrorMessage = result.Error,
                    Message = PasswordResetEmail.Mask(user.Email), // nunca o link nem o token
                });
                AddAudit(id, PasswordResetAction, null, Json(new { mode, sent = result.Sent }), null, actor, actorName);
                await _db.SaveChangesAsync(ct);

                if (!result.Sent)
                    throw new InvalidOperationException(EmailSendFailedMessage);
                _logger.LogInformation("Administração: {Actor} enviou e-mail de redefinição para a conta {UserId}", actor.UserId, id);
                return new AdminResetPasswordResponse { Mode = mode, SentTo = PasswordResetEmail.Mask(user.Email) };
            }

            default: // ModeLink
            {
                var (token, link) = await _passwordReset.IssueTokenAsync(
                    id, actor.Ip, actor.UserAgent, ct, PasswordResetService.AdminLinkLifetime);
                var hash = AuthService.HashToken(token);
                var expiresAt = await _db.PasswordResetTokens.AsNoTracking()
                    .Where(t => t.TokenHash == hash)
                    .Select(t => (DateTimeOffset?)t.ExpiresAt)
                    .FirstOrDefaultAsync(ct) ?? now + PasswordResetService.AdminLinkLifetime;
                AddAudit(id, PasswordResetAction, null, Json(new { mode, expiresAt }), null, actor, actorName);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Administração: {Actor} gerou link de redefinição (24 h) para a conta {UserId}", actor.UserId, id);
                return new AdminResetPasswordResponse
                {
                    Mode = mode,
                    ResetUrl = link,
                    ExpiresAt = expiresAt,
                    Whatsapp = PhoneNumbers.ToE164Br(user.Whatsapp),
                };
            }
        }
    }

    // ================================================================== status

    /// <summary>
    /// <c>PATCH admin/users/{id}/status</c>. Desativar: <c>IsActive = false</c> e todas as sessões caem (o access token
    /// para na próxima requisição); não mexe nos acessos de dealer nem nas TVs (são por torneio). O Admin não desativa a
    /// si mesmo e o último Admin ativo não é desativado (400). Reativar só liga <c>IsActive</c> (conta excluída pela
    /// própria pessoa não volta: 400). Mesmo status → nada muda. AuditLog <c>StatusChange</c>. Null = conta inexistente.
    /// </summary>
    public async Task<AdminUserDetailResponse?> SetStatusAsync(
        Guid id, bool isActive, AdminActor actor, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;

        if (user.IsActive != isActive)
        {
            if (!isActive)
            {
                if (id == actor.UserId)
                    throw new DomainException(SelfDeactivateMessage);
                if (user.Role == nameof(UserRole.Admin))
                    await EnsureAnotherActiveAdminAsync(id, ct);
            }
            else if (IsDeletedAccount(user))
            {
                throw new DomainException(DeletedAccountMessage + " Ela não pode ser reativada.");
            }

            user.IsActive = isActive;
            if (!isActive)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
            }
            var actorName = await ActorNameAsync(actor, ct);
            AddAudit(id, StatusChangeAction, Json(new { isActive = !isActive }), Json(new { isActive }), null, actor, actorName);
            await _db.SaveChangesAsync(ct);
            if (!isActive)
                await _auth.RevokeAllSessionsAsync(id, ct);
            else
                // Reativada: volta a receber os jogadores com o e-mail dela que ficaram sem conta (SPEC-20261029-1725)
                await PlayerLinkService.LinkUserAsync(_db, user, ct);
            _logger.LogInformation("Administração: {Actor} {Acao} a conta {UserId}",
                actor.UserId, isActive ? "reativou" : "desativou", id);
        }

        await tx.CommitAsync(ct);
        return await GetDetailAsync(id, ct);
    }

    // ================================================================== confirmação de e-mail (D16.7)

    /// <summary>
    /// "Confirmar manualmente": o Admin responde pelo e-mail da conta. Grava <c>email_confirmed_at</c> e, como a
    /// confirmação pelo link mais novo faria (D16.10), aplica à conta os dados do cadastro pendente mais recente (nome,
    /// apelido, WhatsApp e a senha daquele cadastro); depois revoga os links e apaga os cadastros pendentes. Se a pessoa
    /// não lembrar a senha, o Admin redefine em seguida. AuditLog <c>EmailConfirmed</c> com <c>{ via: "admin" }</c>.
    /// Já confirmada, desativada ou excluída → 400. Null = conta inexistente.
    /// </summary>
    public async Task<AdminUserDetailResponse?> ConfirmEmailAsync(Guid id, AdminActor actor, CancellationToken ct = default)
    {
        var email = await _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (email is null)
            return null;

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Mesma fila do cadastro/reenviar/confirmar pelo link: lê a conta só DEPOIS da trava
            await EmailConfirmationService.LockEmailAsync(_db, UserEmail.Normalize(email), ct);
            var user = await _db.Users.FirstAsync(u => u.Id == id, ct);
            EnsureCanTouchConfirmation(user);

            var pending = await _emailConfirmation.LatestPendingAsync(id, ct);
            if (pending is not null)
            {
                user.FullName = string.IsNullOrWhiteSpace(pending.FullName) ? user.FullName : pending.FullName;
                user.Nickname = pending.Nickname;
                user.Whatsapp = pending.Whatsapp;
                user.PasswordHash = pending.PasswordHash;
            }

            user.EmailConfirmedAt = DateTimeOffset.UtcNow;
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            // Confirmada agora: ganha o home game principal "Clube de <nome>" (SPEC-20261029-1723)
            await HomeGamePrimary.EnsureForAccountAsync(_db, user, ct);
            // ... e os jogadores cadastrados com o e-mail dela, em qualquer home game (SPEC-20261029-1725)
            await PlayerLinkService.LinkUserAsync(_db, user, ct);
            var actorName = await ActorNameAsync(actor, ct);
            AddAudit(id, EmailConfirmationService.AuditAction, null,
                Json(new { via = "admin", pendingRegistration = pending is not null }), null, actor, actorName);
            await _db.SaveChangesAsync(ct);
            await _emailConfirmation.ClearPendingAsync(id, ct);
            // Sessões não deveriam existir em conta não confirmada; se houver, caem (a senha pode ter mudado)
            await _auth.RevokeAllSessionsAsync(id, ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Administração: {Actor} confirmou manualmente o e-mail da conta {UserId}", actor.UserId, id);
        return await GetDetailAsync(id, ct);
    }

    /// <summary>
    /// "Reenviar e-mail de confirmação" pela administração: link novo de 48 h (revoga o anterior, leva o cadastro
    /// pendente mais novo) pelo mesmo <see cref="EmailConfirmationService"/> do cadastro; sem o cooldown de 2 min do
    /// reenvio público (quem pede é o Admin). Sem envio configurado → 409. AuditLog <c>EmailConfirmationResent</c>.
    /// Já confirmada, desativada ou excluída → 400. Null = conta inexistente.
    /// </summary>
    public async Task<AdminEmailSentResponse?> ResendConfirmationAsync(Guid id, AdminActor actor, CancellationToken ct = default)
    {
        var email = await _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (email is null)
            return null;

        string link;
        User user;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            await EmailConfirmationService.LockEmailAsync(_db, UserEmail.Normalize(email), ct);
            user = await _db.Users.FirstAsync(u => u.Id == id, ct);
            EnsureCanTouchConfirmation(user);
            if (!_delivery.Enabled)
                throw new InvalidOperationException(ConfirmationEmailNotConfiguredMessage);

            link = await _emailConfirmation.IssueTokenAsync(id, actor.Ip, actor.UserAgent, ct: ct);
            var actorName = await ActorNameAsync(actor, ct);
            AddAudit(id, ConfirmationResentAction, null, Json(new { via = "admin" }), null, actor, actorName);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Depois do commit: o link do e-mail só existe se o token já estiver gravado
        _emailConfirmation.QueueEmail(user.Email, link);
        _logger.LogInformation("Administração: {Actor} reenviou a confirmação de e-mail da conta {UserId}", actor.UserId, id);
        return new AdminEmailSentResponse { SentTo = PasswordResetEmail.Mask(user.Email) };
    }

    // ================================================================== apoio

    /// <summary>Conta excluída pela própria pessoa (anonimizada pelo "Meu perfil").</summary>
    public static bool IsDeletedAccount(User user) =>
        !user.IsActive && user.Email.EndsWith(DeletedEmailSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>O pedido das boas-vindas ainda não foi atendido? (plano efetivo abaixo do pedido)</summary>
    public static bool IsRequestPending(string? requestedPlan, string effectivePlan) =>
        PlanCatalog.Normalize(requestedPlan) is { } requested
        && PlanCatalog.Get(effectivePlan).Order < PlanCatalog.Get(requested).Order;

    private static void EnsureCanTouchConfirmation(User user)
    {
        if (IsDeletedAccount(user))
            throw new DomainException(DeletedAccountMessage);
        if (!user.IsActive)
            throw new DomainException(InactiveAccountMessage);
        if (user.EmailConfirmedAt is not null)
            throw new DomainException(AlreadyConfirmedMessage);
    }

    /// <summary>
    /// Trava "Admins ativos" (dentro da transação de quem chama) e exige outro Admin ativo além de
    /// <paramref name="exceptUserId"/>: o sistema nunca fica sem Admin.
    /// </summary>
    private async Task EnsureAnotherActiveAdminAsync(Guid exceptUserId, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is not null)
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminLockSpace}, 0)", ct);
        var admin = nameof(UserRole.Admin);
        var another = await _db.Users.AnyAsync(u => u.Id != exceptUserId && u.IsActive && u.Role == admin, ct);
        if (!another)
            throw new DomainException(LastAdminMessage);
    }

    private Task<string?> ActorNameAsync(AdminActor actor, CancellationToken ct) =>
        _db.Users.AsNoTracking().Where(u => u.Id == actor.UserId).Select(u => (string?)u.FullName).FirstOrDefaultAsync(ct);

    private void AddAudit(
        Guid targetUserId, string action, string? oldValues, string? newValues, string? changedFields,
        AdminActor actor, string? actorName) =>
        _db.AuditLogs.Add(new AuditLog
        {
            // AuditLog não é BaseEntity: Id e CreatedAt à mão
            Id = Guid.NewGuid(),
            EntityType = "User",
            EntityId = targetUserId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            ChangedFields = changedFields,
            UserId = actor.UserId == Guid.Empty ? null : actor.UserId,
            UserName = actorName,
            IpAddress = Cut(actor.Ip, 50),
            UserAgent = Cut(actor.UserAgent, 500),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private static void ValidateEmail(string email)
    {
        if (email.Length == 0)
            throw new DomainException("Informe o e-mail.");
        var at = email.IndexOf('@');
        if (email.Length > 200 || at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1
            || email.Any(char.IsWhiteSpace))
            throw new DomainException("Informe um e-mail válido.");
    }

    private static UserPlanHistoryEntry ToPlanEntry(AuditLog a)
    {
        var entry = new UserPlanHistoryEntry
        {
            Id = a.Id,
            Action = a.Action,
            CreatedAt = a.CreatedAt,
            ActorId = a.UserId,
            ActorName = a.UserName,
        };
        var oldValues = ParseObject(a.OldValues);
        var newValues = ParseObject(a.NewValues);
        if (a.Action == AuthService.PlanRequestedAction)
        {
            entry.RequestedPlan = Text(newValues, "plan");
            entry.FromPlan = Text(newValues, "currentPlan");
            entry.Via = Text(newValues, "via");
            return entry;
        }
        // Mesmo formato do PlanService.ChangePlanAsync e da migration de cortesia (que não grava PlanExpiresAt novo)
        entry.FromPlan = Text(oldValues, "Plan");
        entry.ToPlan = Text(newValues, "Plan");
        entry.FromSource = Text(oldValues, "PlanSource");
        entry.ToSource = Text(newValues, "PlanSource");
        entry.FromExpiresAt = Date(oldValues, "PlanExpiresAt");
        entry.ToExpiresAt = Date(newValues, "PlanExpiresAt");
        entry.FromNote = Text(oldValues, "PlanNote");
        entry.ToNote = Text(newValues, "PlanNote");
        return entry;
    }

    private static Dictionary<string, JsonElement>? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            if (element.ValueKind != JsonValueKind.Object)
                return null;
            var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
                map[property.Name] = property.Value;
            return map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(Dictionary<string, JsonElement>? map, string key) =>
        map is not null && map.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(Dictionary<string, JsonElement>? map, string key) =>
        map is not null && map.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var date)
            ? date
            : null;

    private static JsonElement? ParseElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Cut(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
