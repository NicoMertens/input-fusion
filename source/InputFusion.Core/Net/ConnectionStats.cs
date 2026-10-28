namespace InputFusion.Core.Net;

/// <summary>Zähler einer <see cref="MessageConnection"/> seit ihrem Aufbau, Heartbeats eingeschlossen.</summary>
/// <param name="RoundTrip">Letzte Laufzeit eines Heartbeats hin und zurück; null, solange die Gegenseite keinen zurückgeschickt hat.</param>
public sealed record ConnectionStats(Int64 SentFrames, Int64 ReceivedFrames, TimeSpan? RoundTrip);
