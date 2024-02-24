namespace TCPMaid.Benchmark {
    internal class Program {
        static void Main() {
            /*TCPMaidServer Server2 = new(12345);
            Server2.Start();
            Server2.OnReceive += (Client, Message) => {
                Console.WriteLine($"{Client.RemoteEndPoint} {Message}");
            };
            TCPMaidClient Client2 = new();
            Client2.ConnectAsync("localhost", 12345).Wait();
            TCPMaidClient Client3 = new();
            Client3.ConnectAsync("localhost", 12345).Wait();
            while (true) {
                Client2.Server!.SendAsync(new BlankMessageUDP(), Protocol.UDP).Wait();
                Client3.Server!.SendAsync(new BlankMessageUDP(), Protocol.UDP).Wait();
                Client2.Server.SendAsync(new BlankMessageTCP(), Protocol.TCP).Wait();
                Client3.Server.SendAsync(new BlankMessageTCP(), Protocol.TCP).Wait();
                Task.Delay(100).Wait();
            }*/

            // Initialise server
            TCPMaidServer Server = new(12345);
            Server.OnConnect += (Connection) => {
                Console.WriteLine($"{Server.ClientCount} clients.");
            };
            Server.OnDisconnect += (Connection, ByRemote, Reason) => {
                Console.WriteLine($"Disconnected: {Reason}");
            };
            Server.Start();

            // Broadcast messages
            Task.Run(async () => {
                while (true) {
                    const double BroadcastsPerSecond = 10;
                    await Task.Delay(TimeSpan.FromSeconds(1 / BroadcastsPerSecond));
                    await Server.BroadcastAsync(new BlankMessageTCP());
                    await Server.BroadcastAsync(new BlankMessageUDP(), Protocol.UDP);
                }
            });

            // Connect clients
            while (true) {
                TCPMaidClient Client = new();
                Client.OnConnect += (Connection) => {
                    Console.WriteLine("Connected!");
                };
                Client.OnReceive += (Message) => {
                    if (!Message.Internal) {
                        // Console.WriteLine(Message.GetType().Name);
                    }
                };
                Client.ConnectAsync("127.0.0.1", 12345).Wait();
            }
        }
        class BlankMessageTCP : Message { }
        class BlankMessageUDP : Message { }
    }
}
