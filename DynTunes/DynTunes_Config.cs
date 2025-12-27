using ResoniteModLoader;

namespace DynTunes;

public partial class DynTunes // Config
{
    private const string KeySpaceDescription = "The space to put the dynamic variables under, e.g. \"User\" for \"User/Music_Title\"";
    private const string KeySpaceDefault = "User";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> KeySpaceInternal = new("DynVarKeySpace", KeySpaceDescription, () => KeySpaceDefault);
    internal static string KeySpace => Config?.GetValue(KeySpaceInternal) ?? "User";
    
    private const string KeyPrefixDescription = "The prefix to prepend to the dynamic variable name, e.g. \"Music_\" for \"User/Music_Title\"";
    private const string KeyPrefixDefault = "Music_";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> KeyPrefixInternal = new("DynVarKeyPrefix", KeyPrefixDescription, () => KeyPrefixDefault);
    internal static string KeyPrefix => Config?.GetValue(KeyPrefixInternal) ?? "Music_";
    
    private const string ArtistDescription = "The name to use for the Artist string";
    private const string ArtistDefault = "Artist";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> ArtistInternal = new("Artist", ArtistDescription, () => ArtistDefault);
    internal static string Artist => Config?.GetValue(ArtistInternal) ?? ArtistDefault;
    
    private const string TitleDescription = "The name to use for the Title string";
    private const string TitleDefault = "Title";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> TitleInternal = new("Title", TitleDescription, () => TitleDefault);
    internal static string Title => Config?.GetValue(TitleInternal) ?? TitleDefault;
    
    private const string PlayingDescription = "The name to use for the IsPlaying boolean";
    private const string PlayingDefault = "IsPlaying";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> PlayingInternal = new("Playing", PlayingDescription, () => PlayingDefault);
    internal static string Playing => Config?.GetValue(PlayingInternal) ?? PlayingDefault;
    
    private const string PositionDescription = "The name to use for the Position float";
    private const string PositionDefault = "Position";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> PositionInternal = new("Position", PositionDescription, () => PositionDefault);
    internal static string Position => Config?.GetValue(PositionInternal) ?? PositionDefault;
    
    private const string LengthDescription = "The name to use for the Length float";
    private const string LengthDefault = "Length";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> LengthInternal = new("Length", LengthDescription, () => LengthDefault);
    internal static string Length => Config?.GetValue(LengthInternal) ?? LengthDefault;
    
    private const string AlbumDescription = "The name to use for the Album string";
    private const string AlbumDefault = "Album";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> AlbumInternal = new("Album", AlbumDescription, () => AlbumDefault);
    internal static string Album => Config?.GetValue(AlbumInternal) ?? AlbumDefault;
    
    private const string AlbumArtUrlDescription = "The name to use for the Album art url string";
    private const string AlbumArtUrlDefault = "AlbumArtUrl";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> AlbumArtUrlInternal = new("AlbumArtUrl", AlbumArtUrlDescription, () => AlbumArtUrlDefault);
    internal static string AlbumArtUrl => Config?.GetValue(AlbumArtUrlInternal) ?? AlbumArtUrlDefault;
    
    private const string IsConnectedDescription = "The name to use for the IsConnected boolean";
    private const string IsConnectedDefault = "IsConnected";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> IsConnectedInternal = new("IsConnected", IsConnectedDescription, () => IsConnectedDefault);
    internal static string IsConnected => Config?.GetValue(IsConnectedInternal) ?? IsConnectedDefault;
    
    private const string SlotNameDescription = "The name of the Slot to create in the UserRoot to store all DynTunes dynamic variables";
    private const string SlotNameDefault = "🎵 DynTunes";
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<string> SlotNameInternal = new("DynTunesSlotName", SlotNameDescription, () => SlotNameDefault);
    internal static string SlotName => Config?.GetValue(SlotNameInternal) ?? SlotNameDefault;
}