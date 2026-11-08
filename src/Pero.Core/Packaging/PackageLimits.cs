namespace Pero.Core.Packaging;

/// <summary>Ограничения распаковки по SEC-01.</summary>
public sealed record PackageLimits
{
    public long MaxTotalUncompressedBytes { get; init; } = 1L * 1024 * 1024 * 1024; // 1 ГБ
    public int MaxEntries { get; init; } = 10_000;
    /// <summary>Подозрительный коэффициент сжатия для одной части (защита от zip-бомб).</summary>
    public double MaxCompressionRatio { get; init; } = 200;
    /// <summary>Коэффициент проверяется только для частей крупнее этого размера.</summary>
    public long RatioCheckThresholdBytes { get; init; } = 16L * 1024 * 1024;

    public static PackageLimits Default { get; } = new();
}
