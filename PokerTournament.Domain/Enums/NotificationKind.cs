namespace PokerTournament.Domain.Enums;

/// <summary>
/// Tipos de notificação da conta (SPEC-20261108-1635). Gravados como string via <c>nameof</c> em
/// <c>Notification.Kind</c>, como os outros status do domínio. Cada tipo pertence a uma categoria de preferência
/// (<see cref="NotificationCategory"/>): torneio, pedidos/avisos ou social.
/// </summary>
public enum NotificationKind
{
    /// <summary>Torneio em que a conta tem inscrição vinculada passou a Em andamento (botão ou relógio).</summary>
    TournamentStarted,
    /// <summary>Torneio em que a conta jogou terminou (sem posição conhecida para ela).</summary>
    TournamentFinished,
    /// <summary>Resultado do torneio para a conta: posição e prêmio (finalização automática ou manual).</summary>
    ResultPublished,
    /// <summary>Convite para um torneio (tipo pronto; a SPEC-20261108-1632 dispara).</summary>
    TournamentInvite,
    /// <summary>Organizador: um jogador pediu vínculo "sou eu" (tipo pronto; a SPEC-20261108-1632 dispara).</summary>
    PlayerLinkRequested,
    /// <summary>Jogador: o organizador respondeu ao pedido de vínculo (tipo pronto; a SPEC-20261108-1632 dispara).</summary>
    PlayerLinkAnswered,
    /// <summary>Organizador sem CEP no home game (tipo pronto; a SPEC-20261108-1634 dispara).</summary>
    AddressIncomplete,
    /// <summary>Alguém pediu a sua amizade (SPEC-20261108-1637; link /amigos?aba=pedidos). Categoria Social.</summary>
    FriendRequestReceived,
    /// <summary>Quem você pediu aceitou (SPEC-20261108-1637; link para o perfil dele). Categoria Social. A recusa NÃO notifica.</summary>
    FriendRequestAccepted,
    /// <summary>
    /// Mensagem nova numa conversa (SPEC-20261108-1638; link /mensagens/{conversationId}). Categoria Social. Criada só
    /// quando a conversa não está aberta em nenhuma conexão de quem recebe; no máximo uma não lida por conversa (a existente
    /// é atualizada em vez de nascer outra).
    /// </summary>
    MessageReceived,
    /// <summary>Organizador: alguém pediu vaga num torneio público do home game que ele gerencia (SPEC-20261108-1639; link para o painel). Categoria Requests.</summary>
    JoinRequestReceived,
    /// <summary>Interessado: a vaga foi reservada (SPEC-20261108-1639; link para Meus torneios). Categoria Torneio.</summary>
    JoinRequestApproved,
    /// <summary>Interessado: o pedido foi recusado, ou a reserva removida pelo organizador (SPEC-20261108-1639). Categoria Torneio.</summary>
    JoinRequestDeclined,
    /// <summary>Interessado: as vagas acabaram e o pedido entrou na lista de espera (SPEC-20261108-1639). Categoria Torneio.</summary>
    JoinRequestWaitlisted,
    /// <summary>Interessado: saiu da espera e a vaga ficou reservada (SPEC-20261108-1639). Categoria Torneio.</summary>
    JoinRequestPromoted,
    /// <summary>Lembrete no dia do torneio, para reservas aprovadas e inscritos vinculados — uma vez por conta e torneio (SPEC-20261108-1639). Categoria Torneio.</summary>
    TournamentReminder,
}

/// <summary>Categoria de preferência do Meu perfil: cada tipo cai numa delas (NotifyTournament, NotifyRequests, NotifySocial).</summary>
public enum NotificationCategory
{
    Tournament,
    Requests,
    Social,
}
