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
        // Do not guess at changed internals or interfere with another replacement plugin.
        if (applicationHost.ApplicationVersion.Major != 12 || applicationHost.ApplicationVersion.Minor != 1
            || original?.Lifetime != ServiceLifetime.Singleton
            || implementation?.FullName != "Emby.Server.Implementations.SyncPlay.SyncPlayManager")
        {
            Plugin.IntegrationStatus = "Inactive: requires Jellyfin 12.1 with the standard SyncPlay manager";
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
        Plugin.IntegrationStatus = "Registered for Jellyfin 12.1; uses native DisplayMessage notifications";
    }
}
