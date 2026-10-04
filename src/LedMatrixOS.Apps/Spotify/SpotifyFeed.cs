using LedMatrixOS.Apps.Services;

namespace LedMatrixOS.Apps.Spotify;

/// <summary>
/// Bridges the existing <see cref="SpotifyDataService"/> (which writes a static <see cref="SpotifyDataStore"/> once a second) to
/// <see cref="NowPlaying"/> snapshots. Album art is decoded once per distinct artwork payload, here on the polling thread.
/// </summary>
internal sealed class SpotifyFeed
{
    private byte[]? _artBytes;
    private LedMatrixOS.Graphics.Sprite? _art;

    /// <summary>The last error thrown by the data service, cleared once data flows again.</summary>
    public volatile Exception? ServiceError;

    /// <summary>Runs the data service, restarting it after a failure (auth or network) until cancelled.</summary>
    public async Task RunServiceAsync(SpotifyDataService service, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await service.ExecuteAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                ServiceError = e;
                Console.WriteLine(e);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Returns null until the service has fetched once, so the app can show a loading state.</summary>
    public Task<NowPlaying?> ReadAsync(CancellationToken ct)
    {
        if (ServiceError is { } error && !SpotifyDataStore.Loaded) throw error;
        if (!SpotifyDataStore.Loaded) return Task.FromResult<NowPlaying?>(null);

        ServiceError = null;
        var d = SpotifyDataStore.Value;
        if (string.IsNullOrEmpty(d.SongName)) return Task.FromResult<NowPlaying?>(NowPlaying.Nothing);

        if (!ReferenceEquals(d.Artwork, _artBytes))
        {
            _artBytes = d.Artwork;
            _art = NowPlaying.DecodeArt(d.Artwork);
        }

        var palette = d.AlbumColors is { Count: > 0 } colors ? colors.ToArray()
            : d.AlbumColor is { } one ? [one] : [];

        return Task.FromResult<NowPlaying?>(new NowPlaying
        {
            Title = d.SongName,
            Artist = d.ArtistName,
            ProgressMs = d.Progress,
            DurationMs = d.TrackLength,
            IsPlaying = d.IsPlaying,
            IsSaved = d.IsSavedSong == true,
            NextTitle = d.NextTrackName,
            Art = _art,
            Palette = palette,
        });
    }
}
