// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using SIPSorcery.Net;

namespace WebRtcDemo.Transport;

// ---------------------------------------------------------------------------
// DataChannel Channel Adapter
// ---------------------------------------------------------------------------
// DataChannelAdapter wraps an RTCDataChannel (from SIPSorcery) and exposes it
// as an IWebRtcChannel — the transport-level byte stream equivalent.
//
// Relation to libp2p internals:
//   In IpTcpProtocol (the existing TCP transport), data flows through a
//   Socket → IChannel pipe created by connectionCtx.Upgrade().
//
//   Here, the RTCDataChannel takes the role of the Socket:
//     RTCDataChannel.send()    ↔  socket.SendAsync()
//     RTCDataChannel.onmessage ↔  socket.ReceiveAsync()
//
//   A future WebRtcTransport : ITransportProtocol would:
//     1. Complete the ICE+DTLS handshake (the "TCP connect" equivalent).
//     2. Open a DataChannel (the "socket stream" equivalent).
//     3. Call connectionCtx.Upgrade() to push data into the libp2p stack.
//     4. Bridge Upgrade's IChannel ↔ this DataChannelAdapter I/O loops.
// ---------------------------------------------------------------------------

/// <summary>
/// Bridges an <see cref="RTCDataChannel"/> into the <see cref="IWebRtcChannel"/>
/// abstraction so it can later be adapted into a libp2p IChannel.
/// </summary>
public sealed class DataChannelAdapter : IWebRtcChannel, IDisposable
{
    private readonly RTCDataChannel _dataChannel;

    // Unbounded queue: producer = DataChannel.onmessage, consumer = ReceiveAsync
    private readonly BlockingCollection<byte[]> _inbox = new();
    private bool _disposed;

    public event Action? OnOpen;
    public event Action? OnClose;
    public event Action<byte[]>? OnMessage;

    public string Label => _dataChannel.label;
    public bool IsOpen => _dataChannel.readyState == RTCDataChannelState.open;

    public DataChannelAdapter(RTCDataChannel dataChannel)
    {
        _dataChannel = dataChannel ?? throw new ArgumentNullException(nameof(dataChannel));

        // Wire DataChannel events → IWebRtcChannel events + internal queue
        _dataChannel.onopen  += () => OnOpen?.Invoke();
        _dataChannel.onclose += () =>
        {
            _inbox.CompleteAdding();   // unblock any pending ReceiveAsync
            OnClose?.Invoke();
        };

        _dataChannel.onmessage += (_, _, data) =>
        {
            if (!_inbox.IsAddingCompleted)
            {
                _inbox.TryAdd(data);
                OnMessage?.Invoke(data);
            }
        };

        // On the answerer side, ondatachannel can fire AFTER the DataChannel
        // is already open (onopen already fired before we registered the handler).
        // Detect this and fire OnOpen immediately so ChannelReady resolves.
        if (_dataChannel.readyState == RTCDataChannelState.open)
        {
            Task.Run(() => OnOpen?.Invoke());
        }
    }

    /// <inheritdoc/>
    public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
            throw new InvalidOperationException($"DataChannel '{Label}' is not open.");

        _dataChannel.send(data);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                // Blocks until a message arrives or the channel is closed
                return _inbox.Take(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<byte>();
            }
            catch (InvalidOperationException)
            {
                // _inbox.CompleteAdding() was called (channel closed)
                return Array.Empty<byte>();
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task CloseAsync()
    {
        // DataChannel close is handled by the owning PeerConnection in this prototype.
        // In a full integration, this would trigger the libp2p channel teardown:
        //   await upChannel.CloseAsync();   // propagates EOF upward through the stack
        _inbox.CompleteAdding();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _inbox.Dispose();
        }
    }
}
