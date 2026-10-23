namespace PokerTournament.Application.DTOs.Requests;

/// <summary>
/// PUT api/v1/users/{id}/plan (Admin, SPEC-20261023-1205): o suporte atribui o plano na fase de testes.
/// Plan: Mesa | Clube | Liga. Source: Manual | Cortesia | Trial | Assinatura (vazio = Manual).
/// ExpiresAt nulo = sem vencimento. Note até 200 caracteres (ex.: "fundador").
/// </summary>
public class ChangePlanRequest
{
    public string Plan { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Source { get; set; }
    public string? Note { get; set; }
}
