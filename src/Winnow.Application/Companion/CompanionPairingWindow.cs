using System.Security.Cryptography;

namespace Winnow.Application.Companion;

/// <summary>
/// One pairing code at a time, valid for five minutes and for one phone. Five wrong
/// codes close the window, so guessing needs the user to reopen it each time.
/// </summary>
public sealed class CompanionPairingWindow(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public const int MaxFailures = 5;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly Lock _lock = new();
    private (string Code, DateTime ExpiresAt)? _open;
    private int _failures;

    public (string Code, DateTime ExpiresAt)? Current
    {
        get { lock (_lock) return _open is { } open && open.ExpiresAt > clock.GetUtcNow().UtcDateTime ? open : null; }
    }

    /// <summary>Opens a fresh window, replacing any earlier code.</summary>
    public (string Code, DateTime ExpiresAt) Open()
    {
        // 16 characters from a 32-letter alphabet: 80 bits, read off a QR code, not typed.
        var code = new string([.. RandomNumberGenerator.GetBytes(16).Select(b => Alphabet[b % Alphabet.Length])]);
        lock (_lock)
        {
            _open = (code, clock.GetUtcNow().UtcDateTime + Lifetime);
            _failures = 0;
            return _open.Value;
        }
    }

    public void Close() { lock (_lock) _open = null; }

    /// <summary>True once for the current code; a wrong code counts toward closing the window.</summary>
    public bool TryRedeem(string? code)
    {
        lock (_lock)
        {
            if (_open is not { } open || open.ExpiresAt <= clock.GetUtcNow().UtcDateTime) { _open = null; return false; }
            var supplied = System.Text.Encoding.ASCII.GetBytes((code ?? "").Trim().ToUpperInvariant());
            if (CryptographicOperations.FixedTimeEquals(supplied, System.Text.Encoding.ASCII.GetBytes(open.Code)))
            {
                _open = null;
                return true;
            }
            if (++_failures >= MaxFailures) _open = null;
            return false;
        }
    }
}
