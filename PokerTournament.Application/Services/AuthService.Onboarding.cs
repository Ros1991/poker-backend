using Microsoft.Extensions.Logging;
using PokerTournament.Application.Common;
using PokerTournament.Application.DTOs.Requests;
using PokerTournament.Application.DTOs.Responses;
using PokerTournament.Application.Plans;
using PokerTournament.Domain.Entities;
using PokerTournament.Domain.Exceptions;

namespace PokerTournament.Application.Services;

/// <summary>
/// Boas-vindas no primeiro acesso (SPEC-20261023-1212). Conta nova (cadastro por senha confirmado ou entrada pelo
/// Google) nasce com <see cref="User.OnboardedAt"/> nulo — o <c>GET me</c> devolve <c>needsOnboarding</c> e o site leva
/// a <c>/boas-vindas</c>; contas anteriores (inclusive as fundadoras) foram marcadas pela migration. O
/// <c>POST me/onboarding</c> grava apelido/WhatsApp/foto do Google e a escolha do plano:
/// <list type="bullet">
/// <item>Mesa: começa na hora (é o plano de toda conta nova);</item>
/// <item>Clube/Liga: só registra o pedido (<see cref="User.RequestedPlan"/>, <see cref="User.RequestedPlanAt"/> e
/// AuditLog <c>PlanRequested</c>) — o plano continua o mesmo até o Admin trocar (<c>PUT users/{id}/plan</c>).</item>
/// </list>
/// Tudo é da PRÓPRIA conta (id do token); os AuditLogs não levam dados pessoais (só nomes de campos e o plano).
/// </summary>
public partial class AuthService
{
    public const string OnboardingCompletedAction = "OnboardingCompleted";
    public const string PlanRequestedAction = "PlanRequested";
    public const string OnboardingPlanRequiredMessage = "Escolha um plano: Mesa, Clube ou Liga.";

    /// <summary>
    /// <c>POST me/onboarding</c>. Campo null = não mexe (o "Fazer depois" manda só o plano); texto vazio = apaga.
    /// Validação → 400 (plano ausente/desconhecido, apelido acima de 100, WhatsApp sem DDD). Pode ser repetido: a
    /// data das boas-vindas fica a primeira; um novo pedido de Clube/Liga atualiza o pedido.
    /// </summary>
    public async Task<ProfileResponse> CompleteOnboardingAsync(
        Guid userId, OnboardingRequest request, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var plan = PlanCatalog.Normalize(request.Plan)
            ?? throw new DomainException(OnboardingPlanRequiredMessage);

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

        var user = await FindActiveUserAsync(userId, ct) ?? throw new KeyNotFoundException(UserNotFoundMessage);
        var now = DateTimeOffset.UtcNow;

        var changed = new List<string>();
        if (request.Nickname is not null && user.Nickname != nickname)
        {
            user.Nickname = nickname;
            changed.Add(nameof(User.Nickname));
        }
        if (request.Whatsapp is not null && user.Whatsapp != whatsapp)
        {
            user.Whatsapp = whatsapp;
            changed.Add(nameof(User.Whatsapp));
        }
        if (changed.Count > 0)
            AddProfileAudit(user, ProfileUpdatedAction, Json(new { via = "onboarding" }), Json(changed), ip, userAgent);

        // A foto do Google já é a da conta desde a entrada pelo Google (FillPhoto): "não usar" tira; foto enviada pelo
        // site nunca sai por aqui
        var photoRemoved = false;
        if (request.UseProviderPhoto == false && IsProviderPhoto(user.PhotoUrl))
        {
            user.PhotoUrl = null;
            photoRemoved = true;
            AddProfileAudit(user, ProfilePhotoRemovedAction, Json(new { via = "onboarding" }), PhotoUrlField, ip, userAgent);
        }

        // Clube/Liga: só o pedido (o plano muda pelo Admin); Mesa: nada a fazer, é o plano de toda conta nova
        if (plan != PlanCatalog.Mesa)
        {
            user.RequestedPlan = plan;
            user.RequestedPlanAt = now;
            AddProfileAudit(user, PlanRequestedAction,
                Json(new { plan, currentPlan = PlanCatalog.EffectivePlan(user), via = "onboarding" }), null, ip, userAgent);
        }

        if (user.OnboardedAt is null)
        {
            user.OnboardedAt = now;
            AddProfileAudit(user, OnboardingCompletedAction,
                Json(new { plan, profileFields = changed.Count, photoRemoved }), null, ip, userAgent);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Boas-vindas: usuário {UserId} escolheu {Plan} ({Fields} campo(s) do perfil, foto do Google removida: {PhotoRemoved})",
            user.Id, plan, changed.Count, photoRemoved);
        return await ToProfileAsync(user, ct);
    }

    /// <summary>
    /// Foto que veio do provedor (Google): URL https absoluta. As enviadas pelo site são caminhos nossos
    /// (<c>/uploads/photos/...</c>).
    /// </summary>
    public static bool IsProviderPhoto(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
