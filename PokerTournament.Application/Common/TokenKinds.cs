namespace PokerTournament.Application.Common;

/// <summary>
/// Claims dos tokens de escopo restrito. Token de usuário NÃO tem "kind" (inclusive
/// os emitidos antes desta mudança). Token de dealer tem kind=dealer e tournament_id=torneio,
/// e só passa em endpoints marcados com [AllowTokenKind("dealer")].
/// Não usar "tid": o JwtBearer o renomeia para o tenantid do Azure AD ao ler o token.
/// </summary>
public static class TokenKinds
{
    public const string ClaimType = "kind";
    public const string TournamentClaim = "tournament_id";
    public const string SessionClaim = "sid"; // sessão do aparelho (token de usuário)

    public const string Dealer = "dealer";
}
