using Jellyfin.Plugin.SyncPlayNames.Configuration;

namespace Jellyfin.Plugin.SyncPlayNames;

internal readonly record struct Recipient(string SessionId, Guid MembershipId);
internal sealed record Announcement(Guid GroupId, Recipient[] Recipients, string Text, DateTimeOffset CreatedAt);

// All membership comes from the server's actual GroupJoined/GroupLeft messages.
// Account names are labels, never routing keys.
internal sealed class PartyTracker
{
    private sealed class Member(Guid groupId)
    {
        internal Guid GroupId = groupId;
        internal Guid MembershipId = Guid.NewGuid();
        internal bool Buffering;
        internal bool AnnouncedBuffering;
        internal DateTimeOffset LastBufferNotice = DateTimeOffset.MinValue;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Member> _members = new(StringComparer.Ordinal);

    public void Join(string sessionId, Guid groupId)
    {
        lock (_gate)
        {
            // A restore of the same group is not a new membership.
            if (_members.TryGetValue(sessionId, out var current) && current.GroupId == groupId) return;
            _members[sessionId] = new Member(groupId);
        }
    }

    public void Leave(string sessionId, Guid groupId)
    {
        lock (_gate)
        {
            if (_members.TryGetValue(sessionId, out var member) && member.GroupId == groupId)
                _members.Remove(sessionId);
        }
    }

    public bool StillMember(Guid groupId, Recipient recipient)
    {
        lock (_gate)
            return _members.TryGetValue(recipient.SessionId, out var member)
                && member.GroupId == groupId && member.MembershipId == recipient.MembershipId;
    }

    public void ResetBuffering(Guid groupId)
    {
        lock (_gate)
            foreach (var member in _members.Values.Where(m => m.GroupId == groupId))
            {
                member.Buffering = false;
                member.AnnouncedBuffering = false;
            }
    }

    public Announcement? Observe(Guid groupId, string sessionId, string username, string action,
        long positionTicks, PluginConfiguration config, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(sessionId, out var member) || member.GroupId != groupId) return null;
            string? verb;
            if (action == "Buffer")
            {
                member.Buffering = true;
                if (!config.Enabled || !config.AnnounceBuffering ||
                    now - member.LastBufferNotice < TimeSpan.FromSeconds(Math.Clamp(config.BufferRepeatIntervalSeconds, 3, 300)))
                    return null;
                member.LastBufferNotice = now;
                member.AnnouncedBuffering = true;
                verb = "is buffering";
            }
            else if (action == "Ready")
            {
                var reportRecovery = member.Buffering && member.AnnouncedBuffering;
                member.Buffering = false;
                member.AnnouncedBuffering = false;
                if (!reportRecovery || !config.Enabled || !config.AnnounceRecovery) return null;
                verb = "is ready again";
            }
            else
            {
                if (!config.Enabled || !config.AnnouncePlaybackActions) return null;
                verb = action switch
                {
                    "Pause" => "paused playback",
                    "Unpause" => "resumed playback",
                    "Seek" => "seeked to " + FormatPosition(positionTicks),
                    "Stop" => "stopped playback",
                    "NextItem" => "advanced to the next item",
                    "PreviousItem" => "went to the previous item",
                    "SetPlaylistItem" => "changed the playing item",
                    "Play" => "started playback",
                    _ => null
                };
            }
            if (verb is null) return null;
            var recipients = _members.Where(p => p.Value.GroupId == groupId)
                .Select(p => new Recipient(p.Key, p.Value.MembershipId)).ToArray();
            return new Announcement(groupId, recipients, ResolveName(username, config) + " " + verb, now);
        }
    }

    internal static string ResolveName(string username, PluginConfiguration config)
    {
        var configured = config.Nicknames?.FirstOrDefault(n =>
            string.Equals(n.Username?.Trim(), username, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(n.Nickname))?.Nickname;
        var name = string.IsNullOrWhiteSpace(configured) ? username : configured;
        // Plain text only, including on older clients that use an HTML notification renderer.
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)
            && c is not '<' and not '>' and not '&' && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
            .Take(64).ToArray()).Trim();
        return clean.Length == 0 ? "A party member" : clean;
    }

    private static string FormatPosition(long ticks)
    {
        var time = TimeSpan.FromTicks(Math.Max(0, ticks));
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }
}
