namespace PokerTournament.Application.DTOs.Requests;

// Pedido de vaga em torneio público (SPEC-20261108-1639).

/// <summary><c>POST me/join-requests</c>: pedir vaga num torneio público, com um recado opcional (até 500).</summary>
public class CreateJoinRequestRequest
{
    public Guid TournamentId { get; set; }
    public string? Message { get; set; }
}
