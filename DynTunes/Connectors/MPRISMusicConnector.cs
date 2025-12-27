using DynTunes.Integration;
using Tmds.DBus.Protocol;
using System.Security.Cryptography;
using System.Text;

#if !DEBUG
using Elements.Core;
using FrooxEngine;
#endif

namespace DynTunes.Connectors;

public class MPRISMusicConnector : IMusicConnector
{
    private Player? _player;
    private volatile MediaPlayerState _state = new();
    private Connection? _connection;
    private volatile bool _shouldReconnect = false;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private const int ReconnectRetryIntervalSeconds = 10;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _urlCache = new(); // URL -> cached file path
    private readonly HttpClient _httpClient = new();
    
    public MPRISMusicConnector()
    {
        // Set up cache directory in user's temp folder
        string tempDir = Path.GetTempPath();
        _cacheDirectory = Path.Combine(tempDir, "DynTunes", "AlbumArtCache");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch
        {
            // If we can't create cache directory, we'll just pass URLs through
        }
        
        Task.Factory.StartNew(ConnectAndRunAsync, TaskCreationOptions.LongRunning);
    }

    public void Reconnect()
    {
        _shouldReconnect = true;
        _player = null;
        _state.IsConnected = false;
    }

    private async Task ConnectAndRunAsync()
    {
        _connection = new Connection(Address.Session!);
        await _connection.ConnectAsync();

#if !DEBUG
        while (!Engine.Current.ShutdownRequested)
#else
        while (true)
#endif
        {
            try
            {
                // Check if we need to reconnect or if we're not connected and it's time to retry
                bool shouldAttemptConnection = _shouldReconnect || 
                    (_player == null && (DateTime.Now - _lastConnectionAttempt).TotalSeconds >= ReconnectRetryIntervalSeconds);
                
                if (shouldAttemptConnection)
                {
                    _shouldReconnect = false;
                    _lastConnectionAttempt = DateTime.Now;
                    await this.ConnectToMPRISAsync(_connection);
                }

                if (this._player != null)
                {
                    await this.UpdateStatusAsync();
                    _state.IsConnected = true;
                }
                else
                {
                    _state.IsConnected = false;
                }
            }
            catch (DBusException e) when(e.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown")
            {
                this._player = null;
                this._state.IsConnected = false;
                // Reset state but keep existing values for display
                await Task.Delay(5000);
                continue;
            }
            catch (Exception e)
            {
                this._player = null;
                this._state.IsConnected = false;
                #if !DEBUG
                UniLog.Warning($"Failed to update MPRIS status: {e}");
                #else
                Environment.FailFast("Failed to update MPRIS status", e);
                #endif
            }
            
            await Task.Delay(this._state.IsPlaying ? 500 : 2000);
        }
    }

    private async Task ConnectToMPRISAsync(Connection connection)
    {
        string[] services = await connection.ListServicesAsync();
        string[] mprisServices = services.Where(s => s.StartsWith("org.mpris.MediaPlayer2.")).ToArray();
        
        if (mprisServices.Length == 0)
        {
            _state.IsConnected = false;
            return;
        }
        
        // Prefer Spotify first, then non-browser players, then browsers
        string? spotifyService = mprisServices.FirstOrDefault(s => s.EndsWith(".spotify"));
        string? nonBrowserService = mprisServices
            .Where(s => !s.Contains("chromium", StringComparison.OrdinalIgnoreCase) &&
                       !s.Contains("brave", StringComparison.OrdinalIgnoreCase) &&
                       !s.Contains("firefox", StringComparison.OrdinalIgnoreCase) &&
                       !s.Contains("chrome", StringComparison.OrdinalIgnoreCase) &&
                       !s.Contains("plasma-browser", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        string? browserService = mprisServices
            .FirstOrDefault(s => s.Contains("chromium", StringComparison.OrdinalIgnoreCase) ||
                               s.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
                               s.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
                               s.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
                               s.Contains("plasma-browser", StringComparison.OrdinalIgnoreCase));
        
        string serviceDestination = spotifyService ?? nonBrowserService ?? browserService ?? mprisServices[0];

        MPRISService service = new(connection, serviceDestination);
        Player player = service.CreatePlayer("/org/mpris/MediaPlayer2");

        // Check if player is playing before connecting (similar to media-info.sh script behavior)
        // This ensures we only connect to actively playing media
        try
        {
            string playbackStatus = await player.GetPlaybackStatusAsync();
            if (playbackStatus == "Playing")
            {
                this._player = player;
                _state.IsConnected = true;
            }
            else
            {
                _state.IsConnected = false;
            }
        }
        catch
        {
            // If we can't get playback status, connect anyway and let UpdateStatusAsync handle it
            this._player = player;
            _state.IsConnected = true;
        }
    }

    private async Task UpdateStatusAsync()
    {
        if (this._player == null) return;
        
        long position = await this._player.GetPositionAsync();
        _state.PositionSeconds = (float)(position / (decimal)TimeSpan.MicrosecondsPerSecond);

        string playbackStatus = await this._player.GetPlaybackStatusAsync();
        _state.IsPlaying = playbackStatus == "Playing";

        Dictionary<string, VariantValue>? metadata = await this._player.GetMetadataAsync();
        foreach ((string key, VariantValue value) in metadata)
        {
            switch (key)
            {
                case "mpris:artUrl":
                    string? artUrl = value.GetString();
                    if (!string.IsNullOrEmpty(artUrl))
                    {
                        // Convert file:// URLs to proper file paths for better Resonite compatibility
                        if (artUrl.StartsWith("file://"))
                        {
                            try
                            {
                                Uri uri = new(artUrl);
                                _state.AlbumArtUrl = Uri.UnescapeDataString(uri.AbsolutePath);
                            }
                            catch
                            {
                                // If URI parsing fails, try simple string replacement
                                _state.AlbumArtUrl = artUrl.Replace("file://", "").Replace("%20", " ");
                            }
                        }
                        else if (artUrl.StartsWith("http://") || artUrl.StartsWith("https://"))
                        {
                            // Download HTTP/HTTPS images and cache them locally for Resonite compatibility
                            _state.AlbumArtUrl = await DownloadAndCacheImageAsync(artUrl);
                        }
                        else
                        {
                            // Keep other formats as-is
                            _state.AlbumArtUrl = artUrl;
                        }
                    }
                    break;
                case "mpris:length":
                    // Handle both Int64 and UInt64 types
                    ulong length;
                    try
                    {
                        length = value.GetUInt64();
                    }
                    catch
                    {
                        length = (ulong)value.GetInt64();
                    }
                    _state.LengthSeconds = (float)(length / (decimal)TimeSpan.MicrosecondsPerSecond);
                    break;
                case "xesam:album":
                    _state.Album = value.GetString();
                    break;
                case "xesam:artist":
                    string[]? artists = value.GetArray<string>();
                    if (artists != null && artists.Length > 0)
                    {
                        _state.Artist = string.Join(", ", artists);
                    }
                    break;
                case "xesam:albumArtist":
                    // Use albumArtist as fallback if artist wasn't set (common in browser players)
                    if (string.IsNullOrEmpty(_state.Artist))
                    {
                        string[]? albumArtists = value.GetArray<string>();
                        if (albumArtists != null && albumArtists.Length > 0)
                        {
                            _state.Artist = string.Join(", ", albumArtists);
                        }
                        else
                        {
                            string? albumArtistStr = value.GetString();
                            if (!string.IsNullOrEmpty(albumArtistStr))
                            {
                                _state.Artist = albumArtistStr;
                            }
                        }
                    }
                    break;
                case "xesam:title":
                    _state.Title = value.GetString();
                    break;
            }
        }
    }
    
    private async Task<string?> DownloadAndCacheImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_cacheDirectory))
        {
            return url; // Return original URL if we can't cache
        }

        try
        {
            // Check if we already have this URL cached
            if (_urlCache.TryGetValue(url, out string? cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            // Generate a cache filename from the URL hash
            byte[] urlBytes = Encoding.UTF8.GetBytes(url);
            byte[] hashBytes = SHA256.HashData(urlBytes);
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            
            // Try to determine file extension from URL or content
            string extension = ".jpg"; // Default
            try
            {
                Uri uri = new(url);
                string path = uri.AbsolutePath;
                if (path.Contains('.'))
                {
                    string urlExt = Path.GetExtension(path).ToLowerInvariant();
                    if (urlExt == ".jpg" || urlExt == ".jpeg" || urlExt == ".png" || urlExt == ".webp" || urlExt == ".gif")
                    {
                        extension = urlExt;
                    }
                }
            }
            catch
            {
                // Use default extension
            }

            string cacheFilePath = Path.Combine(_cacheDirectory, $"{hash}{extension}");

            // Download the image
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            byte[] imageData = await response.Content.ReadAsByteArrayAsync();
            
            // Write to cache file
            await File.WriteAllBytesAsync(cacheFilePath, imageData);
            
            // Store in cache dictionary
            _urlCache[url] = cacheFilePath;
            
            // Limit cache size - remove oldest files if cache gets too large (keep last 100)
            try
            {
                string[] cacheFiles = Directory.GetFiles(_cacheDirectory);
                if (cacheFiles.Length > 100)
                {
                    Array.Sort(cacheFiles, (a, b) => File.GetLastWriteTime(a).CompareTo(File.GetLastWriteTime(b)));
                    for (int i = 0; i < cacheFiles.Length - 100; i++)
                    {
                        try
                        {
                            File.Delete(cacheFiles[i]);
                            // Remove from cache dictionary if present
                            var keyToRemove = _urlCache.FirstOrDefault(kvp => kvp.Value == cacheFiles[i]).Key;
                            if (keyToRemove != null)
                            {
                                _urlCache.Remove(keyToRemove);
                            }
                        }
                        catch
                        {
                            // Ignore deletion errors
                        }
                    }
                }
            }
            catch
            {
                // Ignore cache cleanup errors
            }

            return cacheFilePath;
        }
        catch
        {
            // If download fails, return original URL as fallback
            #if !DEBUG
            UniLog.Warning($"Failed to download album art from {url}, using URL directly");
            #endif
            return url;
        }
    }
    
    public MediaPlayerState GetState()
    {
        return _state;
    }
}