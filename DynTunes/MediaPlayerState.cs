namespace DynTunes;

public class MediaPlayerState
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtUrl { get; set; }
    public bool IsPlaying { get; set; }
    public float PositionSeconds { get; set; }
    public float LengthSeconds { get; set; }
    public bool IsConnected { get; set; }
}