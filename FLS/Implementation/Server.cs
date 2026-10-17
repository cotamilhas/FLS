using FLS.Models.Config;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FLS.Implementation
{
    public class Server
    {
        private readonly TcpListener Listener;
        private readonly List<Client> Clients;
        public readonly Guid ServerID;
        public readonly ServerConfig Config;
        private readonly ServerCommunication ServerComm;
        public readonly SessionManager SessionManager;
        public CancellationTokenSource StopToken;

        public Server(string configPath)
        {
            Config = ServerConfig.GetFromFile(configPath);
            ServerID = Guid.NewGuid();
            Clients = [];
            SessionManager = new();
            ServerComm = new(this);
            Listener = new(new IPEndPoint(IPAddress.Any, Config.Port));
            StopToken = new();
        }

        public void Start()
        {
            if (StopToken.IsCancellationRequested)
                throw new InvalidOperationException();
            
            Listener.Start();
            ServerComm.Start();
            
            Task.Run(async () =>
            {
                double step = 1.0 / 5;
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(step));
                while (await timer.WaitForNextTickAsync(StopToken.Token))
                {
                    try
                    {
                        Tick();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"There was an error while executing tick: {e}");
                    }
                }
            }, StopToken.Token);
            Log.Information($"Started listening on {Listener.LocalEndpoint}");
        }

        public void Stop()
        {
            StopToken.Cancel();
            
            foreach (var client in Clients)
                client.Disconnect();

            Listener.Stop();
            ServerComm.Stop();
        }

        private void CheckClients()
        {
            if (Listener.Pending())
            {
                var client = Listener.AcceptTcpClient();
                Clients.Add(new Client(client, this));
            }
        }

        private void CheckServerComm()
        {
            if (!ServerComm.IsConnected)
                ServerComm.Start();
        }

        private void Tick()
        {
            CheckClients();
            CheckServerComm();
        }

        public void SendToPlayer(string message, int playerId)
        {
            var client = Clients.FirstOrDefault(match => match.PlayerID == playerId);
            client?.Send(message);
        }
    }
}
