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
    
    public MPRISMusicConnector()
    {
        Task.Factory.StartNew(ConnectAndRunAsync, TaskCreationOptions.LongRunning);
    }

    private async Task ConnectAndRunAsync()
    {
        using Connection connection = new(Address.Session!);
        await connection.ConnectAsync();

#if !DEBUG
        while (!Engine.Current.ShutdownRequested)
#else
        while (true)
#endif
        {
            try
            {
                if (this._player == null)
                    await this.ConnectToMPRISAsync(connection);

                await this.UpdateStatusAsync();
            }
            catch (DBusException e) when(e.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown")
            {
                this._state = new MediaPlayerState();
                await Task.Delay(5000);
                continue;
            }
            catch (Exception e)
            {
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
            return;
        
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

        this._player = player;
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
                            // Keep http/https URLs and other formats as-is
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
                    _state.Artist = string.Join(", ", value.GetArray<string>());
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