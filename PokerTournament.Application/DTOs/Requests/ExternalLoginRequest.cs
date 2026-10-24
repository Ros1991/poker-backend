namespace PokerTournament.Application.DTOs.Requests;

/// <summary>Login social (SPEC-20261023-1210): <c>POST auth/external/{provider}</c>.</summary>
public class ExternalLoginRequest
{
    /// <summary>ID token (JWT) que o botão do provedor devolveu ao site.</summary>
    public string? IdToken { get; set; }
    /// <summary>Nonce emitido por <c>POST auth/external/nonce</c> e passado ao botão (uso único, 10 min).</summary>
    public string? Nonce { get; set; }
    /// <summary>
    /// Ticket de um 409 anterior que o site abandonou (a pessoa voltou e clicou no botão de novo): é descartado
    /// e o fluxo recomeça do zero. Opcional.
    /// </summary>
    public string? LinkTicket { get; set; }
}
