![Icon](https://raw.githubusercontent.com/Joy-less/TCPMaid/main/Assets/IconMini.png)

# TCPMaid

[![NuGet](https://img.shields.io/nuget/v/TCPMaid.svg)](https://www.nuget.org/packages/TCPMaid)

An easy, powerful and lightweight TCP client/server in C#.

TCPMaid makes it easy to setup a robust client & server, send messages and requests, and provide your own SSL certificate.

## Features
- Easy client & server setup
- Supports TCP and UDP
- Supports SSL encryption and certificates
- Automatically serialises messages
- Send requests and await a response
- Automatically fragments large messages
- Supports IPv4 and IPv6

## Dependencies
- [Newtonsoft.Json](https://www.newtonsoft.com/json)

## Example

```cs
public static void Server() {
    // Start server on port 5000
    TCPMaidServer Server = new(5000);
    Server.Start();

    // Listen to connect event
    Server.OnConnect += OnConnect;
    Server.OnReceive += OnReceive;
    
    // Events
    void OnConnect(Connection Client) {
        Console.WriteLine("Hi, client!");
    }
    void OnReceive(Connection Client, Message Message) {
        if (Message is ExampleMessage ExampleMessage) {
            Console.WriteLine($"Received '{ExampleMessage.ExampleText}' from client!");
        }
    }
}
```
```cs
public static async void Client() {
    // Connect client to server
    TCPMaidClient Client = new();
    await Client.ConnectAsync("localhost", 5000);

    // Say hello to server
    await Client.Server!.SendAsync(new ExampleMessage("hello server!"));
}
```
```cs
public class ExampleMessage : Message {
    [JsonProperty] public readonly string ExampleText;
    
    public ExampleMessage(string example_text) {
        ExampleText = example_text;
    }
}
```
#### Output
```
Hi, client!
Received 'hello server!' from client!
```
