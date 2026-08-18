namespace PaketUtilityServices.Core.Models;

public record class DependencyStrategy
{
    public string Source { get; init; } = "https://api.nuget.org/v3/index.json";
    public string Storage { get; init; } = "none";
    public string StrategyLevel { get; init; } = "min";
}
