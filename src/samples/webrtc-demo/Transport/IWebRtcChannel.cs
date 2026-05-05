// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

namespace WebRtcDemo.Transport;

// ---------------------------------------------------------------------------
// Transport Abstraction Layer
// ---------------------------------------------------------------------------
// IWebRtcChannel is intentionally shaped like the libp2p IChannel interface
// (see: Nethermind.Libp2p.Core.IChannel) so that it can later be adapted
// into the full libp2p channel pipeline.
//
// Mapping:
//   RTCDataChannel  ≡  libp2p IChannel  ≡  transport-level byte stream
//
// In a full integration, an IWebRtcChannel implementation would be passed
// into connectionCtx.Upgrade() (replacing the TCP socket), and all upper
// protocols (Noise, Yamux, etc.) would run on top of it unchanged.
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal channel abstraction over a WebRTC DataChannel.
/// Mirrors the role of <c>Nethermind.Libp2p.Core.IChannel</c>.
/// </summary>
public interface IWebRtcChannel
{
    /// <summary>Unique name / label of the underlying DataChannel.</summary>
    string Label { get; }

    /// <summary>True once the DataChannel is open and ready to transfer data.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Send raw bytes over the DataChannel.
    /// This is the write side of the libp2p byte-stream abstraction.
    /// </summary>
    Task SendAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive the next chunk of raw bytes from the DataChannel.
    /// Blocks until data arrives or the channel is closed.
    /// This is the read side of the libp2p byte-stream abstraction.
    /// </summary>
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Close the channel gracefully.
    /// In the libp2p pipeline this would propagate upstream via CloseAsync().
    ///
    /// Future integration note:
    ///   When wiring into ITransportProtocol.DialAsync / ListenAsync, call
    ///   connectionCtx.Upgrade() AFTER this channel is open, and pass data
    ///   through WriteAsync / ReadAllAsync exactly as IpTcpProtocol does.
    /// </summary>
    Task CloseAsync();

    /// <summary>Raised when the channel becomes open (DataChannel.onopen).</summary>
    event Action? OnOpen;

    /// <summary>Raised when the channel is closed remotely or locally.</summary>
    event Action? OnClose;

    /// <summary>Raised when a message arrives (DataChannel.onmessage).</summary>
    event Action<byte[]>? OnMessage;
}
