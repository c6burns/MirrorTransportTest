# Mirror Transport Stress Test

*Note* -- Requires Unity 2018.3.13+

**Testing a Transport**

1.  Open Build Settings and move the appropriate scene for the correct transport to the top of the list (slot 0).

2.  Make sure Server Build is checked.

3.  Click Build

To start the build as client, run the EXE with a command line arg of "client". Without this, the default mode is to run as a headless server.

Additionally, server address and, optionally, port may also be specified per these examples:

```cs
// IPv4
MirrorLoadTest.exe client 12.34.56.78
MirrorLoadTest.exe client 12.34.56.78 7777

// IPv6
MirrorLoadTest.exe client FE80::0202:B3FF:FE1E:8329
MirrorLoadTest.exe client FE80::0202:B3FF:FE1E:8329 7777

// FQDN
MirrorLoadTest.exe client game.example.com
MirrorLoadTest.exe client game.example.com 7777
```

**How it works**

Each client has a SyncVar of a Struct comprised of a message id and a timestamp. At the specified Send Interval, each client will invoke a [Command] to update the SyncVar with a new message, which is also stored in a Sent Messages List, and increment an internal count of messages sent. The server will return this message to the client and send it to all observers. The sending client will compare the timestamp of the returned message with the current time (Stopwatch) and add the delta to a cummulative total, as well as increment the count of messages received, and remove that message from the Sent Messages List. If the message is not found in the list, it will increment the Unks count (unknown messages). If the message is not the first in the list, it will increment the OOOs count (Out of Order messages).

Additionally, the clients will update the server at the Update Interval with their cummulative stats.

If the Sent Messages List grows too large, the client must assume that either the messages it's sending aren't reaching the server, or the server's messages aren't reaching the client, or the server is unable to get the messages out fast enough to keep up with the clients. When the Sent Messages List reaches the Backlog Limit, the client will disconnect itself.

Based on the Print Interval, the server will output aggregate stats based on what the clients have reported. Column headings will be repeated every n lines per the Repeat Header Lines value. Column headings that have "-A" are averages, and those with "-T" are sum totals for all clients.

**Custom Observers**

If Custom Observers is enabled, the output will include an average number of observers per client. Obeservers are updated only when a client joins or disconnects based on OverlapSphereNonAlloc and Vector3.Distance based on the Visible Range property of the Area of Interest component and the Layer Mask. Area of Interest by default is layer masked to the Player layer, which the Player prefab is set for.

If Custom Observers is disabled, all clients are observers of all others, which puts the maximum message output load on the server, because for every incoming SyncVar message from any given client, the server must generate an outgoing message to all clients, including the sender. With 1000 clients, this would produce 1M messages per update loop cycle.

The Spawn Volume translates to width, height, and depth of a cube within which to spawn the clients. The scene origin will be at the center of this volume.
