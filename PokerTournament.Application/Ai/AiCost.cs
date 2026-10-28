using PokerTournament.Domain.Entities;

namespace PokerTournament.Application.Ai;

/// <summary>Tokens de uma chamada (do <c>Usage</c> da resposta).</summary>
public readonly record struct AiTokenUsage(long InputTokens, long OutputTokens, long CacheCreationInputTokens, long CacheReadInputTokens)
{
    /// <summary>Tamanho do prompt para a faixa de preço longa: entrada + escrita + leitura de cache.</summary>
    public long PromptTokens => InputTokens + CacheCreationInputTokens + CacheReadInputTokens;
}

/// <summary>Custo em US$ de uma chamada pela tabela de preços (US$ por milhão de tokens) — SPEC-20261027-1312.</summary>
public static class AiCost
{
    public static decimal Compute(AiModelPrice price, AiTokenUsage usage)
    {
        var useLong = price.LongContextThresholdTokens is { } threshold && usage.PromptTokens > threshold;
        var input = useLong ? price.LongInputPerMTok ?? price.InputPerMTok : price.InputPerMTok;
        var output = useLong ? price.LongOutputPerMTok ?? price.OutputPerMTok : price.OutputPerMTok;
        var cacheWrite = useLong ? price.LongCacheWritePerMTok ?? price.CacheWritePerMTok : price.CacheWritePerMTok;
        var cacheRead = useLong ? price.LongCacheReadPerMTok ?? price.CacheReadPerMTok : price.CacheReadPerMTok;
        var total = usage.InputTokens * input
            + usage.OutputTokens * output
            + usage.CacheCreationInputTokens * cacheWrite
            + usage.CacheReadInputTokens * cacheRead;
        return Math.Round(total / 1_000_000m, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Início do mês corrente (UTC) — o mesmo corte do faturamento da Anthropic.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
