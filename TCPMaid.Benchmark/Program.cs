using TCPMaid;
using MemoryPack;

// Initialise server
ServerMaid Server = new();
Server.OnConnect += (Channel) => {
    Console.WriteLine($"{Server.Clients.Count} clients.");
};
Server.OnDisconnect += (Channel, Reason, ByRemote) => {
    Console.WriteLine($"Disconnected: {Reason}, {ByRemote}");
};
Server.Start(12345);

// Broadcast messages
_ = Task.Run(async () => {
    while (true) {
        const double BroadcastsPerSecond = 10;
        await Task.Delay(TimeSpan.FromSeconds(1 / BroadcastsPerSecond));
        await Server.BroadcastAsync(new BlankMessage());
    }
});

// Connect clients
while (true) {
    ClientMaid Client = new();
    Client.OnConnect += (Channel) => {
        Console.WriteLine("Connected!");
    };
    Client.OnReceive += (Message) => {
        if (!Message.Internal) {
            //Console.WriteLine(Message.GetType().Name);
        }
    };
    await Client.ConnectAsync("127.0.0.1", 12345);
    //await Task.Delay(1000);
}

[MemoryPackable]
public partial class BlankMessage : Message { }