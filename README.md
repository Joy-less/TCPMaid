![Icon](https://raw.githubusercontent.com/Joy-less/TCPMaid/main/Assets/IconMini.png)

# TCPMaid

[![NuGet](https://img.shields.io/nuget/v/TCPMaid.svg)](https://www.nuget.org/packages/TCPMaid)

An easy, powerful and lightweight TCP client/server in C#.

TCPMaid makes it easy to setup a robust client & server, send messages and requests, and provide your own SSL certificate.

> [!WARNING]
> This repository is now in critical maintenance mode. Consider using embedded alternatives like [Godot's RPCs](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)!

## Features
- Easy client & server setup
- Supports SSL (TLS) encryption and certificates
- Automatically serialises messages to bytes
- Automatically fragments large messages to avoid congestion
- Supports requests and responses
- Supports IPv4 and IPv6

## Dependencies
- [MemoryPack](https://github.com/Cysharp/MemoryPack)

## Example

#### Client
```cs
public static async void Client() {
    // Connect to server
    using ClientMaid Client = new();
    await Client.ConnectAsync("localhost", 5000);

    // Say hello to server
    await Client.Channel!.SendAsync(new ExampleMessage("hello server!"));

    // Disconnect from server
    await Client.Channel!.DisconnectAsync();
}
```
#### Server
```cs
public static void Server() {
    // Start server on port 5000
    ServerMaid Server = new();
    Server.Start(5000);

    // Listen to events
    Server.OnConnect += OnConnect;
    Server.OnReceive += OnReceive;

    // Events
    void OnConnect(Channel Client) {
        Console.WriteLine("Hi, client!");
    }
    void OnReceive(Channel Client, Message Message) {
        if (Message is ExampleMessage ExampleMessage) {
            Console.WriteLine($"Received '{ExampleMessage.ExampleText}' from client!");
        }
    }
}
```
#### Shared
```cs
[MemoryPackable]
public partial record ExampleMessage(string ExampleText) : Message;
```
#### Output
```
Hi, client!
Received 'hello server!' from client!
```

## Requests

The client may want to ask the server for data. To send a message and wait for a response, you can use `RequestAsync`.

#### Client
```cs
// Send an ExampleRequest and wait for an ExampleResponse with the same message ID
ExampleResponse? Response = await Client.Channel!.RequestAsync<ExampleResponse>(new ExampleRequest());
Console.WriteLine(Response!.ExampleText);
```
#### Server
```cs
Server.OnReceive += (Channel, Message) => {
    if (Message is ExampleRequest ExampleRequest) {
        _ = Channel.SendAsync(new ExampleResponse(ExampleRequest.Id, "Here's my response: -.-"));
    }
};
```
#### Shared
```cs
[MemoryPackable]
public partial record ExampleRequest : Message;
[MemoryPackable]
public partial record ExampleResponse(Guid Id, string ExampleText) : Message(Id);
```
#### Output
```
Here's my response: -.-
```

## Encryption

To encrypt the connection, simply pass an `X509Certificate2` to `ServerMaidOptions` and enable `Ssl` in `ClientMaidOptions`.
An `SslStream` (TLS) is automatically created and authenticated.

#### Client
```cs
ClientMaid ClientMaid = new(new ClientMaidOptions() {
    Ssl = true,
    ServerName = "example.com" // Optional
});
```
#### Server
```cs
ServerMaid ServerMaid = new(new ServerMaidOptions() {
    Certificate = X509Certificate2.CreateFromPemFile(
        certPemFilePath: "/etc/letsencrypt/live/example.com/fullchain.pem",
        keyPemFilePath: "/etc/letsencrypt/live/example.com/privkey.pem"
    )
});
```

## Streams

You may want to send a large file without keeping it all in memory. To send data from a stream, you can use `SendStreamAsync` and `ReceiveStreamAsync`.

#### Sender
```cs
using FileStream Reader = File.OpenRead("Cat.png");
await Channel.SendStreamAsync("Cat Picture", Reader);
```

#### Receiver
```cs
using FileStream Writer = File.OpenWrite("Cat.png");
await Channel.ReceiveStreamAsync("Cat Picture", Writer);
```