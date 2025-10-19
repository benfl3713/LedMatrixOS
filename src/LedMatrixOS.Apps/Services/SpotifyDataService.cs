using LedMatrixOS.Core;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SpotifyAPI.Web;

namespace LedMatrixOS.Apps.Services;

public class SpotifyDataService
{
    private SpotifyClient _spotify = null!;
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public SpotifyDataService(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var spotifyClientConfig = await SpotifyAuth.GetClientConfig(_clientId, _clientSecret);
        _spotify = new SpotifyClient(spotifyClientConfig);
        int counter = 4;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (SpotifyDataStore.Value.IsPlaying)
                SpotifyDataStore.Value.Progress += 1000;
            
            if (counter++ == 4 || SpotifyDataStore.Value.Progress >= SpotifyDataStore.Value.TrackLength)
            {
                counter = 0;
                try
                {
                    await Update(stoppingToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task Update(CancellationToken cancellationToken)
    {
        var currentlyPlaying = await _spotify.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest(), cancellationToken);
        var song = currentlyPlaying.Item as FullTrack;

        var queue = await _spotify.Player.GetQueue(cancellationToken);
        var nextTrack = queue.Queue.Count > 0 ? queue.Queue.First() as FullTrack : null;

        var newSong = song.Name != SpotifyDataStore.Value.SongName;

        var artwork = SpotifyDataStore.Value.Artwork;
        var nextTrackArtwork = SpotifyDataStore.Value.NextTrackArtwork;
        if (newSong)
        {
            var albumArtUrl = song.Album.Images.LastOrDefault();
            if (!string.IsNullOrEmpty(albumArtUrl.Url))
            {
                artwork = await _httpClient.GetByteArrayAsync(albumArtUrl.Url, cancellationToken);
            }
            
            var nextTrackAlbumArtUrl = nextTrack?.Album.Images.LastOrDefault();
            if (nextTrackAlbumArtUrl != null && !string.IsNullOrEmpty(nextTrackAlbumArtUrl.Url))
            {
                nextTrackArtwork = await _httpClient.GetByteArrayAsync(nextTrackAlbumArtUrl.Url, cancellationToken);
            }
        }

        var isSaved = !SpotifyDataStore.Value.IsSavedSong.HasValue || newSong ? (await _spotify.Library.CheckTracks(new LibraryCheckTracksRequest(new List<string> { song.Id })))
            .FirstOrDefault() : SpotifyDataStore.Value.IsSavedSong;
        
        var albumColor = !SpotifyDataStore.Value.AlbumColor.HasValue || newSong ? ExtractAlbumColor(artwork) : SpotifyDataStore.Value.AlbumColor;
        var albumColors = !SpotifyDataStore.Value.AlbumColors?.Any() == true || newSong ? ExtractAlbumColors(artwork) : SpotifyDataStore.Value.AlbumColors;
        
        SpotifyDataStore.Value = new SpotifyData
        {
            SongName = song.Name,
            Artwork = artwork,
            ArtistName = song.Artists.FirstOrDefault()?.Name ?? "Unknown Artist",
            TrackLength = song.DurationMs,
            Progress = currentlyPlaying.ProgressMs ?? 0,
            IsSavedSong = isSaved,
            IsPlaying = currentlyPlaying.IsPlaying,
            AlbumColor = albumColor,
            AlbumColors = albumColors,
            NextTrackName = nextTrack?.Name,
            NextTrackArtwork = nextTrackArtwork
        };
    }
    
    private Pixel ExtractAlbumColor(byte[] artwork)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(artwork);
        image.Mutate(i => i.Resize(10, 10)); // Resize to simplify color extraction

        List<Rgb24> pixels = new List<Rgb24>();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);

                // pixelRow.Length has the same value as accessor.Width,
                // but using pixelRow.Length allows the JIT to optimize away bounds checks:
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    // Get a reference to the pixel at position x
                    ref Rgb24 pixel = ref pixelRow[x];
                    pixels.Add(pixel);
                }
            }
        });

        var albumColors = pixels
            .GroupBy(p => new { p.R, p.G, p.B })
            .OrderByDescending(g => g.Count())
            .Take(5) // Get the top 5 dominant colors
            .Select(g => new Pixel(g.Key.R, g.Key.G, g.Key.B))
            .ToList();

        if (albumColors.Count == 0)
        {
            albumColors.Add(new Pixel(0, 0, 0)); // Fallback to black if no colors are found
        }
        
        return albumColors[0];
    }

    private List<Pixel> ExtractAlbumColors(byte[] artwork, int k = 6)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(artwork);
        image.Mutate(i => i.Resize(20, 20)); // Slightly larger for more color diversity

        List<Rgb24> pixels = new List<Rgb24>();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> pixelRow = accessor.GetRowSpan(y);
                for (int x = 0; x < pixelRow.Length; x++)
                {
                    ref Rgb24 pixel = ref pixelRow[x];
                    // Ignore near-black/white pixels for better palette
                    if (!(pixel.R > 240 && pixel.G > 240 && pixel.B > 240) && !(pixel.R < 15 && pixel.G < 15 && pixel.B < 15))
                        pixels.Add(pixel);
                }
            }
        });
        if (pixels.Count == 0)
            return new List<Pixel> { new Pixel(0, 0, 0) };

        // K-means clustering
        var rnd = new Random();
        var centroids = pixels.OrderBy(_ => rnd.Next()).Take(k).Select(p => new[] { (double)p.R, (double)p.G, (double)p.B }).ToList();
        for (int iter = 0; iter < 10; iter++)
        {
            var clusters = centroids.Select(_ => new List<Rgb24>()).ToList();
            foreach (var px in pixels)
            {
                int best = 0;
                double bestDist = double.MaxValue;
                for (int c = 0; c < centroids.Count; c++)
                {
                    var d = Math.Pow(px.R - centroids[c][0], 2) + Math.Pow(px.G - centroids[c][1], 2) + Math.Pow(px.B - centroids[c][2], 2);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                clusters[best].Add(px);
            }
            for (int c = 0; c < centroids.Count; c++)
            {
                if (clusters[c].Count > 0)
                {
                    centroids[c][0] = clusters[c].Average(p => p.R);
                    centroids[c][1] = clusters[c].Average(p => p.G);
                    centroids[c][2] = clusters[c].Average(p => p.B);
                }
            }
        }
        // Sort by cluster size (most prominent colors first)
        var finalClusters = centroids
            .Select((centroid, i) => new { Color = new Pixel((byte)centroid[0], (byte)centroid[1], (byte)centroid[2]), Size = pixels.Count(px =>
                Math.Pow(px.R - centroid[0], 2) + Math.Pow(px.G - centroid[1], 2) + Math.Pow(px.B - centroid[2], 2) < 1000) })
            .OrderByDescending(c => c.Size)
            .Select(c => c.Color)
            .ToList();
        return finalClusters;
    }
}

public static class SpotifyDataStore
{
    public static SpotifyData Value { get; set; } = new SpotifyData();
}

public class SpotifyData
{
    public string SongName { get; set; } = string.Empty;
    public byte[]? Artwork { get; set; }
    public string ArtistName { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int TrackLength { get; set; }
    public bool? IsSavedSong { get; set; }
    public bool IsPlaying { get; set; }
    public Pixel? AlbumColor { get; set; }
    public List<Pixel>? AlbumColors { get; set; }
    public string? NextTrackName { get; set; }
    public byte[]? NextTrackArtwork { get; set; }
}
