namespace TCPMaid.Benchmark {
    internal class Program {
        static void Main() {
            // Initialise server
            TCPMaidServer Server = new(12345);
            Server.OnConnect += (Connection) => {
                Console.WriteLine($"{Server.ClientCount} clients.");
            };
            Server.Start();

            // Broadcast messages
            Task.Run(async () => {
                while (true) {
                    const double BroadcastsPerSecond = 20;
                    await Task.Delay(TimeSpan.FromSeconds(1.0 / BroadcastsPerSecond));
                    await Server.BroadcastAsync(new BlankMessage());
                }
            });

            // Connect clients
            while (true) {
                TCPMaidClient Client = new();
                Client.OnConnect += (Connection) => {
                    Console.WriteLine("Connected!");
                };
                Client.ConnectAsync("localhost", 12345).Wait();
            }
        }
        class BlankMessage : Message { }
    }
}
