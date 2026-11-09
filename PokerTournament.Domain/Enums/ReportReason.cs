namespace PokerTournament.Domain.Enums;

/// <summary>Motivo da denúncia (SPEC-20261108-1638), lista fixa gravada via <c>nameof</c> em <c>Report.Reason</c>.</summary>
public enum ReportReason
{
    /// <summary>Propaganda ou mensagens repetidas.</summary>
    Spam,
    /// <summary>Assédio, ameaça ou ofensa.</summary>
    Harassment,
    /// <summary>Golpe ou pedido de dinheiro.</summary>
    Scam,
    /// <summary>Conteúdo impróprio.</summary>
    Inappropriate,
    Other
}

/// <summary>Situação da denúncia na Administração: aberta ou já revisada.</summary>
public enum ReportStatus
{
    Open,
    Reviewed
}
