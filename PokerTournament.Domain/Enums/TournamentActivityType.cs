namespace PokerTournament.Domain.Enums;

/// <summary>Tipos de atividade do torneio que não envolvem dinheiro (histórico do terminal).</summary>
public enum TournamentActivityType
{
    TablesDrawn,        // mesas sorteadas (ou sorteadas de novo)
    PlayerMoved,        // jogador mudou de mesa/lugar (inclusive balanceamento e quebra de mesa)
    PlayersSwapped,     // dois jogadores trocaram de lugar
    PlayerSeated,       // jogador inscrito sentou sozinho num lugar livre (inscrição em lote)
    TableBroken,        // mesa quebrada e jogadores redistribuídos
    PlayerRemoved,      // inscrição desfeita
    PlayerCreated,      // jogador novo cadastrado pelo torneio (painel ou terminal) — SPEC-20261023-1213
    PlayerPhotoChanged  // foto do jogador posta/trocada pelo torneio — SPEC-20261023-1213
}
