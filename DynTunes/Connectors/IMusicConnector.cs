namespace DynTunes.Connectors;

public interface IMusicConnector
{
    public MediaPlayerState GetState();
    public void Reconnect();
    // public bool NeedsPolling { get; }
}