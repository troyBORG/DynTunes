using DynTunes.Integration;
using Tmds.DBus.Protocol;

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
    
    public MPRISMusicConnector()
    {
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
                        else
                        {
                            // Pass URLs through directly
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
    
    public MediaPlayerState GetState()
    {
        return _state;
    }
}