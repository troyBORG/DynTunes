using System.Diagnostics;
using FrooxEngine;

namespace DynTunes.Connectors;

public class DummyMusicConnector : IMusicConnector
{
    public DummyMusicConnector()
    {
        Stopwatch sw = Stopwatch.StartNew();
        Thread thread = new(() =>
        {
            while (!Engine.Current.ShutdownRequested)
            {
                this._state = new MediaPlayerState
                {
                    Title = "Title",
                    Artist = "Artist",
                    IsPlaying = true,
                    PositionSeconds = sw.ElapsedMilliseconds / 1000f,
                };
                
                Thread.Sleep(1000);
            }
        });
        thread.Start();
    }

    private MediaPlayerState _state = new();
    
    public MediaPlayerState GetState()
    {
        return this._state;
    }
    
    public void Reconnect()
    {
        // Dummy connector doesn't need to reconnect
    }
}