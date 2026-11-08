namespace PokerTournament.Application.DTOs.Requests;

// Perfil público e privacidade (SPEC-20261108-1636): pedidos de me/public-profile e me/privacy.

/// <summary>
/// <c>PUT me/public-profile</c>: o apelido da URL (obrigatório; 3–30, [a-z0-9-]), cidade, UF e bio (até 200). Texto
/// vazio = apaga; tudo é da própria conta.
/// </summary>
public class UpdatePublicProfileRequest
{
    public string? Handle { get; set; }
    public string? City { get; set; }
    /// <summary>UF com 2 letras (a mesma lista do endereço do home game).</summary>
    public string? State { get; set; }
    public string? Bio { get; set; }
}

/// <summary>
/// <c>PUT me/privacy</c>: os quatro controles. Campo ausente (null) = não mexe, então cada interruptor da tela salva
/// sozinho.
/// </summary>
public class UpdatePrivacyRequest
{
    /// <summary>Everyone | Friends | Nobody.</summary>
    public string? ProfileVisibility { get; set; }
    /// <summary>Everyone | PlayedWith | Nobody.</summary>
    public string? FriendRequestsFrom { get; set; }
    /// <summary>Friends | FriendsAndOrganizers.</summary>
    public string? MessagesFrom { get; set; }
    public bool? ShowInPlayedWith { get; set; }
    /// <summary>Mostrar o WhatsApp da conta aos jogadores dos torneios que ela organiza.</summary>
    public bool? ShowWhatsappToPlayers { get; set; }
}
