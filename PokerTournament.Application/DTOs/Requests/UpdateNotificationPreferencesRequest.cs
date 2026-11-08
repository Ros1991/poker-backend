namespace PokerTournament.Application.DTOs.Requests;

/// <summary><c>PUT me/notification-preferences</c> (SPEC-20261108-1635): campo ausente (null) = não mexe.</summary>
public class UpdateNotificationPreferencesRequest
{
    public bool? NotifyByEmail { get; set; }
    public bool? NotifyTournament { get; set; }
    public bool? NotifyRequests { get; set; }
    public bool? NotifySocial { get; set; }
}
