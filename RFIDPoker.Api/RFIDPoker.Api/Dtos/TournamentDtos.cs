namespace RFIDPoker.Api.Dtos;

public record BreakStateDto
{
    public bool IsActive { get; init; }
    public bool IsPaused { get; init; }
    public string? Label { get; init; }
    public int TotalSeconds { get; init; }
    public int RemainingSeconds { get; init; }
    public DateTimeOffset ServerNowUtc { get; init; }
}

public record StartBreakRequest(int DurationSeconds, string? Label);

public record AdjustBreakRequest(int DeltaSeconds);
