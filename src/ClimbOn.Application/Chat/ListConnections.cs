using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Chat;

// A connection of the viewer with the other climber's card (S54). Partner is null when the other climber's stored
// profile is one today's rules refuse; DisplayName is then their stored display name if it alone passes those rules,
// otherwise null, and the neutral placeholder (UnnamedPartnerKey) is shown instead.
public sealed record ConnectionEntry(Connection Connection, string? DisplayName, ClimberCard? Partner);

// The viewer's connections, newest first.
public sealed class ListConnections(IChatStore chat, ICandidateStore climbers, IProfileStore profiles, GetSuggestions suggestions)
{
    public const string UnnamedPartnerKey = "connection_partner_unnamed";

    public async Task<IReadOnlyList<ConnectionEntry>> RunAsync(Guid viewerId, CancellationToken cancellationToken)
    {
        var connections = (await chat.ListConnectionsAsync(viewerId, cancellationToken))
            .OrderByDescending(connection => connection.CreatedAt)
            .ToList();
        if (connections.Count == 0)
        {
            return [];
        }

        // The viewer's own stored profile that today's rules refuse is refused with its code, as on the suggestion
        // list and the invite list. A partner is read whatever their state, dormant included (S107).
        var viewer = await suggestions.RequesterAsync(viewerId, cancellationToken);
        var partnerIds = connections.Select(connection => connection.PartnerId).ToHashSet();
        var cards = new Dictionary<Guid, ClimberCard>();
        if (viewer is not null)
        {
            var found = await climbers.FindAsync(partnerIds, cancellationToken);
            var gyms = await profiles.FindGymsAsync(
                found.Append(viewer).SelectMany(climber => climber.Profile.HomeGyms).Distinct().ToList(), cancellationToken);
            foreach (var climber in found)
            {
                cards[climber.AccountId] = CardBuilder.For(viewer, climber, gyms);
            }
        }

        var stored = await profiles.FindStoredAsync(partnerIds.Where(id => !cards.ContainsKey(id)).ToHashSet(), cancellationToken);

        return connections
            .Select(connection => cards.TryGetValue(connection.PartnerId, out var card)
                ? new ConnectionEntry(connection, card.DisplayName, card)
                : new ConnectionEntry(
                    connection, Profile.ValidDisplayName(stored.GetValueOrDefault(connection.PartnerId)?.DisplayName), null))
            .ToList();
    }
}
