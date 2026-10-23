namespace PokerTournament.Domain.Enums;

/// <summary>Tipos de atividade do torneio que não envolvem dinheiro (histórico do terminal).</summary>
public enum TournamentActivityType
{
    TablesDrawn,    // mesas sorteadas (ou sorteadas de novo)
    PlayerMoved,    // jogador mudou de mesa/lugar (inclusive balanceamento e quebra de mesa)
    PlayersSwapped, // dois jogadores trocaram de lugar
    PlayerSeated,   // jogador adicionado sentou sozinho num lugar livre
    TableBroken,    // mesa quebrada e jogadores redistribuídos
    PlayerRemoved   // inscrição desfeita
}
