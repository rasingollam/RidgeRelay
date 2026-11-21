using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace RidgeRelay
{
    public sealed class PrivateRaceSession : MonoBehaviour
    {
        private const string SessionType = "ridge-relay-race-v6";
        private const string HelloMessage = "ridge-relay/v6/hello";
        private const string SeatsMessage = "ridge-relay/v6/seats";
        private const string InputMessage = "ridge-relay/v6/input";
        private const string StateMessage = "ridge-relay/v6/state";
        private const byte ProtocolVersion = 6;
        private const int SnapshotInterval = 3;
        private const int InputBytes = 2 * sizeof(int) + 1;
        private const int MaxQueuedInputs = 8;
        private const float HandshakeTimeout = 10;
        private const float ActiveStateTimeout = 8;

        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly RaceCommand[] commands = new RaceCommand[RaceSimulation.Entrants];
        private readonly RaceBot[] bots = new RaceBot[RaceSimulation.Entrants - 1];

        private ISession session;
        private NetworkManager manager;
        private CustomMessagingManager messaging;
        private RaceStatePacket previous = new RaceStatePacket();
        private RaceStatePacket latest = new RaceStatePacket();
        private RaceStatePacket incoming = new RaceStatePacket();
        private RaceCommand queuedHostInput;
        private float accumulator;
        private float latestSnapshotTime;
        private float nextHello;
        private float sessionOpened;
        private int match;
        private int sentInput;
        private int anticipatedLane;
        private int anticipatedLaneInput;
        private float anticipatedLaneDeadline;
        private int anticipatedJumpInput;
        private float anticipatedJumpTime;
        private bool suspended;
        private bool operationPending;

        public RaceSimulation DisplayRace { get; private set; }
        public int LocalSlot { get; private set; } = -1;
        public int FurColor { get; set; }
        public string JoinCode => session?.Code ?? "";
        public int HumanCount { get; private set; }
        public bool IsHost => session != null && manager != null && manager.IsHost;
        public bool Connected => session != null && manager != null && manager.IsConnectedClient && LocalSlot >= 0;
        public bool Busy => operationPending || (manager != null && manager.ShutdownInProgress);
        public string Error { get; private set; } = "";
        public bool CanStart => Connected && !Busy && IsHost &&
            manager.ConnectedClientsIds.Count == HumanCount &&
            (DisplayRace == null || DisplayRace.Phase == RacePhase.Finished);
        public bool CanControl => Connected && !Busy && DisplayRace != null &&
            DisplayRace.Phase == RacePhase.Running && DisplayRace.Runners[LocalSlot].FinishTime < 0;
        public event Action RaceStarted;

        public Task HostAsync() => ConnectAsync(null);

        public Task JoinAsync(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4 || code.Length > 12 || !IsAlphanumericCode(code))
            {
                Error = "Enter the private race code.";
                return Task.CompletedTask;
            }

            return ConnectAsync(code);
        }

        private static bool IsAlphanumericCode(string code)
        {
            foreach (char c in code)
                if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }

        private async Task ConnectAsync(string code)
        {
            if (Busy || session != null) return;
            operationPending = true;
            Error = "";
            try
            {
                CreateNetworkManager();
                await UnityServices.InitializeAsync(ServicesOptions());
                if (!AuthenticationService.Instance.IsAuthorized)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                if (this == null || suspended) return;

                if (code == null)
                {
                    var options = new SessionOptions
                    {
                        Type = SessionType,
                        Name = "Ridge Relay",
                        MaxPlayers = RaceSimulation.Entrants,
                        IsPrivate = true
                    };
                    session = await MultiplayerService.Instance.CreateSessionAsync(options.WithRelayNetwork());
                    if (this != null && !suspended)
                    {
                        LocalSlot = 0;
                        HumanCount = 1;
                    }
                }
                else
                {
                    session = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                        code, new JoinSessionOptions { Type = SessionType });
                }

                if (this == null || suspended)
                {
                    await CloseSessionAsync();
                    return;
                }

                sessionOpened = Time.realtimeSinceStartup;
                EnsureHandlers();
            }
            catch (Exception exception)
            {
                await CloseSessionAsync();
                Error = code == null
                    ? "Couldn’t host a private race. Check your connection and Unity services setup."
                    : "Couldn’t join that race. Check the code and try again.";
                Debug.LogWarning($"Ridge Relay private race: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                if (session == null) ResetLocal();
                operationPending = false;
            }
        }

        private static InitializationOptions ServicesOptions()
        {
            var options = new InitializationOptions();
            string[] args = Environment.GetCommandLineArgs();
            int profileFlag = Array.IndexOf(args, "-ridgeRelayProfile");
            if (profileFlag >= 0 && profileFlag + 1 < args.Length)
                options.SetProfile(args[profileFlag + 1]);
            return options;
        }

        private void CreateNetworkManager()
        {
            if (manager != null) return;
            if (NetworkManager.Singleton != null)
                throw new InvalidOperationException("Another NetworkManager is already active.");
            var root = new GameObject("Ridge Relay Network");
            root.SetActive(false);
            var transport = root.AddComponent<UnityTransport>();
            manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = true
            };
            manager.ConnectionApprovalCallback = ApproveConnection;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            root.SetActive(true);
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool active = DisplayRace != null && DisplayRace.Phase != RacePhase.Finished;
            response.Approved = !active && manager.ConnectedClientsIds.Count < RaceSimulation.Entrants;
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (!response.Approved) response.Reason = "The race is full or already running.";
        }

        private void EnsureHandlers()
        {
            if (manager == null) return;
            CustomMessagingManager current = manager.CustomMessagingManager;
            if (current == null || current == messaging) return;
            messaging = current;
            messaging.RegisterNamedMessageHandler(HelloMessage, ReceiveHello);
            messaging.RegisterNamedMessageHandler(SeatsMessage, ReceiveSeat);
            messaging.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            messaging.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        }

        public bool TryStartRace()
        {
            if (!CanStart) return false;

            ++match;
            accumulator = 0;
            queuedHostInput = default;
            foreach (Peer peer in peers.Values) peer.Reset();
            for (int i = 0; i < bots.Length; ++i) bots[i] = new RaceBot(167 + i * 37);
            DisplayRace = new RaceSimulation(waitForAll: true);
            DisplayRace.Runners[0].FurColor = Mathf.Clamp(FurColor, 0, RaceSimulation.FurColorCount - 1);
            foreach (Peer peer in peers.Values) DisplayRace.Runners[peer.Slot].FurColor = peer.FurColor;
            BroadcastState(NetworkDelivery.ReliableSequenced);
            RaceStarted?.Invoke();
            return true;
        }

        public void Advance(float seconds, RaceCommand input)
        {
            if (session == null || Busy) return;
            EnsureHandlers();
            if (!Connected)
            {
                SendHello();
                if (Time.realtimeSinceStartup - sessionOpened > HandshakeTimeout)
                    _ = DisconnectWithErrorAsync("Couldn’t connect to the private race.");
                return;
            }

            if (DisplayRace == null) return;
            if (IsHost)
            {
                AdvanceHost(Mathf.Min(0.1f, seconds), input);
                return;
            }

            float age = Time.realtimeSinceStartup - latestSnapshotTime;
            if (DisplayRace.Phase != RacePhase.Finished && age > ActiveStateTimeout)
            {
                _ = DisconnectWithErrorAsync("The host stopped responding.");
                return;
            }

            if (CanControl && HasInput(input)) SendInput(input);
            PresentClient(age, seconds);
        }

        private void AdvanceHost(float seconds, RaceCommand input)
        {
            if (DisplayRace.Phase == RacePhase.Finished) return;
            if (CanControl && HasInput(input))
            {
                queuedHostInput = new RaceCommand(
                    input.LaneDelta != 0 ? input.LaneDelta : queuedHostInput.LaneDelta,
                    input.Jump || queuedHostInput.Jump, input.UseItem || queuedHostInput.UseItem);
            }

            accumulator += seconds;
            while (accumulator >= RaceSimulation.StepSeconds && DisplayRace.Phase != RacePhase.Finished)
            {
                accumulator -= RaceSimulation.StepSeconds;
                commands[0] = queuedHostInput;
                queuedHostInput = default;
                for (int i = 1; i < commands.Length; ++i) commands[i] = bots[i - 1].Decide(DisplayRace, i);
                foreach (Peer peer in peers.Values) commands[peer.Slot] = peer.Consume();

                DisplayRace.Tick(commands);
                if (DisplayRace.Phase == RacePhase.Finished)
                    BroadcastState(NetworkDelivery.ReliableSequenced);
                else if (DisplayRace.SimulationTick % SnapshotInterval == 0)
                    BroadcastState(NetworkDelivery.UnreliableSequenced);
            }
        }

        private void PresentClient(float age, float seconds)
        {
            if (latest.Match == 0) return;
            float span = Mathf.Max(RaceSimulation.StepSeconds, (latest.Tick - previous.Tick) * RaceSimulation.StepSeconds);
            float blend = latest.Phase == RacePhase.Finished ? 1 : age / span;
            RunnerState local = DisplayRace.Runners[LocalSlot];
            float priorLateral = local.Lateral;
            latest.Present(DisplayRace, previous, blend);
            if (CanControl)
            {
                float now = Time.realtimeSinceStartup;
                if (latest.AcknowledgedInput < anticipatedLaneInput && now < anticipatedLaneDeadline &&
                    local.Recovery <= 0 && RaceSimulation.CanChangeLane(local.Distance, local.Lane, anticipatedLane))
                    local.Lateral = Mathf.MoveTowards(priorLateral, anticipatedLane, Mathf.Min(seconds, 0.05f) * 5);
                if (latest.AcknowledgedInput < anticipatedJumpInput && local.Recovery <= 0)
                {
                    float time = Mathf.Clamp(now - anticipatedJumpTime, 0, 0.87f);
                    local.Height = Mathf.Max(local.Height, 7.8f * time - 9 * time * time);
                }
            }
        }

        private void SendHello()
        {
            float now = Time.realtimeSinceStartup;
            if (IsHost || manager == null || !manager.IsConnectedClient || messaging == null || now < nextHello) return;
            nextHello = now + 0.7f;
            using var writer = new FastBufferWriter(2, Allocator.Temp);
            writer.WriteValueSafe(ProtocolVersion);
            writer.WriteValueSafe((byte)Mathf.Clamp(FurColor, 0, RaceSimulation.FurColorCount - 1));
            messaging.SendNamedMessage(HelloMessage, NetworkManager.ServerClientId, writer);
        }

        private void ReceiveHello(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || sender == NetworkManager.ServerClientId || !manager.ConnectedClients.ContainsKey(sender) ||
                !reader.TryBeginRead(2)) return;
            reader.ReadValue(out byte protocol);
            reader.ReadValue(out byte furColor);
            if (protocol != ProtocolVersion || furColor >= RaceSimulation.FurColorCount ||
                reader.Position != reader.Length) return;

            if (!peers.ContainsKey(sender))
            {
                if (DisplayRace != null && DisplayRace.Phase != RacePhase.Finished)
                {
                    manager.DisconnectClient(sender);
                    return;
                }

                int slot = FindFreeSlot();
                if (slot < 0)
                {
                    manager.DisconnectClient(sender);
                    return;
                }

                peers.Add(sender, new Peer(slot));
            }
            peers[sender].FurColor = furColor;

            SyncSeats();
        }

        private int FindFreeSlot()
        {
            for (int i = 1; i < RaceSimulation.Entrants; ++i)
            {
                bool taken = false;
                foreach (Peer peer in peers.Values)
                    if (peer.Slot == i) taken = true;
                if (!taken) return i;
            }

            return -1;
        }

        private void SyncSeats()
        {
            HumanCount = peers.Count + 1;
            foreach (var pair in peers)
            {
                using var writer = new FastBufferWriter(2, Allocator.Temp);
                writer.WriteValueSafe((byte)pair.Value.Slot);
                writer.WriteValueSafe((byte)HumanCount);
                messaging.SendNamedMessage(SeatsMessage, pair.Key, writer);
            }
        }

        private void ReceiveSeat(ulong sender, FastBufferReader reader)
        {
            if (IsHost || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(2)) return;
            reader.ReadValue(out byte slot);
            reader.ReadValue(out byte people);
            if (slot == 0 || slot >= RaceSimulation.Entrants || people < 2 ||
                people > RaceSimulation.Entrants || reader.Position != reader.Length) return;
            LocalSlot = slot;
            HumanCount = people;
        }

        private static bool HasInput(RaceCommand command) =>
            command.LaneDelta != 0 || command.Jump || command.UseItem;

        private void SendInput(RaceCommand input)
        {
            if (messaging == null) return;
            ++sentInput;
            byte flags = Encode(input);
            using var writer = new FastBufferWriter(InputBytes, Allocator.Temp);
            writer.WriteValueSafe(match);
            writer.WriteValueSafe(sentInput);
            writer.WriteValueSafe(flags);
            messaging.SendNamedMessage(InputMessage, NetworkManager.ServerClientId, writer);

            RunnerState runner = DisplayRace.Runners[LocalSlot];
            float now = Time.realtimeSinceStartup;
            if (input.LaneDelta != 0)
            {
                if (latest.AcknowledgedInput >= anticipatedLaneInput || now >= anticipatedLaneDeadline)
                    anticipatedLane = runner.Lane;
                int requestedLane = Mathf.Clamp(anticipatedLane + input.LaneDelta, -1, 1);
                if (RaceSimulation.CanChangeLane(runner.Distance, runner.Lane, requestedLane))
                {
                    anticipatedLane = requestedLane;
                    anticipatedLaneInput = sentInput;
                    anticipatedLaneDeadline = now + 0.35f;
                }
            }

            if (input.Jump && runner.Height <= 0.01f)
            {
                anticipatedJumpInput = sentInput;
                anticipatedJumpTime = now;
            }
        }

        private static byte Encode(RaceCommand input)
        {
            int bits = input.LaneDelta + 1;
            if (input.Jump) bits |= 4;
            if (input.UseItem) bits |= 8;
            return (byte)bits;
        }

        private void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (!IsHost || DisplayRace == null || DisplayRace.Phase != RacePhase.Running ||
                !peers.TryGetValue(sender, out Peer peer) || !reader.TryBeginRead(InputBytes)) return;
            reader.ReadValue(out int packetMatch);
            reader.ReadValue(out int input);
            reader.ReadValue(out byte flags);
            if (packetMatch != match || input <= peer.LastReceived || input - peer.LastReceived > 4096 ||
                flags > 15 || (flags & 3) == 3 || reader.Position != reader.Length) return;

            peer.LastReceived = input;
            if (peer.Queue.Count < MaxQueuedInputs)
                peer.Queue.Enqueue((input, new RaceCommand((flags & 3) - 1, (flags & 4) != 0, (flags & 8) != 0)));
        }

        private void BroadcastState(NetworkDelivery delivery)
        {
            foreach (var pair in peers)
            {
                using var writer = new FastBufferWriter(RaceStatePacket.Capacity, Allocator.Temp);
                RaceStatePacket.Write(writer, DisplayRace, match, pair.Value.LastApplied);
                messaging.SendNamedMessage(StateMessage, pair.Key, writer, delivery);
            }
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (IsHost || sender != NetworkManager.ServerClientId || LocalSlot < 0 || !incoming.TryRead(reader)) return;
            if (incoming.Match < match || (incoming.Match == match && incoming.Tick <= latest.Tick)) return;

            bool newRace = incoming.Match != match || DisplayRace == null;
            RaceStatePacket recycle = previous;
            previous = latest;
            latest = incoming;
            incoming = recycle;
            latestSnapshotTime = Time.realtimeSinceStartup;
            if (!newRace) return;

            match = latest.Match;
            sentInput = anticipatedLaneInput = anticipatedJumpInput = 0;
            DisplayRace = new RaceSimulation(waitForAll: true);
            latest.Present(DisplayRace, previous, 1);
            RaceStarted?.Invoke();
        }

        private void OnClientDisconnected(ulong client)
        {
            if (operationPending || session == null) return;
            if (IsHost && client != NetworkManager.ServerClientId)
            {
                if (peers.Remove(client)) SyncSeats();
                return;
            }

            if (manager == null || client == manager.LocalClientId || client == NetworkManager.ServerClientId)
                _ = DisconnectWithErrorAsync("Lost the connection to the private race.", waitForTransport: true);
        }

        private void OnApplicationPause(bool paused)
        {
            suspended = paused;
            if (paused && session != null)
                _ = DisconnectWithErrorAsync("The private race ended when the app was suspended.");
        }

        public Task LeaveAsync() => DisconnectWithErrorAsync("");

        private async Task DisconnectWithErrorAsync(string message, bool waitForTransport = false)
        {
            if (operationPending) return;
            operationPending = true;
            DisplayRace = null;
            Error = message;
            try
            {
                // NGO finishes its own disconnect after returning from the callback.
                // MPS LeaveAsync would otherwise request a second shutdown and retain stale sends.
                if (waitForTransport)
                {
                    await Awaitable.NextFrameAsync();
                    while (manager != null && manager.ShutdownInProgress)
                        await Awaitable.NextFrameAsync();
                }
                await CloseSessionAsync();
            }
            finally
            {
                ResetLocal();
                operationPending = false;
            }
        }

        private async Task CloseSessionAsync()
        {
            ISession old = session;
            session = null;
            try
            {
                if (old != null) await old.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Ridge Relay could not leave its session cleanly: {exception.Message}");
            }
            finally
            {
                if (manager != null && manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
                messaging = null;
            }
        }

        private void ResetLocal()
        {
            peers.Clear();
            LocalSlot = -1;
            HumanCount = 0;
            DisplayRace = null;
            match = 0;
            latest.Match = previous.Match = incoming.Match = 0;
            sentInput = anticipatedLaneInput = anticipatedJumpInput = 0;
            nextHello = 0;
        }

        private async void OnDestroy()
        {
            await CloseSessionAsync();
            if (manager != null) Destroy(manager.gameObject);
        }

        private sealed class Peer
        {
            public readonly int Slot;
            public int FurColor;
            public readonly Queue<(int sequence, RaceCommand command)> Queue =
                new Queue<(int, RaceCommand)>(MaxQueuedInputs);
            public int LastApplied;
            public int LastReceived;

            public Peer(int slot) => Slot = slot;

            public void Reset()
            {
                Queue.Clear();
                LastApplied = LastReceived = 0;
            }

            public RaceCommand Consume()
            {
                if (Queue.Count == 0) return default;
                var input = Queue.Dequeue();
                LastApplied = input.sequence;
                return input.command;
            }
        }
    }
}

