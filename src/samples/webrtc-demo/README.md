# WebRTC DataChannel Prototype

**Note:** This is a prototype and is not integrated with the core libp2p stack yet.

## What it does
This sample demonstrates a minimal WebRTC DataChannel-based transport prototype. It simulates two peers (Peer A and Peer B) connecting via WebRTC within a single process. It establishes a `DataChannel` through an in-memory offer/answer exchange, proving that the `SIPSorcery` library can be used to handle WebRTC handshakes and act as a byte-stream transport for libp2p. 

## How to run
Run the sample using the .NET CLI:
```bash
cd src/samples/webrtc-demo
dotnet run
```

## Expected output
```text
[Main] Both DataChannels are open -> starting message exchange

[PeerB] Receiver started -> awaiting messages…
[PeerA] → Sending: "Hello from Peer A! "
[PeerB] ✉  Received: "Hello from Peer A! "
...
  Prototype complete -> DataChannel exchange succeeded!
```

## How this maps to libp2p

This prototype helped in understanding how WebRTC can be integrated as a transport layer in dotnet-libp2p.

Conceptually, the flow is:

WebRTC PeerConnection → DataChannel → libp2p Channel → Upgrade → Session

- The `DataChannel` acts as the transport-level byte stream
- It can be adapted into a libp2p-compatible channel
- The existing upgrade pipeline (security + multiplexing) can remain unchanged

This confirms that WebRTC can replace TCP at the transport layer while preserving the overall architecture.
