namespace PokerTournament.Application.DTOs.Requests;

// Pedido de vínculo "sou eu" (SPEC-20261108-1632).

/// <summary><c>POST invite/{code}/claim</c>: "sou eu" (<see cref="PersonId"/>) ou "não estou na lista" (<see cref="NotInList"/>).</summary>
public class ClaimInviteRequest
{
    public Guid? PersonId { get; set; }
    public bool NotInList { get; set; }
}

/// <summary>
/// <c>POST tournaments/{id}/link-requests/{reqId}/approve</c>: no pedido "não estou na lista" o organizador pode indicar
/// o jogador (já cadastrado, sem conta) a vincular; no pedido com jogador escolhido o campo é ignorado.
/// </summary>
public class ApproveLinkRequest
{
    public Guid? PersonId { get; set; }
}
