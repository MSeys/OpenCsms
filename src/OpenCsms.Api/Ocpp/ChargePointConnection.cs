namespace OpenCsms.Api.Ocpp;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenCsms.Application.Ports;
using OpenCsms.Protocol.Ocpp;

/// <summary>An OCPP call this gateway can answer with a call error instead of a result.</summary>
public sealed class OcppCallException(string errorCode, string description) : Exception(description)
{
    /// <summary>The OCPP error code that answers the call.</summary>
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// One connected charge point. The gateway's receive loop routes incoming frames here: calls are
/// dispatched by <see cref="OcppGateway"/>, and the answers to calls this server started resolve the
/// pending <see cref="CallAsync"/> that is waiting for them. One connection is one conversation - the
/// send gate serializes frames, and the pending table correlates server calls by message id.
/// </summary>
public sealed class ChargePointConnection(string chargePointId, WebSocket socket) : IAsyncDisposable, IChargePointConnection
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OcppMessage>> _pending = new(StringComparer.Ordinal);

    /// <summary>The OCPP identity this connection belongs to, as the charge point boots with it.</summary>
    public string ChargePointId { get; } = chargePointId;

    /// <summary>Accepts frames until the charge point closes or the run stops; null means a normal close.</summary>
    public async Task RunAsync(
        Func<OcppCall, CancellationToken, Task<object>> dispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            string? text;
            try
            {
                text = await ReceiveTextAsync(cancellationToken);
            }
            catch (OcppProtocolException exception)
            {
                // A binary frame in a text-only protocol is answered like any other formation violation.
                await SendErrorAsync(string.Empty, OcppErrorCodes.FormationViolation, exception.Message, cancellationToken);
                continue;
            }
            catch (WebSocketException)
            {
                // The charge point went away without a close handshake; the connection is released anyway.
                return;
            }

            if (text is null)
            {
                return;
            }

            await HandleAsync(text, dispatch, cancellationToken);
        }
    }

    /// <summary>
    /// Sends a server-initiated call and waits for the charge point's result. A call error and a shape
    /// that cannot be read both throw, so the operator sees the device's own answer; a connection
    /// disposed under the wait throws <see cref="ChargePointConnectionLostException"/> instead of a
    /// bare cancellation.
    /// </summary>
    private async Task<T> CallAsync<T>(string action, object payload, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(payload);

        var messageId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<OcppMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[messageId] = completion;
        try
        {
            await SendTextAsync(OcppJson.SerializeCall(messageId, action, payload), cancellationToken);
            OcppMessage answer;
            try
            {
                answer = await completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"The charge point '{ChargePointId}' did not answer {action} within {timeout.TotalSeconds:0.#}s.",
                    exception);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Disposing the connection cancels every pending call; the socket went away - a
                // reconnect replaced this connection, or it closed - so no answer can arrive on it.
                throw new ChargePointConnectionLostException(ChargePointId, exception);
            }

            return answer switch
            {
                OcppCallResult result => OcppJson.ReadPayload<T>(result.Payload),
                OcppCallError error => throw new ChargePointCallRefusedException(
                    error.ErrorCode,
                    $"The charge point '{ChargePointId}' refused {action}: {error.Description}"),
                _ => throw new OcppProtocolException($"The answer to {action} was not a result or an error.")
            };
        }
        finally
        {
            _pending.TryRemove(messageId, out _);
        }
    }

    /// <summary>Asks the charge point to start a transaction and reads its authorization decision.</summary>
    public async Task<AuthorizationStatus> RemoteStartAsync(
        string idTag,
        int? connectorId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idTag);
        var answer = await CallAsync<RemoteStartTransactionResponse>(
            OcppActions.RemoteStartTransaction,
            new RemoteStartTransactionRequest(idTag, connectorId),
            timeout,
            cancellationToken);
        return OcppTranslator.ToAuthorization(answer.IdTagInfo.Status);
    }

    /// <summary>Asks the charge point to stop the named transaction and reads its authorization decision.</summary>
    public async Task<AuthorizationStatus> RemoteStopAsync(
        int transactionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var answer = await CallAsync<RemoteStopTransactionResponse>(
            OcppActions.RemoteStopTransaction,
            new RemoteStopTransactionRequest(transactionId),
            timeout,
            cancellationToken);
        return OcppTranslator.ToAuthorization(answer.IdTagInfo.Status);
    }

    /// <summary>Closes the socket politely; a charge point that is already gone needs no reason.</summary>
    public async ValueTask CloseAsync(string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, attempt.Token);
            }
        }
        catch (Exception)
        {
            // Closing is best-effort; the connection is disposed either way.
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pending in _pending.Values)
        {
            pending.TrySetCanceled();
        }

        _pending.Clear();
        await CloseAsync("the gateway is stopping");
        socket.Dispose();
        _sendGate.Dispose();
    }

    private async Task HandleAsync(
        string text,
        Func<OcppCall, CancellationToken, Task<object>> dispatch,
        CancellationToken cancellationToken)
    {
        if (!OcppJson.TryParse(text, out var message, out var error))
        {
            await SendErrorAsync(string.Empty, OcppErrorCodes.FormationViolation, error, cancellationToken);
            return;
        }

        switch (message)
        {
            case OcppCall call:
                try
                {
                    var payload = await dispatch(call, cancellationToken);
                    await SendTextAsync(OcppJson.SerializeCallResult(call.MessageId, payload), cancellationToken);
                }
                catch (OcppCallException exception)
                {
                    await SendErrorAsync(call.MessageId, exception.ErrorCode, exception.Message, cancellationToken);
                }
                catch (OcppProtocolException exception)
                {
                    await SendErrorAsync(call.MessageId, OcppErrorCodes.FormationViolation, exception.Message, cancellationToken);
                }
                catch (JsonException exception)
                {
                    // A typed payload that does not match its contract is a formation violation, not a crash.
                    await SendErrorAsync(call.MessageId, OcppErrorCodes.FormationViolation, exception.Message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await SendErrorAsync(
                        call.MessageId,
                        OcppErrorCodes.InternalError,
                        $"The CSMS could not handle {call.Action}: {exception.Message}",
                        cancellationToken);
                }

                break;
            case OcppCallResult result:
                Resolve(result);
                break;
            case OcppCallError callError:
                Resolve(callError);
                break;
            default:
                await SendErrorAsync(string.Empty, OcppErrorCodes.NotSupported, "The frame type is not supported.", cancellationToken);
                break;
        }
    }

    private void Resolve(OcppMessage message)
    {
        if (_pending.TryRemove(message.MessageId, out var completion))
        {
            completion.TrySetResult(message);
        }
    }

    private ValueTask SendErrorAsync(string messageId, string errorCode, string description, CancellationToken cancellationToken)
        => SendTextAsync(OcppJson.SerializeCallError(messageId, errorCode, description), cancellationToken);

    private async ValueTask SendTextAsync(string text, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new ChargePointConnectionLostException(ChargePointId);
            }

            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The peer is gone or the socket was disposed under the send; the frame did not
                // travel a live connection.
                throw new ChargePointConnectionLostException(ChargePointId, exception);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            switch (result.MessageType)
            {
                case WebSocketMessageType.Close:
                    return null;
                case WebSocketMessageType.Binary:
                    throw new OcppProtocolException("OCPP 1.6J is text JSON; binary frames are not supported.");
                default:
                    message.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(message.ToArray());
                    }

                    break;
            }
        }
    }
}
