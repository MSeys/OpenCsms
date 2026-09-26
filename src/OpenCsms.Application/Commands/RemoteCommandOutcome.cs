namespace OpenCsms.Application.Commands;

using OpenCsms.Application.Ports;

/// <summary>What a server-initiated operator command ended in; the caller maps each to its own answer.</summary>
public abstract record RemoteCommandOutcome;

/// <summary>The charge point answered; the device's authorization decision is the payload, not an error.</summary>
public sealed record RemoteCommandAccepted(AuthorizationStatus Status) : RemoteCommandOutcome;

/// <summary>No station with that id is registered.</summary>
public sealed record RemoteCommandStationMissing(Guid StationId) : RemoteCommandOutcome;

/// <summary>No session with that id is stored.</summary>
public sealed record RemoteCommandSessionMissing(Guid SessionId) : RemoteCommandOutcome;

/// <summary>The session the stop named is already ended.</summary>
public sealed record RemoteCommandSessionEnded(Guid SessionId) : RemoteCommandOutcome;

/// <summary>The station's charge point is not connected right now.</summary>
public sealed record RemoteCommandChargePointOffline(string ChargePointId) : RemoteCommandOutcome;

/// <summary>The charge point took the call but did not answer within the configured timeout.</summary>
public sealed record RemoteCommandTimedOut(string Message) : RemoteCommandOutcome;

/// <summary>The charge point refused the call with an OCPP call error.</summary>
public sealed record RemoteCommandRefused(string Message) : RemoteCommandOutcome;
