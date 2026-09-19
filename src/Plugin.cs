using Jellyfin.Plugin.SyncPlayNames.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SyncPlayNames;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = new("f99bc1b7-7959-4914-9dab-adcc481e8946");
    public static Plugin? Instance { get; private set; }
    public static string IntegrationStatus { get; internal set; } = "Waiting for service registration";

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer)
    {
        Instance = this;
    }

    public override string Name => "SyncPlay Names";
    public override Guid Id => PluginId;
    public override string Description => "Shows who paused, resumed, seeked, or reported buffering in a SyncPlay party.";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = "SyncPlayNames",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        }
    ];
}
