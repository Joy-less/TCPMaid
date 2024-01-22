![Icon](Assets/Icon Mini.png)

# TCPMaid
 
An easy, powerful and lightweight TCP client/server in C#.

TCPMaid makes it easy to setup a robust client & server, send messages and requests, and provide your own SSL certificate.

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

```cs
public static void Server() {
    TCPMaidServer Server = new(5000);
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
```cs
public static async void Client() {
    TCPMaidClient Client = new();

    await Client.ConnectAsync("localhost", 5000);
    Connection Connection = Client.Connection!;

    await Connection.SendAsync(new ExampleMessage("hello server!"));
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
