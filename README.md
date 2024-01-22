# Net-sama
 
An easy, powerful and lightweight TCP client/server in C#.

Net-sama makes it easy to setup a robust client & server, send messages and requests, and provide your own SSL certificate.

## Features
- Easy client & server setup
- Support for SSL encryption and certificates
- Automatic serialisation of messages
- Send requests and wait for a response
- Automatic fragmentation of large messages
- Support for IPv4 and IPv6

## Dependencies
- [Newtonsoft.Json](https://www.newtonsoft.com/json)

## Example

```csharp
public static void Server() {
    NetSamaServer Server = new(5000);
    Server.OnConnect += OnConnect;

    Server.Start();
    
    void OnConnect(Connection Client) {
        Client.OnReceive += Message => OnReceive(Client, Message);
        
        Console.WriteLine("Hi, client!");
    }
    void OnReceive(Connection Client, Message Message) {
        if (Message is ExampleMessage ExampleMessage) {
            Console.WriteLine($"Received '{ExampleMessage.ExampleText}' from client!");
        }
    }
}
```
```csharp
public static async void Client() {
    NetSamaClient Client = new();
    Client.OnConnect += OnConnect;

    await Client.ConnectAsync("localhost", 5000);

    Connection Connection = Client.Connection!;

    await Connection.SendAsync(new ExampleMessage("hello server!"));
}
```
```csharp
public class ExampleMessage : Message {
    [JsonProperty] public readonly string ExampleText;
    
    public ExampleMessage(string example_text) {
        ExampleText = example_text;
    }
}
```
```csharp
static void Main() {
    Test.Server();
    Test.Client();
}
```
#### Output
```
Hi, client!
Received 'hello server!' from client!
```
