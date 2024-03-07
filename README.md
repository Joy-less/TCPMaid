![Icon](https://raw.githubusercontent.com/Joy-less/TCPMaid/main/Assets/IconMini.png)

# TCPMaid

[![NuGet](https://img.shields.io/nuget/v/TCPMaid.svg)](https://www.nuget.org/packages/TCPMaid)

An easy, powerful and lightweight TCP client/server in C#.

TCPMaid makes it easy to setup a robust client & server, send messages and requests, and provide your own SSL certificate.

## Features
- Easy client & server setup
- Supports SSL encryption and certificates
- Automatically serialises messages
- Send requests and await a response
- Automatically fragments large messages
- Supports IPv4 and IPv6

## Dependencies
- [MemoryPack](https://github.com/Cysharp/MemoryPack)

## Example

```cs
public static void Server() {
    // Start server on port 5000
    ServerMaid Server = new();
    Server.Start(5000);

    // Listen to connect event
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
```cs
public static async void Client() {
    // Connect client to server
    ClientMaid Client = new();
    await Client.ConnectAsync("localhost", 5000);

    // Say hello to server
    await Client.Channel!.SendAsync(new ExampleMessage("hello server!"));
}
```
```cs
[MemoryPackable]
public partial class ExampleMessage : Message {
    public readonly string ExampleText;

    public ExampleMessage(string ExampleText) {
        this.ExampleText = ExampleText;
    }
}
```
#### Output
```
Hi, client!
Received 'hello server!' from client!
```
