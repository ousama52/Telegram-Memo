namespace Telegram_Memo_App.Services;

public enum LocationState
{
    Getting,
    Ready,
    None,
}

public sealed record LocationFix(double Latitude, double Longitude, DateTime TimestampUtc);

public class LocationService
{
    static readonly TimeSpan MaxFixAge = TimeSpan.FromSeconds(60);
    static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    CancellationTokenSource? warmCts;
    LocationFix? lastFix;
    bool permissionGranted;
    int consecutiveFailures;

    public LocationState State { get; private set; } = LocationState.Getting;

    public event Action? StateChanged;

    // Call from the UI thread (permission dialogs need it).
    public async Task StartAsync()
    {
        Stop();
        warmCts = new CancellationTokenSource();
        var ct = warmCts.Token;
        consecutiveFailures = 0;

        permissionGranted = await EnsurePermissionAsync();
        if (!permissionGranted)
        {
            SetState(LocationState.None);
            return;
        }

        UpdateStateFromFix();
        _ = WarmLoopAsync(ct);
    }

    public void Stop()
    {
        warmCts?.Cancel();
        warmCts?.Dispose();
        warmCts = null;
    }

    // Freshest fix no older than about 60 s, else one quick 3 s request, else null.
    public async Task<LocationFix?> GetFixForShutterAsync()
    {
        if (!permissionGranted)
            return null;

        var fix = lastFix;
        if (fix is not null && DateTime.UtcNow - fix.TimestampUtc <= MaxFixAge)
            return fix;

        return await RequestFixAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
    }

    async Task WarmLoopAsync(CancellationToken ct)
    {
        try
        {
            // A recent last-known fix makes the chip ready immediately.
            var known = await Geolocation.Default.GetLastKnownLocationAsync();
            if (known is not null)
                Accept(known);
        }
        catch
        {
            // Ignore and wait for a live fix.
        }

        while (!ct.IsCancellationRequested)
        {
            await RequestFixAsync(TimeSpan.FromSeconds(8), ct);
            try
            {
                await Task.Delay(RefreshInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    async Task<LocationFix?> RequestFixAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, timeout);
            var location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLocationAsync(request, ct));
            if (location is not null)
            {
                consecutiveFailures = 0;
                return Accept(location);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (FeatureNotEnabledException)
        {
            // Location is switched off on the phone.
            consecutiveFailures = int.MaxValue / 2;
        }
        catch (Exception)
        {
            // Timeout or no provider: not an error, just no fix.
        }

        consecutiveFailures++;
        UpdateStateFromFix();
        return null;
    }

    LocationFix? Accept(Location location)
    {
        var timestamp = location.Timestamp.UtcDateTime;
        // Some providers report odd timestamps; never treat a fix as newer than now.
        if (timestamp > DateTime.UtcNow)
            timestamp = DateTime.UtcNow;
        var fix = new LocationFix(location.Latitude, location.Longitude, timestamp);
        if (lastFix is null || fix.TimestampUtc >= lastFix.TimestampUtc)
            lastFix = fix;
        UpdateStateFromFix();
        return DateTime.UtcNow - fix.TimestampUtc <= MaxFixAge ? fix : null;
    }

    void UpdateStateFromFix()
    {
        if (!permissionGranted)
            SetState(LocationState.None);
        else if (lastFix is not null && DateTime.UtcNow - lastFix.TimestampUtc <= MaxFixAge)
            SetState(LocationState.Ready);
        else if (consecutiveFailures >= 2)
            SetState(LocationState.None);
        else
            SetState(LocationState.Getting);
    }

    void SetState(LocationState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke();
    }

    static async Task<bool> EnsurePermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return status == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
    }
}
