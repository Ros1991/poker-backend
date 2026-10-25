namespace PokerTournament.Application.DTOs.Requests;

// Meu perfil (SPEC-20261023-1211): pedidos de me/* — a conta logada mexe só nos próprios dados.

/// <summary><c>PUT me</c>: nome, apelido e WhatsApp da conta (o e-mail é só leitura nesta SPEC).</summary>
public class UpdateProfileRequest
{
    public string? FullName { get; set; }
    /// <summary>Vazio = sem apelido.</summary>
    public string? Nickname { get; set; }
    /// <summary>Com ou sem o 55, com ou sem máscara; gravado como 55 + DDD + número (só dígitos). Vazio = sem WhatsApp.</summary>
    public string? Whatsapp { get; set; }
}

/// <summary>
/// <c>POST me/onboarding</c> (SPEC-20261023-1212): conclui as boas-vindas. Campo ausente (null) = não mexe no que já
/// está gravado ("Fazer depois" manda só o plano); texto vazio = apaga.
/// </summary>
public class OnboardingRequest
{
    /// <summary>Até 100 caracteres. Null = mantém; vazio = sem apelido.</summary>
    public string? Nickname { get; set; }
    /// <summary>Com ou sem 55/máscara; gravado como 55 + DDD + número. Null = mantém; vazio = sem WhatsApp.</summary>
    public string? Whatsapp { get; set; }
    /// <summary>
    /// Foto do Google: true = fica (já é a foto da conta desde a entrada pelo Google); false = sai, se a foto atual for
    /// a do Google (foto enviada pelo site nunca é apagada por aqui); null = não mexe.
    /// </summary>
    public bool? UseProviderPhoto { get; set; }
    /// <summary>Obrigatório: Mesa | Clube | Liga. Clube/Liga só registram o pedido (o plano não muda).</summary>
    public string? Plan { get; set; }
}

/// <summary><c>POST me/password</c>: troca a senha (exige a atual); este aparelho continua, os outros saem.</summary>
public class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

/// <summary><c>POST me/logins/{provider}</c>: conectar o Google pelo perfil (ID token do botão + nonce emitido pela API).</summary>
public class ConnectExternalLoginRequest
{
    public string? IdToken { get; set; }
    public string? Nonce { get; set; }
}

/// <summary>
/// <c>DELETE me</c>: excluir a conta. <see cref="Confirmation"/> = "EXCLUIR" e, para provar que é a dona da conta,
/// a senha (conta com senha) ou um login com o Google feito agora (conta sem senha: ID token + nonce, emitido há
/// menos de 5 minutos).
/// </summary>
public class DeleteAccountRequest
{
    public string? Confirmation { get; set; }
    public string? Password { get; set; }
    public string? IdToken { get; set; }
    public string? Nonce { get; set; }
}
