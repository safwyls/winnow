using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

public enum ActivitySection { Sessions, Updates, Journal }
public sealed record ActivityCursor(DateTime AtUtc, long Id);
public sealed record ActivityRow(long OwnershipId, string Store, DateTime AtUtc,
    Session? Session, SessionNote? Note, UpdateEvent? Update);
public sealed record ActivityPage(IReadOnlyList<ActivityRow> Rows, ActivityCursor? Next);
