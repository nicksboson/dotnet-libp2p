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
