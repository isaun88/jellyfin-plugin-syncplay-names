using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SyncPlayNames;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<PartyTracker>();
        services.AddSingleton<AnnouncementDispatcher>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AnnouncementDispatcher>());
        var original = services.LastOrDefault(d => d.ServiceType == typeof(ISyncPlayManager));
        var implementation = original?.ImplementationType;
        var version = applicationHost.ApplicationVersion;
        // Only enable server versions covered by compatibility tests.
        if (version.Major != 12 || version.Minor is not (1 or 2))
        {
            Plugin.IntegrationStatus = $"Inactive on Jellyfin {version}: requires Jellyfin 12.1.x or 12.2.x";
            return;
        }
        // Do not interfere with another replacement plugin.
        if (original?.Lifetime != ServiceLifetime.Singleton
            || implementation?.FullName != "Emby.Server.Implementations.SyncPlay.SyncPlayManager")
        {
            Plugin.IntegrationStatus = $"Inactive on Jellyfin {version}: requires the standard singleton SyncPlay manager";
            return;
        }
        services.Remove(original!);
        services.AddSingleton<ISyncPlayManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<NamedSyncPlayManager>>();
            var parties = sp.GetRequiredService<PartyTracker>();
            var sessions = sp.GetRequiredService<ISessionManager>();
            var observedSessions = SessionObserverProxy.Wrap(sessions, parties, logger);
            var inner = (ISyncPlayManager)ActivatorUtilities.CreateInstance(sp, implementation, observedSessions);
            return new NamedSyncPlayManager(inner, parties, sp.GetRequiredService<AnnouncementDispatcher>(), logger);
        });
        Plugin.IntegrationStatus = $"Registered for Jellyfin {version}; uses native DisplayMessage notifications";
    }
}
