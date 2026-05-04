# Transport Layer in dotnet-libp2p

## What is the transport layer?

The transport layer is responsible for creating connections between peers and sending raw data over the network.

It is the lowest layer in the stack. It does not deal with security or multiplexing. Those are handled later during the upgrade phase.

---

## Existing transports

### TCP

TCP creates a standard socket connection between peers. Once the connection is established, it passes the data stream to the upgrade pipeline.

### QUIC

QUIC works over UDP and supports multiplexing internally. It also uses TLS-based encryption. Like TCP, it eventually passes data into the same upgrade flow.

---

## How a connection is created

A connection usually starts in one of two ways:

* DialAsync is used for outgoing connections
* StartListenAsync is used to accept incoming connections

After that:

1. The appropriate transport is selected
2. A connection is established
3. The connection is passed to the upgrade pipeline
4. A session is created

---

## How data flows

Once a session is ready, data flows like this:

```text id="flow4"
Application → Stream → Multiplexer → Transport
```

---

## Adding a new transport

To add a new transport:

* Implement ITransportProtocol
* Provide DialAsync and ListenAsync
* Create a connection using your protocol
* Convert it into a libp2p-compatible channel
* Pass it into the upgrade pipeline

---

## WebRTC as a transport

WebRTC can be added as another transport alongside TCP and QUIC.

Instead of sockets, it uses:

* PeerConnection for establishing connections
* DataChannel for sending data

The idea is to treat the DataChannel as a byte stream and integrate it into the existing pipeline.

---

## Proposed WebRTC flow

```text id="flow5"
WebRTC PeerConnection
        ↓
   DataChannel
        ↓
     Channel
        ↓
     Upgrade
        ↓
     Session
```

---

## Things to consider

* WebRTC requires signaling to establish connections
* NAT traversal needs to be handled
* Interoperability with other implementations will be important

    
