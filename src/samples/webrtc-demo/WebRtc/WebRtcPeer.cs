// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace WebRtcDemo.WebRtc;

// ---------------------------------------------------------------------------
// WebRTC Connection Logic
// ---------------------------------------------------------------------------
// WebRtcPeer encapsulates the RTCPeerConnection lifecycle:
//   1. Create PeerConnection with default ICE configuration.
//   2. (Offerer) Create a DataChannel, then generate an Offer SDP.
//   3. (Answerer) Accept an Offer SDP, generate an Answer SDP.
//   4. Apply the remote SDP from the other peer (in-process / loopback).
//   5. DataChannel opens → signalled via TaskCompletionSource.
//
// Deliberately minimal:
//   - No real network signaling server; SDPs passed in-memory.
//   - Both peers live in the same process.
//   - No STUN / TURN server (per MVP constraint).
//
// Future integration note:
//   Replace in-process signaling with a real signaling channel
//   (WebSocket, libp2p stream, etc.) to enable actual remote connectivity.
// ---------------------------------------------------------------------------

/// <summary>
/// Wraps an <see cref="RTCPeerConnection"/> and manages the offer/answer
/// handshake plus DataChannel negotiation.
/// </summary>
public sealed class WebRtcPeer : IAsyncDisposable
{
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly RTCPeerConnection _peerConnection;

    // Resolved when the primary DataChannel becomes open
    private readonly TaskCompletionSource<Transport.DataChannelAdapter> _channelReady = new();

    private Transport.DataChannelAdapter? _channel;

    public string Name => _name;

    /// <summary>
    /// Awaitable: resolves to the open <see cref="Transport.DataChannelAdapter"/>
    /// once the WebRTC handshake completes and the DataChannel is usable.
    ///
    /// This is the point in the lifecycle where a real integration would call
    /// connectionCtx.Upgrade() to inject the channel into the libp2p stack.
    /// </summary>
    public Task<Transport.DataChannelAdapter> ChannelReady => _channelReady.Task;

    public WebRtcPeer(string name, ILogger logger)
    {
        _name = name;
        _logger = logger;

        // RTCConfiguration with no STUN/TURN per prototype constraints.
        // Phase 2 of the roadmap would add: iceServers = [new RTCIceServer { urls = "stun:stun.l.google.com:19302" }]
        var config = new RTCConfiguration { iceServers = [] };

        _peerConnection = new RTCPeerConnection(config);

        // Log ICE / connection state changes for observability
        _peerConnection.oniceconnectionstatechange += state =>
            _logger.LogInformation("[{Name}] ICE state → {State}", _name, state);

        _peerConnection.onconnectionstatechange += state =>
            _logger.LogInformation("[{Name}] Connection state → {State}", _name, state);

        // Wire incoming DataChannel (fires on the Answerer side)
        _peerConnection.ondatachannel += channel =>
        {
            _logger.LogInformation("[{Name}] Remote DataChannel received: '{Label}'", _name, channel.label);
            WireChannel(channel);
        };
    }

    // -----------------------------------------------------------------------
    // Offer / Answer helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a DataChannel and generates an SDP offer.
    /// Call this on Peer A (the Offerer).
    /// </summary>
    public async Task<RTCSessionDescriptionInit> CreateOfferAsync(string dataChannelLabel = "libp2p")
    {
        // Create the DataChannel BEFORE creating the offer so it is reflected in SDP.
        // In libp2p terms, this is the "open a stream" step.
        RTCDataChannel dc = await _peerConnection.createDataChannel(dataChannelLabel);
        _logger.LogInformation("[{Name}] DataChannel '{Label}' created (offerer side)", _name, dc.label);
        WireChannel(dc);

        RTCSessionDescriptionInit offer = _peerConnection.createOffer();

        // setLocalDescription is Task-returning (void) in SIPSorcery 6.x;
        // setRemoteDescription is synchronous and returns SetDescriptionResultEnum.
        await _peerConnection.setLocalDescription(offer);

        _logger.LogInformation("[{Name}] Local offer SDP set", _name);
        return offer;
    }

    /// <summary>
    /// Accepts a remote offer SDP and returns an answer SDP.
    /// Call this on Peer B (the Answerer).
    /// </summary>
    public async Task<RTCSessionDescriptionInit> CreateAnswerAsync(RTCSessionDescriptionInit remoteOffer)
    {
        SetDescriptionResultEnum remoteResult = _peerConnection.setRemoteDescription(remoteOffer);
        if (remoteResult != SetDescriptionResultEnum.OK)
            throw new InvalidOperationException($"[{_name}] setRemoteDescription (offer) failed: {remoteResult}");

        _logger.LogInformation("[{Name}] Remote offer SDP accepted", _name);

        RTCSessionDescriptionInit answer = _peerConnection.createAnswer();

        // setLocalDescription is void-Task in SIPSorcery 6.x
        await _peerConnection.setLocalDescription(answer);

        _logger.LogInformation("[{Name}] Local answer SDP set", _name);
        return answer;
    }

    /// <summary>
    /// Applies the remote answer SDP.
    /// Call this on the Offerer (Peer A) after receiving Peer B's answer.
    /// </summary>
    public Task SetRemoteAnswerAsync(RTCSessionDescriptionInit remoteAnswer)
    {
        SetDescriptionResultEnum result = _peerConnection.setRemoteDescription(remoteAnswer);
        if (result != SetDescriptionResultEnum.OK)
            throw new InvalidOperationException($"[{_name}] setRemoteDescription (answer) failed: {result}");

        _logger.LogInformation("[{Name}] Remote answer SDP accepted — handshake complete", _name);
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private void WireChannel(RTCDataChannel dc)
    {
        var adapter = new Transport.DataChannelAdapter(dc);
        _channel = adapter;

        adapter.OnOpen += () =>
        {
            _logger.LogInformation("[{Name}] DataChannel '{Label}' is OPEN ✓", _name, adapter.Label);
            // ----------------------------------------------------------------
            // FUTURE INTEGRATION POINT — Upgrade()
            // ----------------------------------------------------------------
            // In a full WebRtcTransport : ITransportProtocol implementation
            // this is where the libp2p upgrade chain starts:
            //
            //   IChannel upChannel = connectionCtx.Upgrade();
            //   // Bridge adapter ↔ upChannel I/O loops (same pattern as IpTcpProtocol):
            //   //   Task readTask  = ForwardDataChannelToUpChannel(adapter, upChannel);
            //   //   Task writeTask = ForwardUpChannelToDataChannel(upChannel, adapter);
            //   //   await Task.WhenAny(readTask, writeTask)
            //   //         .ContinueWith(_ => connectionCtx.Dispose());
            // ----------------------------------------------------------------
            _channelReady.TrySetResult(adapter);
        };

        adapter.OnClose += () =>
            _logger.LogInformation("[{Name}] DataChannel '{Label}' closed", _name, adapter.Label);
    }

    public async ValueTask DisposeAsync()
    {
        await (_channel?.CloseAsync() ?? Task.CompletedTask);
        _peerConnection.close();
        _peerConnection.Dispose();
    }
}
