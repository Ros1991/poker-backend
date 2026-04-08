namespace PokerTournament.Application.DTOs.Responses;

public class HomeGameMemberResponse
{
    public Guid Id { get; set; }
    public Guid HomeGameId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? UserPhotoUrl { get; set; }
    public Guid? PersonId { get; set; }
    public string? PersonName { get; set; }
    public string? PersonNickname { get; set; }
    public string? PersonPhotoUrl { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; }
}
