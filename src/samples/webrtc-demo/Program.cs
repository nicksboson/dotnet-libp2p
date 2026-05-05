// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using WebRtcDemo.Transport;
using WebRtcDemo.WebRtc;

// ---------------------------------------------------------------------------
// WebRTC In-Process Signaling Demo
// ---------------------------------------------------------------------------
// This program simulates two libp2p peers communicating over a WebRTC
// DataChannel — all within a single process, without a real network.
//
// Signaling is done in-memory (SDP objects passed directly between instances)
// to keep this a minimal MVP prototype as described in mvp.md Day 4.
//
// Architecture summary:
//
//   Peer A (Offerer)               Peer B (Answerer)
//   ────────────────               ─────────────────
//   RTCPeerConnection              RTCPeerConnection
//        │                              │
//        ├─ createDataChannel()        ├─ ondatachannel (auto)
//        │                              │
//        ├─ createOffer()  ──SDP──►    ├─ setRemoteDescription()
//        │                              ├─ createAnswer()
//        │                ◄─SDP──      │
//        ├─ setRemoteDescription()     │
//        │                              │
//        └─ DataChannel OPEN ──────────┘
//             (IWebRtcChannel)
//
// libp2p mapping:
//   RTCDataChannel  ≡  transport-level byte stream (like TCP socket)
//   DataChannelAdapter ≡ IChannel (read/write wrapper)
//   connectionCtx.Upgrade() would be called when DataChannel opens
// ---------------------------------------------------------------------------

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddSimpleConsole(c =>
    {
        c.SingleLine = true;
        c.TimestampFormat = "[HH:mm:ss.fff] ";
    })
    .SetMinimumLevel(LogLevel.Debug));

var log = loggerFactory.CreateLogger("WebRtcDemo");

log.LogInformation("=======================================================");
log.LogInformation("  dotnet-libp2p  |  WebRTC DataChannel Prototype (MVP) ");
log.LogInformation("=======================================================");
log.LogInformation("");

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

// ── Create both peers ──────────────────────────────────────────────────────
await using var peerA = new WebRtcPeer("PeerA", loggerFactory.CreateLogger<WebRtcPeer>());
await using var peerB = new WebRtcPeer("PeerB", loggerFactory.CreateLogger<WebRtcPeer>());

log.LogInformation("[Main] Peer A creates offer SDP…");
RTCSessionDescriptionInit offer = await peerA.CreateOfferAsync("libp2p-data");

log.LogInformation("[Main] Offer SDP handed to Peer B for answering…");
RTCSessionDescriptionInit answer = await peerB.CreateAnswerAsync(offer);

log.LogInformation("[Main] Answer SDP handed back to Peer A…");
await peerA.SetRemoteAnswerAsync(answer);

// ── Wait for DataChannels to open on both sides ────────────────────────────
log.LogInformation("[Main] Waiting for DataChannels to open…");

IWebRtcChannel channelA = await peerA.ChannelReady.WaitAsync(cts.Token);
IWebRtcChannel channelB = await peerB.ChannelReady.WaitAsync(cts.Token);

log.LogInformation("[Main] Both DataChannels are open — starting message exchange");
log.LogInformation("");

// ── Message exchange ───────────────────────────────────────────────────────
// Simulate what libp2p protocols would do after Upgrade():
// write bytes to the transport → read bytes on the remote side.

var messages = new[]
{
    "Hello from Peer A! 👋",
    "This message travels over a WebRTC DataChannel",
    "In libp2p, this byte-stream feeds into Noise → Yamux → protocol handlers",
    "Goodbye!"
};

// Start receiver on Peer B in the background
var receiveTask = Task.Run(async () =>
{
    log.LogInformation("[PeerB] Receiver started — awaiting messages…");
    for (int i = 0; i < messages.Length; i++)
    {
        byte[] raw = await channelB.ReceiveAsync(cts.Token);
        if (raw.Length == 0) break;
        string text = System.Text.Encoding.UTF8.GetString(raw);
        log.LogInformation("[PeerB] ✉  Received: \"{Message}\"", text);
    }
    log.LogInformation("[PeerB] All messages received ✓");
}, cts.Token);

// Peer A sends messages with a small delay so output is readable
await Task.Delay(100, cts.Token);   // brief pause after channel open
foreach (string msg in messages)
{
    log.LogInformation("[PeerA] → Sending: \"{Message}\"", msg);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(msg);
    await channelA.SendAsync(bytes, cts.Token);
    await Task.Delay(150, cts.Token);
}

await receiveTask;

log.LogInformation("");
log.LogInformation("=======================================================");
log.LogInformation("  Prototype complete — DataChannel exchange succeeded!");
log.LogInformation("=======================================================");
log.LogInformation("");
log.LogInformation("Integration mapping recap:");
log.LogInformation("  RTCPeerConnection  ≡  TCP connection (transport layer)");
log.LogInformation("  RTCDataChannel     ≡  TCP socket stream (byte channel)");
log.LogInformation("  DataChannelAdapter ≡  IChannel (libp2p.Core)");
log.LogInformation("  Upgrade() hook     ≡  point where Noise/Yamux stacks");
log.LogInformation("                        would be layered on top");
