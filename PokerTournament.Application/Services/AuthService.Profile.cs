using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// "Meu perfil" (SPEC-20261023-1211, pesquisa §11): a conta logada edita os próprios dados, troca ou define a senha,
/// conecta/desconecta o Google, vê e derruba os aparelhos conectados e exclui a conta. Tudo é da PRÓPRIA conta (o id
/// vem do token; nenhum método recebe id de outra pessoa) e cada ação grava um <see cref="AuditLog"/> sem dados
/// pessoais no corpo (só nomes de campos e contagens). O token de dealer nunca chega aqui (MeController sem
/// [AllowTokenKind] → 403).
/// </summary>
public partial class AuthService
{
    // AuditLog.Action das ações do perfil
    public const string ProfileUpdatedAction = "ProfileUpdated";
    public const string ProfilePhotoChangedAction = "ProfilePhotoChanged";
    public const string ProfilePhotoRemovedAction = "ProfilePhotoRemoved";
    public const string PasswordChangedAction = "PasswordChanged";
    public const string PasswordSetupLinkAction = "PasswordSetupLinkRequested";
    public const string ExternalLoginUnlinkedAction = "ExternalLoginUnlinked";
    public const string SessionRevokedAction = "SessionRevoked";
    public const string OtherSessionsRevokedAction = "OtherSessionsRevoked";
    public const string AccountDeletedAction = "AccountDeleted";

    /// <summary>Nome que a conta excluída passa a ter (anonimização).</summary>
    public const string DeletedAccountName = "Conta excluída";
    /// <summary>Texto que a pessoa digita para confirmar a exclusão.</summary>
    public const string DeleteConfirmationWord = "EXCLUIR";

    public const string UserNotFoundMessage = "Usuário não encontrado.";
    public const string WrongCurrentPasswordMessage = "Senha atual incorreta.";
    public const string NoPasswordYetMessage =
        "Sua conta ainda não tem senha. Use \"Definir senha\" para receber o link por e-mail.";
    public const string AlreadyHasPasswordMessage = "Sua conta já tem senha. Para mudar, use \"Trocar senha\".";
    public const string SamePasswordMessage = "A nova senha precisa ser diferente da atual.";
    public const string NewPasswordTooShortMessage = "A nova senha deve ter pelo menos 6 caracteres.";
    public const string DeleteConfirmationMessage = "Digite EXCLUIR para confirmar a exclusão da conta.";
    public const string DeleteWrongPasswordMessage = "Senha incorreta.";
    public const string DeleteNeedsGoogleMessage =
        "Sua conta não tem senha: entre com o Google de novo para confirmar a exclusão.";
    public const string DeleteReauthFailedMessage =
        "Não foi possível confirmar com o Google. Entre com o Google de novo e tente excluir em seguida.";
    public const string DeleteAdminBlockedMessage =
        "Conta de administrador do sistema não pode ser excluída por aqui. Fale com o suporte.";

    /// <summary>Intervalo mínimo entre dois links de "Definir senha".</summary>
    public static readonly TimeSpan PasswordSetupCooldown = TimeSpan.FromMinutes(2);
    /// <summary>Conta sem senha: o login com o Google que confirma a exclusão precisa ter acontecido há menos disto.</summary>
    public static readonly TimeSpan RecentLoginWindow = TimeSpan.FromMinutes(5);

    private const string PrivateRelaySuffix = "@privaterelay.appleid.com";
    // audit_logs.changed_fields é jsonb: lista JSON com os nomes dos campos
    private const string PhotoUrlField = """["PhotoUrl"]""";

    public static string OwnerOfHomeGamesMessage(int count) => count == 1
        ? "Você é dono de 1 home game. Exclua-o antes ou fale com o suporte para transferir."
        : $"Você é dono de {count} home games. Exclua-os antes ou fale com o suporte para transferir.";

    // ---------------------------------------------------------------- dados

    /// <summary>Conta ativa (rastreada pelo contexto) ou null.</summary>
    public Task<User?> FindActiveUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

    public async Task<ProfileResponse?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        return user is null ? null : await ToProfileAsync(user, ct);
    }

    public async Task<ProfileResponse> ToProfileAsync(User user, CancellationToken ct = default)
    {
        var owned = await _db.HomeGames.CountAsync(h => h.OwnerId == user.Id, ct);
        return new ProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Nickname = user.Nickname,
            Whatsapp = user.Whatsapp,
            PhotoUrl = user.PhotoUrl,
            Role = user.Role,
            HasPassword = user.PasswordHash is not null,
            EmailIsPrivateRelay = user.Email.EndsWith(PrivateRelaySuffix, StringComparison.OrdinalIgnoreCase),
            EmailConfirmedAt = user.EmailConfirmedAt,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            OwnedHomeGames = owned,
            EffectivePlan = PlanCatalog.EffectivePlan(user),
            // Boas-vindas (SPEC-20261023-1212, AuthService.Onboarding.cs)
            NeedsOnboarding = user.OnboardedAt is null,
            OnboardedAt = user.OnboardedAt,
            PhotoFromProvider = IsProviderPhoto(user.PhotoUrl),
            RequestedPlan = user.RequestedPlan,
            RequestedPlanAt = user.RequestedPlanAt,
        };
    }

    /// <summary>
    /// <c>PUT me</c>: nome (obrigatório, até 200), apelido (até 100; vazio = sem) e WhatsApp (vazio = sem; senão
    /// 55 + DDD + número, pela mesma regra do <see cref="PhoneNumbers"/>). O AuditLog guarda só QUAIS campos mudaram.
    /// </summary>
    public async Task<ProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateProfileRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var nickname = NullIfBlank(request.Nickname);
        string? whatsapp = null;
        if (fullName.Length == 0)
            throw new DomainException("Informe o nome.");
        if (fullName.Length > 200)
            throw new DomainException("O nome deve ter no máximo 200 caracteres.");
        if (nickname is { Length: > 100 })
            throw new DomainException("O apelido deve ter no máximo 100 caracteres.");
        if (!string.IsNullOrWhiteSpace(request.Whatsapp))
        {
            if (request.Whatsapp.Any(char.IsLetter))
                throw new DomainException(PhoneNumbers.InvalidWhatsappMessage);
            whatsapp = PhoneNumbers.ToE164Br(request.Whatsapp)
                ?? throw new DomainException(PhoneNumbers.InvalidWhatsappMessage);
        }

        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var changed = new List<string>();
        if (user.FullName != fullName) { user.FullName = fullName; changed.Add(nameof(User.FullName)); }
        if (user.Nickname != nickname) { user.Nickname = nickname; changed.Add(nameof(User.Nickname)); }
        if (user.Whatsapp != whatsapp) { user.Whatsapp = whatsapp; changed.Add(nameof(User.Whatsapp)); }

        if (changed.Count > 0)
        {
            AddProfileAudit(user, ProfileUpdatedAction, null, Json(changed), ip, userAgent);
            await _db.SaveChangesAsync(ct);
        }
        return await ToProfileAsync(user, ct);
    }

    /// <summary>
    /// <c>POST me/photo</c>: foto da conta pelo MESMO upload das fotos de jogador (<see cref="PhotoService.SaveAsync"/>:
    /// JPEG/PNG/WebP até 2 MB, nome de arquivo novo a cada troca, pasta <c>/uploads/photos</c>). O site manda o
    /// recorte quadrado do PhotoCropDialog e mostra sempre redondo. Devolve também a foto anterior (quem chama apaga
    /// o arquivo, se for nosso).
    /// </summary>
    public async Task<(ProfileResponse Profile, string? PreviousPhoto)> SetPhotoAsync(
        Guid userId, PhotoService photos, Stream data, long length,
        string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var previous = user.PhotoUrl;
        await photos.SaveAsync(UploadsOptions.PhotosFolder, user.Id, data, length, ImageFiles.InvalidPhotoMessage,
            url => user.PhotoUrl = url,
            () => AddProfileAudit(user, ProfilePhotoChangedAction, null, PhotoUrlField, ip, userAgent), ct);
        return (await ToProfileAsync(user, ct), previous);
    }

    /// <summary><c>DELETE me/photo</c>: tira a foto (PhotoUrl = null). Devolve a foto anterior para apagar o arquivo.</summary>
    public async Task<(ProfileResponse Profile, string? PreviousPhoto)> RemovePhotoAsync(
        Guid userId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var previous = user.PhotoUrl;
        if (previous is not null)
        {
            user.PhotoUrl = null;
            AddProfileAudit(user, ProfilePhotoRemovedAction, null, PhotoUrlField, ip, userAgent);
            await _db.SaveChangesAsync(ct);
        }
        return (await ToProfileAsync(user, ct), previous);
    }

    /// <summary>
    /// AuditLog de uma ação do perfil (sem dados pessoais: só campos mudados e contagens). <paramref name="newValues"/> e
    /// <paramref name="changedFields"/> são JSON (colunas jsonb).
    /// </summary>
    public void AddProfileAudit(
        User user, string action, string? newValues, string? changedFields, string? ip, string? userAgent) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "User",
            EntityId = user.Id,
            Action = action,
            NewValues = newValues,
            ChangedFields = changedFields,
            UserId = user.Id,
            UserName = user.FullName,
            IpAddress = Cut(ip, 50),
            UserAgent = Cut(userAgent, 500),
            CreatedAt = DateTimeOffset.UtcNow
        });

    // ---------------------------------------------------------------- senha

    /// <summary>
    /// <c>POST me/password</c>: exige a senha atual (errada conta no <see cref="LoginAttemptLimiter"/>, como no login;
    /// estourou → 429). Grava a nova, invalida os links de "Esqueci minha senha" pendentes e derruba os OUTROS
    /// aparelhos — a sessão deste (<paramref name="currentSessionId"/>) continua. Conta sem senha → 400 (use o link).
    /// </summary>
    public async Task<ChangePasswordResponse> ChangePasswordAsync(
        Guid userId, Guid? currentSessionId, ChangePasswordRequest request,
        string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        if (user.PasswordHash is null)
            throw new DomainException(NoPasswordYetMessage);
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 6)
            throw new DomainException(NewPasswordTooShortMessage);

        var attempt = _loginLimiter.Reserve(ip, user.Email);
        var wrongPassword = false;
        try
        {
            if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                wrongPassword = true;
                throw new DomainException(WrongCurrentPasswordMessage);
            }
            if (VerifyPassword(request.NewPassword, user.PasswordHash))
                throw new DomainException(SamePasswordMessage);

            var now = DateTimeOffset.UtcNow;
            int revoked;
            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                user.PasswordHash = HashPassword(request.NewPassword);
                // O refresh legado na coluna do usuário viraria sessão no próximo refresh (MigrateLegacyTokenAsync)
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                // Um link de "Esqueci minha senha" pedido antes não desfaz a troca
                await _db.PasswordResetTokens
                    .Where(t => t.UserId == user.Id && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(t => t.RevokedAt, now)
                        .SetProperty(t => t.UpdatedAt, now), ct);
                revoked = await RevokeOtherSessionsCoreAsync(user.Id, currentSessionId, ct);
                AddProfileAudit(user, PasswordChangedAction, Json(new { otherSessionsRevoked = revoked }), null, ip, userAgent);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            // Senha certa: as erradas desta conta neste IP deixam de pesar
            attempt.Succeeded(clearIpAndEmail: true);
            _logger.LogInformation("Senha trocada pelo perfil: usuário {UserId}, {Count} outro(s) aparelho(s) saíram", user.Id, revoked);
            return new ChangePasswordResponse
            {
                Message = revoked > 0
                    ? "Senha trocada. Os outros aparelhos saíram da conta; este continua conectado."
                    : "Senha trocada.",
                OtherSessionsRevoked = revoked
            };
        }
        finally
        {
            if (!wrongPassword)
                attempt.Succeeded();
        }
    }

    /// <summary>
    /// Antes do <c>POST me/password/setup-link</c> (conta só-Google): confere que a conta não tem senha e que não
    /// saiu link nos últimos 2 minutos (→ 429), grava o AuditLog e devolve o e-mail da conta. O token e o e-mail
    /// ficam com quem chama (PasswordResetService.IssueTokenAsync + AccountEmailService — o link é o da SPEC-1203).
    /// </summary>
    public async Task<string> PreparePasswordSetupAsync(
        Guid userId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        if (user.PasswordHash is not null)
            throw new DomainException(AlreadyHasPasswordMessage);

        var now = DateTimeOffset.UtcNow;
        var last = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (DateTimeOffset?)t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (last is { } at && now - at < PasswordSetupCooldown)
            throw new TooManyPasswordSetupLinksException((int)Math.Ceiling((at + PasswordSetupCooldown - now).TotalSeconds));

        AddProfileAudit(user, PasswordSetupLinkAction, null, null, ip, userAgent);
        await _db.SaveChangesAsync(ct);
        return user.Email;
    }

    // ---------------------------------------------------------------- contas conectadas

    /// <summary><c>GET me/logins</c>: senha sim/não e, por provedor, se está conectado e se dá para desconectar.</summary>
    public async Task<ExternalLoginsResponse> GetExternalLoginsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var logins = await _db.UserLogins.AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.LastUsedAt ?? l.CreatedAt)
            .ToListAsync(ct);

        var hasPassword = user.PasswordHash is not null;
        var response = new ExternalLoginsResponse { HasPassword = hasPassword };
        foreach (var provider in ExternalLoginProviders.All)
        {
            var clientId = _external.ClientIdFor(provider.Provider);
            var mine = logins.Where(l => l.Provider == provider.StoredName).ToList();
            // Provedor desligado (Apple hoje) só aparece se já estiver conectado (dá para desconectar)
            if (clientId is null && mine.Count == 0)
                continue;
            var others = logins.Count(l => l.Provider != provider.StoredName);
            response.Providers.Add(new ExternalLoginItem
            {
                Provider = provider.Key,
                DisplayName = provider.DisplayName,
                Enabled = clientId is not null,
                ClientId = clientId,
                Connected = mine.Count > 0,
                Accounts = mine.Select(l => new ExternalLoginAccount
                {
                    Email = l.Email,
                    ConnectedAt = l.CreatedAt,
                    LastUsedAt = l.LastUsedAt,
                }).ToList(),
                CanDisconnect = mine.Count > 0 && (hasPassword || others > 0),
            });
        }
        return response;
    }

    /// <summary>
    /// <c>POST me/logins/{provider}</c>: conecta o Google à conta logada com o MESMO validador da SPEC-1210 (assinatura,
    /// emissor, audience, validade, nonce emitido pela API e gasto aqui). A identidade é o <c>sub</c>: o e-mail da conta
    /// Google pode ser outro. Login do provedor já ligado a outra conta → 400; esta conta já tem um do provedor → 400;
    /// já era desta conta → 200 sem mudar nada. Avisa por e-mail ("O Google foi conectado à sua conta").
    /// </summary>
    public async Task<ExternalLoginsResponse> ConnectExternalLoginAsync(
        Guid userId, string providerKey, ConnectExternalLoginRequest request,
        string? ip, string? userAgent, CancellationToken ct = default)
    {
        var provider = ExternalLoginProviders.FindEnabled(providerKey, _external)
            ?? throw new KeyNotFoundException("Provedor de login não disponível.");
        var failure = $"Não foi possível conectar {provider.WithArticle}. Tente de novo.";

        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var identity = await ValidateProviderTokenAsync(provider, request.IdToken, request.Nonce, failure, ct);

        var now = DateTimeOffset.UtcNow;
        var current = await _db.UserLogins
            .FirstOrDefaultAsync(l => l.Provider == provider.StoredName && l.Subject == identity.Subject, ct);
        if (current is not null)
        {
            if (current.UserId != user.Id)
                throw new DomainException($"Esta conta {provider.DisplayName} já está conectada a outra conta do Poker da Galera.");
            // Já era desta conta (outra aba concluiu antes): só atualiza o último uso
            current.LastUsedAt = now;
            await _db.SaveChangesAsync(ct);
            return await GetExternalLoginsAsync(user.Id, ct);
        }

        if (await _db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.Provider == provider.StoredName, ct))
            throw new DomainException(
                $"Sua conta já tem uma conta {provider.DisplayName} conectada. Desconecte-a antes de conectar outra.");

        var email = UserEmail.Normalize(identity.Email);
        AddLogin(user, provider, identity.Subject, identity.EmailVerified && IsPlausibleEmail(email) ? email : null, now);
        AddAudit(user, ExternalLoginLinkedAction, Json(new { provider = provider.StoredName, via = "profile" }), ip, userAgent, now);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: "23505" })
        {
            // O mesmo login do provedor foi conectado a outra conta no meio do caminho
            throw new DomainException($"Esta conta {provider.DisplayName} já está conectada a outra conta do Poker da Galera.");
        }

        _logger.LogInformation("{Provider} conectado pelo perfil ao usuário {UserId}", provider.DisplayName, user.Id);
        // Depois do commit: o aviso só sai se o vínculo existe de verdade
        _externalEmail.QueueLinkedNotice(user, provider);
        return await GetExternalLoginsAsync(user.Id, ct);
    }

    /// <summary>
    /// <c>DELETE me/logins/{provider}</c>: desconecta (apaga) os logins do provedor. Bloqueado (400) quando é a única
    /// forma de entrar: conta sem senha e sem login de outro provedor. Provedor desligado também pode ser desconectado.
    /// </summary>
    public async Task<ExternalLoginsResponse> DisconnectExternalLoginAsync(
        Guid userId, string providerKey, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var provider = ExternalLoginProviders.All
            .FirstOrDefault(p => string.Equals(p.Key, providerKey?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Provedor de login desconhecido.");

        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var logins = await _db.UserLogins.Where(l => l.UserId == user.Id).ToListAsync(ct);
        var mine = logins.Where(l => l.Provider == provider.StoredName).ToList();
        if (mine.Count == 0)
            throw new DomainException($"Nenhuma conta {provider.DisplayName} está conectada.");
        if (user.PasswordHash is null && logins.Count == mine.Count)
            throw new DomainException(
                $"Não dá para desconectar {provider.WithArticle}: é a única forma de entrar na sua conta. Defina uma senha antes.");

        _db.UserLogins.RemoveRange(mine);
        AddAudit(user, ExternalLoginUnlinkedAction, Json(new { provider = provider.StoredName, count = mine.Count }), ip, userAgent, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("{Provider} desconectado pelo perfil do usuário {UserId}", provider.DisplayName, user.Id);
        return await GetExternalLoginsAsync(user.Id, ct);
    }

    /// <summary>
    /// ID token do provedor + nonce, pelas mesmas regras do login social: validador OIDC, nonce do token = nonce
    /// enviado, nonce emitido por esta API e ainda não gasto (gasta agora). Qualquer falha → 400 com
    /// <paramref name="failureMessage"/>.
    /// </summary>
    private async Task<ExternalIdentity> ValidateProviderTokenAsync(
        ExternalProviderDefinition provider, string? idToken, string? nonce, string failureMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(nonce))
            throw new DomainException(failureMessage);
        var clientId = _external.ClientIdFor(provider.Provider) ?? throw new DomainException(failureMessage);
        var identity = await _oidc.ValidateAsync(provider, clientId, idToken, ct)
            ?? throw new DomainException(failureMessage);
        if (identity.Nonce is null || !SameText(identity.Nonce, nonce) || !_externalStore.ConsumeNonce(nonce))
        {
            _logger.LogInformation("{Provider} pelo perfil recusado: nonce diferente, reusado ou vencido", provider.DisplayName);
            throw new DomainException(failureMessage);
        }
        return identity;
    }

    // ---------------------------------------------------------------- aparelhos

    /// <summary><c>GET me/sessions</c>: sessões ativas (não revogadas e no prazo), a deste aparelho primeiro.</summary>
    public async Task<List<SessionResponse>> GetSessionsAsync(
        Guid userId, Guid? currentSessionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => new { s.Id, s.UserAgent, s.CreatedAt, s.LastUsedAt })
            .ToListAsync(ct);

        return sessions
            .Select(s =>
            {
                var parsed = UserAgentLabel.Parse(s.UserAgent);
                return new SessionResponse
                {
                    Id = s.Id,
                    Label = parsed.Label,
                    Browser = parsed.Browser,
                    Platform = parsed.Platform,
                    DeviceType = parsed.DeviceType,
                    CreatedAt = s.CreatedAt,
                    LastUsedAt = s.LastUsedAt,
                    Current = s.Id == currentSessionId,
                };
            })
            .OrderByDescending(s => s.Current)
            .ThenByDescending(s => s.LastUsedAt ?? s.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// <c>DELETE me/sessions/{id}</c>: tira um aparelho da conta (o access token dele para de valer em até 60 s —
    /// na hora nesta instância). Falso = sessão inexistente, de outra conta ou já encerrada.
    /// </summary>
    public async Task<bool> RevokeSessionAsync(
        Guid userId, Guid sessionId, Guid? currentSessionId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var now = DateTimeOffset.UtcNow;
        var changed = await _db.AuthSessions
            .Where(s => s.Id == sessionId && s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct);
        if (changed == 0)
            return false;
        MarkSessionInactive(sessionId);
        AddProfileAudit(user, SessionRevokedAction, Json(new { current = sessionId == currentSessionId }), null, ip, userAgent);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary><c>POST me/sessions/revoke-others</c>: todos os aparelhos saem, menos este. Devolve quantos saíram.</summary>
    public async Task<int> RevokeOtherSessionsAsync(
        Guid userId, Guid? currentSessionId, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var revoked = await RevokeOtherSessionsCoreAsync(user.Id, currentSessionId, ct);
        AddProfileAudit(user, OtherSessionsRevokedAction, Json(new { count = revoked }), null, ip, userAgent);
        await _db.SaveChangesAsync(ct);
        return revoked;
    }

    /// <summary>Revoga as sessões ativas do usuário, menos <paramref name="keepSessionId"/>, e marca no cache na hora.</summary>
    private async Task<int> RevokeOtherSessionsCoreAsync(Guid userId, Guid? keepSessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (keepSessionId == null || s.Id != keepSessionId))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (ids.Count == 0)
            return 0;

        await _db.AuthSessions
            .Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct);
        foreach (var id in ids)
            MarkSessionInactive(id);
        return ids.Count;
    }

    // ---------------------------------------------------------------- excluir conta

    /// <summary>
    /// <c>DELETE me</c> (LGPD, arts. 15 e 16): bloqueado para dono de home game (400 com a contagem) e para Admin do
    /// sistema. Confirmação: "EXCLUIR" + a senha (conta com senha; errada conta no limitador do login) ou, em conta
    /// sem senha, um ID token do Google de um login desta conta emitido há menos de 5 min (nonce da API, gasto aqui).
    /// Exclusão = anonimização: nome "Conta excluída", apelido/WhatsApp/foto apagados, e-mail trocado por um marcador
    /// único inválido (o endereço fica livre para um cadastro novo), senha e logins externos removidos, links
    /// pendentes revogados, participação nos home games de outras pessoas removida, todas as sessões revogadas,
    /// IsActive = false e o nome tirado dos AuditLogs e dos registros de uso de IA (ai_usages.actor_name) da própria
    /// conta; grava <c>AccountDeleted</c>. Os jogadores
    /// (Person) dos home games ficam (são dos organizadores). Devolve a foto anterior (quem chama apaga o arquivo).
    /// </summary>
    public async Task<string?> DeleteAccountAsync(
        Guid userId, DeleteAccountRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        if (!string.Equals((request.Confirmation ?? string.Empty).Trim(), DeleteConfirmationWord, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(DeleteConfirmationMessage);
        if (user.Role == "Admin")
            throw new DomainException(DeleteAdminBlockedMessage);

        var owned = await _db.HomeGames.CountAsync(h => h.OwnerId == user.Id, ct);
        if (owned > 0)
            throw new DomainException(OwnerOfHomeGamesMessage(owned));

        var via = user.PasswordHash is not null
            ? ConfirmDeletionWithPassword(user, request.Password, ip)
            : await ConfirmDeletionWithProviderAsync(user, request.IdToken, request.Nonce, ct);

        var previousPhoto = user.PhotoUrl;
        var now = DateTimeOffset.UtcNow;
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            // Links pendentes: nenhum "Esqueci minha senha" ou confirmação de e-mail reabre a conta
            await _db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.UsedAt == null && t.RevokedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), ct);
            await _emailConfirmation.ClearPendingAsync(user.Id, ct);
            await _db.UserLogins.Where(l => l.UserId == user.Id).ExecuteDeleteAsync(ct);
            await _db.HomeGameMembers.Where(m => m.UserId == user.Id).ExecuteDeleteAsync(ct);
            // O nome não fica nos registros de auditoria da própria conta
            await _db.AuditLogs
                .Where(a => a.UserId == user.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.UserName, DeletedAccountName), ct);
            // Nem nos registros de uso de IA (SPEC-20261027-1312): o nome copiado em ai_usages.actor_name no momento do
            // uso ("Tirar o fundo" no aparelho e as chamadas pagas) aparece na tela de IA da administração
            await _db.AiUsages
                .Where(u => u.UserId == user.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(u => u.ActorName, DeletedAccountName)
                    .SetProperty(u => u.UpdatedAt, now), ct);

            user.FullName = DeletedAccountName;
            user.Nickname = null;
            user.Whatsapp = null;
            user.PhotoUrl = null;
            user.Email = $"conta-excluida-{user.Id:N}@excluida.invalid";
            user.PasswordHash = null;
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            user.IsActive = false;
            AddProfileAudit(user, AccountDeletedAction, Json(new { via }), null, ip, userAgent);
            await _db.SaveChangesAsync(ct);

            await RevokeAllSessionsAsync(user.Id, ct);
            await tx.CommitAsync(ct);
        }

        _logger.LogInformation("Conta excluída (anonimizada) pelo perfil: usuário {UserId}", user.Id);
        return previousPhoto;
    }

    /// <summary>Senha da conta (errada conta no <see cref="LoginAttemptLimiter"/>, como no login; estourou → 429).</summary>
    private string ConfirmDeletionWithPassword(User user, string? password, string? ip)
    {
        var attempt = _loginLimiter.Reserve(ip, user.Email);
        if (!VerifyPassword(password, user.PasswordHash!))
            throw new DomainException(DeleteWrongPasswordMessage); // a reserva fica: senha errada conta
        attempt.Succeeded();
        return "password";
    }

    /// <summary>
    /// Conta sem senha: o ID token tem que ser de um login desta conta (provedor + sub em user_logins), válido pelo
    /// validador OIDC, com o nonce desta API (gasto aqui) e emitido (<c>iat</c>) há menos de 5 minutos.
    /// </summary>
    private async Task<string> ConfirmDeletionWithProviderAsync(
        User user, string? idToken, string? nonce, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(nonce))
            throw new DomainException(DeleteNeedsGoogleMessage);

        var linked = await _db.UserLogins.AsNoTracking().Where(l => l.UserId == user.Id).ToListAsync(ct);
        foreach (var provider in ExternalLoginProviders.All)
        {
            if (_external.ClientIdFor(provider.Provider) is not { } clientId
                || !linked.Any(l => l.Provider == provider.StoredName))
                continue;
            var identity = await _oidc.ValidateAsync(provider, clientId, idToken, ct);
            if (identity is null)
                continue; // token de outro provedor (ou inválido): tenta o próximo
            if (identity.Nonce is null || !SameText(identity.Nonce, nonce) || !_externalStore.ConsumeNonce(nonce))
                throw new DomainException(DeleteReauthFailedMessage);
            if (!linked.Any(l => l.Provider == provider.StoredName && l.Subject == identity.Subject))
                throw new DomainException(DeleteReauthFailedMessage); // conta do provedor que não é desta conta
            if (!IssuedRecently(idToken))
                throw new DomainException(DeleteReauthFailedMessage);
            return provider.Key;
        }
        throw new DomainException(DeleteReauthFailedMessage);
    }

    /// <summary><c>iat</c> do token (já validado) há menos de 5 min — 1 min de tolerância para relógio adiantado.</summary>
    private static bool IssuedRecently(string idToken)
    {
        DateTime issuedAt;
        try
        {
            issuedAt = new JsonWebTokenHandler().ReadJsonWebToken(idToken).IssuedAt;
        }
        catch (ArgumentException)
        {
            return false;
        }
        if (issuedAt == DateTime.MinValue)
            return false;
        var iat = new DateTimeOffset(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc));
        var now = DateTimeOffset.UtcNow;
        return iat > now - RecentLoginWindow && iat <= now + TimeSpan.FromMinutes(1);
    }
}
