using CashCafe.Data;

namespace CashCafe.App.Core.Services;

public enum PinResult
{
    Accepted,
    Wrong,
    LockedOut,
    NotSetUp,
}

/// <summary>
/// Guards the admin panel.
///
/// Five wrong PINs locks admin for five minutes. Note what is *not* locked: the till. A café
/// that stops selling because somebody mistyped a PIN would be a worse system than the
/// spreadsheet it replaced, so the lockout only ever reaches the admin panel.
/// </summary>
public sealed class AdminSession(SettingsRepository settings, AuditRepository audit, TimeProvider? clock = null)
{
    private const string PinKey = "admin_pin_hash";
    private const int MaxAttempts = 5;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private int _failures;
    private DateTimeOffset? _lockedUntil;
    private DateTimeOffset _lastActivity;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public bool IsOpen { get; private set; }

    public bool IsLockedOut => _lockedUntil is not null && _clock.GetUtcNow() < _lockedUntil;

    public TimeSpan LockoutRemaining =>
        IsLockedOut ? _lockedUntil!.Value - _clock.GetUtcNow() : TimeSpan.Zero;

    public bool IsConfigured => settings.TryGet(PinKey) is not null;

    public void SetPin(string pin, string actor)
    {
        AdminPin.Validate(pin);
        settings.Set(PinKey, AdminPin.Hash(pin), actor);
    }

    public PinResult Enter(string pin)
    {
        if (IsLockedOut) return PinResult.LockedOut;

        var stored = settings.TryGet(PinKey);
        if (stored is null) return PinResult.NotSetUp;

        if (!AdminPin.Verify(pin, stored))
        {
            _failures++;

            // Every failure is recorded. A student working through PINs at the counter
            // leaves a trail whether or not they ever get in.
            audit.WriteFailedPin(_failures);

            if (_failures >= MaxAttempts)
            {
                _lockedUntil = _clock.GetUtcNow().AddMinutes(5);
                _failures = 0;
                return PinResult.LockedOut;
            }

            return PinResult.Wrong;
        }

        _failures = 0;
        _lockedUntil = null;
        IsOpen = true;
        _lastActivity = _clock.GetUtcNow();
        return PinResult.Accepted;
    }

    public void Touch() => _lastActivity = _clock.GetUtcNow();

    public void Close() => IsOpen = false;

    /// <summary>
    /// True once the panel has been sitting untouched too long. Checked by a timer in the UI,
    /// so an admin panel cannot be left open on a counter at break time.
    /// </summary>
    public bool HasGoneIdle() => IsOpen && _clock.GetUtcNow() - _lastActivity > IdleTimeout;
}
