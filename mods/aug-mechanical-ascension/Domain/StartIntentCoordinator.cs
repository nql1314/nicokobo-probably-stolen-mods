namespace AugMechanicalAscension.Domain;

// Menu-only intent. No native start is selected and no save is written here.
internal sealed record PendingStart(string Token, long MenuEpoch);
internal sealed record BoundStart(string Token, long MenuEpoch, string GameRunId, int SaveSlotId);

internal sealed class StartIntentCoordinator
{
    private long _menuEpoch;
    private bool _menuOpen;
    private bool _augSelected;
    private PendingStart? _pending;

    internal long OpenMenu()
    {
        _menuEpoch = checked(_menuEpoch + 1);
        _menuOpen = true;
        _augSelected = false;
        _pending = null;
        return _menuEpoch;
    }

    internal bool SelectAug(long menuEpoch)
    {
        if (!_menuOpen || menuEpoch != _menuEpoch) return false;
        _augSelected = true;
        _pending = null;
        return true;
    }

    internal void SelectOther(long menuEpoch)
    {
        if (!_menuOpen || menuEpoch != _menuEpoch) return;
        _augSelected = false;
        _pending = null;
    }

    internal void Cancel(long menuEpoch)
    {
        if (!_menuOpen || menuEpoch != _menuEpoch) return;
        Invalidate();
    }

    internal PendingStart? Confirm(long menuEpoch)
    {
        if (!_menuOpen || menuEpoch != _menuEpoch || !_augSelected || _pending != null)
            return null;

        _augSelected = false;
        _pending = new PendingStart(Guid.NewGuid().ToString("D"), menuEpoch);
        return _pending;
    }

    // Call only after the native slot and run ID exist, before the initial save is serialized.
    // A matching attempt consumes the token even if the native identity is invalid.
    internal BoundStart? Bind(PendingStart reservation, string? gameRunId, int saveSlotId)
    {
        if (_pending == null || reservation != _pending || reservation.MenuEpoch != _menuEpoch)
            return null;

        _pending = null;
        _menuOpen = false;
        if (saveSlotId < 0 || !Guid.TryParse(gameRunId, out var parsed) || parsed == Guid.Empty)
            return null;
        return new BoundStart(reservation.Token, reservation.MenuEpoch, gameRunId!, saveSlotId);
    }

    internal void Invalidate()
    {
        _menuEpoch = checked(_menuEpoch + 1);
        _menuOpen = false;
        _augSelected = false;
        _pending = null;
    }
}
