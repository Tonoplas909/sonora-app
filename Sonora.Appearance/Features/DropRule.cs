namespace Sonora.Appearance.Features;

/// <summary>What a library entry is, as far as receiving dragged tracks goes.</summary>
public enum DropTarget
{
    /// <summary>Not a playlist: an album, an artist, a collection. Nothing can be dropped on it.</summary>
    Other,
    /// <summary>A playlist kept by Sonora: it stores file paths, so it takes local files only.</summary>
    LocalPlaylist,
    /// <summary>A Spotify playlist of the signed-in user: it takes Spotify tracks only.</summary>
    OwnSpotifyPlaylist,
    /// <summary>A Spotify playlist someone else owns: it takes nothing.</summary>
    ForeignSpotifyPlaylist,
}

public enum DropRefusal
{
    None,
    NotAPlaylist,
    SpotifyIntoLocal,
    LocalIntoSpotify,
    NotYours,
}

/// <summary>What dropping the tracks on the target would do: how many of them it takes, or why it takes none.</summary>
public sealed record DropVerdict(int Accepted, int Total, DropRefusal Refusal)
{
    public bool IsAccepted => Accepted > 0;
    public bool IsPartial => Accepted > 0 && Accepted < Total;
}

/// <summary>
/// The one rule behind dragging tracks onto a playlist. The drag-over preview and the drop both ask it, so what the
/// preview promises is exactly what the drop does (a track the playlist cannot hold used to be dropped without a word).
/// </summary>
public static class DropRule
{
    public static DropVerdict Judge(DropTarget target, int localTracks, int spotifyTracks)
    {
        int total = localTracks + spotifyTracks;
        return target switch
        {
            DropTarget.LocalPlaylist => localTracks > 0
                ? new DropVerdict(localTracks, total, DropRefusal.None)
                : new DropVerdict(0, total, DropRefusal.SpotifyIntoLocal),
            DropTarget.OwnSpotifyPlaylist => spotifyTracks > 0
                ? new DropVerdict(spotifyTracks, total, DropRefusal.None)
                : new DropVerdict(0, total, DropRefusal.LocalIntoSpotify),
            DropTarget.ForeignSpotifyPlaylist => new DropVerdict(0, total, DropRefusal.NotYours),
            _ => new DropVerdict(0, total, DropRefusal.NotAPlaylist),
        };
    }
}
