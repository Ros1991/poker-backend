using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PokerTournament.Domain.Entities;

namespace PokerTournament.Infrastructure.Data.Configurations;

// IA na administração (SPEC-20261027-1312): configurações (linha única), preços por modelo, recursos e registro de uso.

public class AiSettingsConfiguration : IEntityTypeConfiguration<AiSettings>
{
    public void Configure(EntityTypeBuilder<AiSettings> builder)
    {
        builder.ToTable("ai_settings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.xmin).IsRowVersion();

        builder.Property(s => s.Enabled).HasColumnName("enabled");
        builder.Property(s => s.DefaultModel).HasColumnName("default_model").IsRequired().HasMaxLength(100);
        builder.Property(s => s.MonthlyLimitUsd).HasColumnName("monthly_limit_usd").HasColumnType("numeric(10,2)");
        builder.Property(s => s.ApiKeyEncrypted).HasColumnName("api_key_encrypted").HasMaxLength(1000);
        builder.Property(s => s.ApiKeyLast4).HasColumnName("api_key_last4").HasMaxLength(4);
        builder.Property(s => s.ApiKeyUpdatedAt).HasColumnName("api_key_updated_at");
        builder.Property(s => s.ApiKeyUpdatedBy).HasColumnName("api_key_updated_by");
        builder.Property(s => s.LastTestAt).HasColumnName("last_test_at");
        builder.Property(s => s.LastTestOk).HasColumnName("last_test_ok");
        builder.Property(s => s.LastTestMessage).HasColumnName("last_test_message").HasMaxLength(300);
        builder.Property(s => s.LastTestModel).HasColumnName("last_test_model").HasMaxLength(100);
    }
}

public class AiModelPriceConfiguration : IEntityTypeConfiguration<AiModelPrice>
{
    public void Configure(EntityTypeBuilder<AiModelPrice> builder)
    {
        builder.ToTable("ai_model_prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.xmin).IsRowVersion();

        builder.Property(p => p.Model).HasColumnName("model").IsRequired().HasMaxLength(100);
        builder.Property(p => p.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(100);
        builder.Property(p => p.InputPerMTok).HasColumnName("input_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.OutputPerMTok).HasColumnName("output_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.CacheWritePerMTok).HasColumnName("cache_write_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.CacheReadPerMTok).HasColumnName("cache_read_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.LongContextThresholdTokens).HasColumnName("long_context_threshold_tokens");
        builder.Property(p => p.LongInputPerMTok).HasColumnName("long_input_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.LongOutputPerMTok).HasColumnName("long_output_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.LongCacheWritePerMTok).HasColumnName("long_cache_write_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.LongCacheReadPerMTok).HasColumnName("long_cache_read_per_mtok").HasColumnType("numeric(12,4)");
        builder.Property(p => p.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(p => p.Model).IsUnique().HasDatabaseName("ix_ai_model_prices_model");
    }
}

public class AiFeatureConfiguration : IEntityTypeConfiguration<AiFeature>
{
    public void Configure(EntityTypeBuilder<AiFeature> builder)
    {
        builder.ToTable("ai_features");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.xmin).IsRowVersion();

        builder.Property(f => f.Key).HasColumnName("key").IsRequired().HasMaxLength(60);
        builder.Property(f => f.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        builder.Property(f => f.Description).HasColumnName("description").IsRequired().HasMaxLength(300);
        builder.Property(f => f.IsPaid).HasColumnName("is_paid");
        builder.Property(f => f.RunsOnDevice).HasColumnName("runs_on_device");
        builder.Property(f => f.Enabled).HasColumnName("enabled");
        builder.Property(f => f.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(f => f.Key).IsUnique().HasDatabaseName("ix_ai_features_key");
    }
}

public class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("ai_usages");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.xmin).IsRowVersion();

        builder.Property(u => u.FeatureKey).HasColumnName("feature_key").IsRequired().HasMaxLength(60);
        builder.Property(u => u.Model).HasColumnName("model").HasMaxLength(100);
        builder.Property(u => u.OnDevice).HasColumnName("on_device");
        builder.Property(u => u.UserId).HasColumnName("user_id");
        builder.Property(u => u.ActorName).HasColumnName("actor_name").HasMaxLength(200);
        builder.Property(u => u.HomeGameId).HasColumnName("home_game_id");
        builder.Property(u => u.InputTokens).HasColumnName("input_tokens");
        builder.Property(u => u.OutputTokens).HasColumnName("output_tokens");
        builder.Property(u => u.CacheCreationInputTokens).HasColumnName("cache_creation_input_tokens");
        builder.Property(u => u.CacheReadInputTokens).HasColumnName("cache_read_input_tokens");
        builder.Property(u => u.CostUsd).HasColumnName("cost_usd").HasColumnType("numeric(14,6)");
        builder.Property(u => u.Outcome).HasColumnName("outcome").IsRequired().HasMaxLength(20);
        builder.Property(u => u.ErrorCode).HasColumnName("error_code").HasMaxLength(40);
        builder.Property(u => u.ErrorMessage).HasColumnName("error_message").HasMaxLength(300);
        builder.Property(u => u.DurationMs).HasColumnName("duration_ms");

        // Registro (como audit_logs): sem FK para usuário/home game — apagar um deles não mexe no histórico de gasto
        builder.HasIndex(u => u.CreatedAt).HasDatabaseName("ix_ai_usages_created_at");
        builder.HasIndex(u => new { u.FeatureKey, u.CreatedAt }).HasDatabaseName("ix_ai_usages_feature_created_at");
    }
}
