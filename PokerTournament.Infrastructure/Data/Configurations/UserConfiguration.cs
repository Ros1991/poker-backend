using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.xmin)
            .IsRowVersion();

        builder.Property(u => u.FullName)
            .HasColumnName("full_name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.Nickname)
            .HasColumnName("nickname")
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .IsRequired()
            .HasMaxLength(200);

        // Null = conta só-social (entra pelo Google — SPEC-20261023-1210)
        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500);

        builder.Property(u => u.Whatsapp)
            .HasColumnName("whatsapp")
            .HasMaxLength(20);

        builder.Property(u => u.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(500);

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.EmailConfirmedAt).HasColumnName("email_confirmed_at");
        builder.Property(u => u.RefreshToken).HasColumnName("refresh_token");
        builder.Property(u => u.RefreshTokenExpiresAt).HasColumnName("refresh_token_expires_at");

        // Plano da conta (SPEC-20261023-1205): strings via nameof, default Mesa/Manual para conta nova
        builder.Property(u => u.Plan)
            .HasColumnName("plan")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Mesa");

        builder.Property(u => u.PlanExpiresAt).HasColumnName("plan_expires_at");

        builder.Property(u => u.PlanSource)
            .HasColumnName("plan_source")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Manual");

        builder.Property(u => u.PlanNote)
            .HasColumnName("plan_note")
            .HasMaxLength(200);

        builder.Property(u => u.PlanUpdatedAt).HasColumnName("plan_updated_at");
        builder.Property(u => u.PlanUpdatedBy).HasColumnName("plan_updated_by");

        builder.HasIndex(u => u.Plan)
            .HasDatabaseName("ix_users_plan");

        // Boas-vindas (SPEC-20261023-1212): primeiro acesso concluído e plano pago pedido (só a intenção)
        builder.Property(u => u.OnboardedAt).HasColumnName("onboarded_at");

        builder.Property(u => u.RequestedPlan)
            .HasColumnName("requested_plan")
            .HasMaxLength(20);

        builder.Property(u => u.RequestedPlanAt).HasColumnName("requested_plan_at");

        // Notificações (SPEC-20261108-1635): preferências ligadas por padrão e o último e-mail resumo
        builder.Property(u => u.NotifyByEmail).HasColumnName("notify_by_email").HasDefaultValue(true);
        builder.Property(u => u.NotifyTournament).HasColumnName("notify_tournament").HasDefaultValue(true);
        builder.Property(u => u.NotifyRequests).HasColumnName("notify_requests").HasDefaultValue(true);
        builder.Property(u => u.NotifySocial).HasColumnName("notify_social").HasDefaultValue(true);
        builder.Property(u => u.LastDigestAt).HasColumnName("last_digest_at");

        // Perfil público e privacidade (SPEC-20261108-1636): handle único (minúsculas; a aplicação normaliza antes de
        // gravar), cidade/UF/bio e os quatro controles com os padrões conservadores (D12)
        builder.Property(u => u.Handle)
            .HasColumnName("handle")
            .IsRequired()
            .HasMaxLength(30);

        builder.HasIndex(u => u.Handle)
            .IsUnique()
            .HasDatabaseName("ix_users_handle");

        builder.Property(u => u.City)
            .HasColumnName("city")
            .HasMaxLength(100);

        builder.Property(u => u.State)
            .HasColumnName("state")
            .HasMaxLength(2);

        builder.Property(u => u.Bio)
            .HasColumnName("bio")
            .HasMaxLength(200);

        builder.Property(u => u.ProfileVisibility)
            .HasColumnName("profile_visibility")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Everyone");

        builder.Property(u => u.FriendRequestsFrom)
            .HasColumnName("friend_requests_from")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("PlayedWith");

        builder.Property(u => u.MessagesFrom)
            .HasColumnName("messages_from")
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("Friends");

        builder.Property(u => u.ShowInPlayedWith)
            .HasColumnName("show_in_played_with")
            .HasDefaultValue(true);

        // Opt-in do WhatsApp do organizador para os jogadores (revisão independente da onda 1): padrão desligado
        builder.Property(u => u.ShowWhatsappToPlayers)
            .HasColumnName("show_whatsapp_to_players")
            .HasDefaultValue(false);

        // Unique email
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email");
    }
}
