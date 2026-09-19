using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SyncPlayNames.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool AnnouncePlaybackActions { get; set; } = true;
    public bool AnnounceBuffering { get; set; } = true;
    public bool AnnounceRecovery { get; set; } = true;
    public int MessageDurationMs { get; set; } = 3500;
    public int BufferRepeatIntervalSeconds { get; set; } = 15;
    public List<NicknameOverride> Nicknames { get; set; } = [];
}

public sealed class NicknameOverride
{
    public string Username { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}
