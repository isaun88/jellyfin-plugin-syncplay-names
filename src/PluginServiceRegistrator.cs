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
        // The version is diagnostic only. Check the service we wrap, not a version allowlist.
        // Do not interfere with another replacement plugin.
        if (original?.Lifetime != ServiceLifetime.Singleton
            || implementation is null
            || implementation.FullName != "Emby.Server.Implementations.SyncPlay.SyncPlayManager"
            || implementation.IsAbstract
            || !typeof(ISyncPlayManager).IsAssignableFrom(implementation))
        {
            Plugin.IntegrationStatus = $"Inactive on Jellyfin {version}: requires the standard singleton SyncPlay manager";
            return;
        }
        if (!implementation.GetConstructors().Any(constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ISessionManager))))
        {
            Plugin.IntegrationStatus = $"Inactive on Jellyfin {version}: SyncPlay manager has no injectable session manager";
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
