namespace TCPMaid.Benchmark {
    internal class Program {
        static void Main() {
            // Initialise server
            ServerMaid Server = new(12345);
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
                    await Server.BroadcastAsync(new BlankMessage());
                }
            });

            // Connect clients
            while (true) {
                ClientMaid Client = new();
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
        class BlankMessage : Message { }
    }
}
