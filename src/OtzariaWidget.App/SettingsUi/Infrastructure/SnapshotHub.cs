using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>
/// Shared, async access to <see cref="IOtzariaDataService.GetSnapshot"/> so several pages show the same data
/// without blocking the UI thread (reading seforim.db / Hive can be slow). Create and call on the UI thread.
/// </summary>
public sealed class SnapshotHub
{
    private readonly IOtzariaDataService _data;
    private int _generation;

    public SnapshotHub(IOtzariaDataService data) => _data = data;

    public OtzariaSnapshot? Current { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }

    /// <summary>Raised on the UI context when a refresh completes (successfully or not) or starts.</summary>
    public event EventHandler? Updated;

    public async Task RefreshAsync(bool invalidate = true)
    {
        int gen = ++_generation;
        IsLoading = true;
        Updated?.Invoke(this, EventArgs.Empty);
        OtzariaSnapshot? snap = null;
        string? error = null;
        try
        {
            snap = await Task.Run(() =>
            {
                if (invalidate) _data.Invalidate();
                return _data.GetSnapshot();
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SettingsLog.Error("GetSnapshot failed", ex);
            error = ErrorText.From(ex);
        }

        if (gen != _generation) return; // a newer refresh superseded this one
        Current = snap ?? Current;
        Error = error;
        IsLoading = false;
        Updated?.Invoke(this, EventArgs.Empty);
    }
}
