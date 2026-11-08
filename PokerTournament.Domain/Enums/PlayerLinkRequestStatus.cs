namespace PokerTournament.Domain.Enums;

/// <summary>Estado do pedido de vínculo "sou eu" (SPEC-20261108-1632); gravado como string via nameof.</summary>
public enum PlayerLinkRequestStatus
{
    Pending,  // aguardando o organizador
    Approved, // aprovado: Person.UserId gravado
    Declined, // recusado pelo organizador (ou automaticamente, quando o jogador já ficou com outra conta)
    Expired   // venceu sem decisão (24 h)
}
