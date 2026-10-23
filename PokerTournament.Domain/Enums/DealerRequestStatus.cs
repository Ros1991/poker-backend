namespace PokerTournament.Domain.Enums;

public enum DealerRequestStatus
{
    Pending,  // aguardando o organizador autorizar
    Approved, // autorizado: o aparelho que pediu recebe o token
    Rejected, // recusado pelo organizador, ou pedido vencido sem decisão (15 min)
    Revoked   // desconectado pelo organizador (um a um ou ao revogar o QR)
}
