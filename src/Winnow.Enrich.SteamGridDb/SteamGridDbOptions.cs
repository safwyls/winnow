namespace Winnow.Enrich.SteamGridDb;

public sealed class SteamGridDbOptions
{
    public Uri BaseAddress { get; set; } = new("https://www.steamgriddb.com/api/v2/");
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(30);
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxRetryAttempts { get; set; } = 2;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
