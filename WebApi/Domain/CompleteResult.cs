namespace WebApi.Domain;

public sealed class CompleteResult
{
    public bool AcceptedForBackground { get; init; }
    public string? FinalPath { get; init; }
    public string Status { get; init; } = "Completing";

    public static CompleteResult Background() => new()
    {
        AcceptedForBackground = true,
        Status = "Completing"
    };

    public static CompleteResult Done(string path) => new()
    {
        AcceptedForBackground = false,
        FinalPath = path,
        Status = "Completed"
    };
}
