using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HandyBadgeRemote
{
    // Bluetooth LE link to the badge's picture channel (no login needed on this channel).
    // All events are raised on the UI thread via the captured SynchronizationContext.
    class BadgeLink
    {
        public static readonly Guid ServiceUuid = new Guid("7e400001-b5a3-f393-e0a9-e50e24dcca9d");
        public static readonly Guid WriteUuid = new Guid("7e400002-b5a3-f393-e0a9-e50e24dcca9d");
        public static readonly Guid NotifyUuid = new Guid("7e400003-b5a3-f393-e0a9-e50e24dcca9d");

        readonly SynchronizationContext ui;
        readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        readonly Parser parser;
        BluetoothLEDevice dev;
        GattSession session;
        GattDeviceService svc;
        GattCharacteristic wc, nc;
        TaskCompletionSource<Packet> pendingFaces;

        public bool Connected { get; private set; }
        public string Name { get; private set; }
        public ulong Address { get; private set; }
        public event Action Disconnected;

        public BadgeLink()
        {
            ui = SynchronizationContext.Current ?? new SynchronizationContext();
            parser = new Parser(OnPacket);
        }

        // Looks for an advertising badge by name prefix or by its service UUID. Returns 0 if none found.
        public async Task<ulong> ScanAsync(string namePrefix, int ms)
        {
            var found = new TaskCompletionSource<ulong>();
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += (w, a) =>
            {
                var name = a.Advertisement.LocalName ?? "";
                bool byName = namePrefix.Length > 0 && name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase);
                bool bySvc = a.Advertisement.ServiceUuids.Contains(ServiceUuid);
                if (byName || bySvc)
                {
                    Log.Write("Scan: found '" + name + "' at " + a.BluetoothAddress.ToString("X12") + (bySvc ? " (service match)" : ""));
                    found.TrySetResult(a.BluetoothAddress);
                }
            };
            try
            {
                watcher.Start();
                var done = await Task.WhenAny(found.Task, Task.Delay(ms));
                return done == found.Task ? found.Task.Result : 0;
            }
            finally { try { watcher.Stop(); } catch { } }
        }

        public async Task ConnectAsync(ulong address)
        {
            Close();
            Log.Write("Connecting to " + address.ToString("X12"));
            dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask();
            if (dev == null) throw new Exception("Badge not reachable");
            try
            {
                session = await GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId).AsTask();
                if (session != null) session.MaintainConnection = true;
            }
            catch (Exception e) { Log.Write("GattSession unavailable: " + e.Message); }

            var sr = await dev.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask();
            if (sr.Status != GattCommunicationStatus.Success || sr.Services.Count == 0)
                throw new Exception("Badge service not found (" + sr.Status + ")");
            svc = sr.Services[0];

            var cr = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask();
            if (cr.Status != GattCommunicationStatus.Success) throw new Exception("Couldn't read characteristics (" + cr.Status + ")");
            wc = cr.Characteristics.FirstOrDefault(c => c.Uuid == WriteUuid);
            nc = cr.Characteristics.FirstOrDefault(c => c.Uuid == NotifyUuid);
            if (wc == null || nc == null) throw new Exception("Badge characteristics missing");

            nc.ValueChanged += OnValueChanged;
            var cccd = (nc.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            var st = await nc.WriteClientCharacteristicConfigurationDescriptorAsync(cccd).AsTask();
            if (st != GattCommunicationStatus.Success) throw new Exception("Couldn't subscribe to the badge (" + st + ")");

            dev.ConnectionStatusChanged += OnConnectionStatusChanged;
            Address = address;
            Name = string.IsNullOrEmpty(dev.Name) ? "Badge" : dev.Name;
            Connected = true;
            Log.Write("Connected to " + Name);
        }

        void OnConnectionStatusChanged(BluetoothLEDevice d, object args)
        {
            if (d.ConnectionStatus != BluetoothConnectionStatus.Disconnected) return;
            ui.Post(_ =>
            {
                if (!Connected) return;
                Log.Write("Badge disconnected");
                Connected = false;
                var h = Disconnected; if (h != null) h();
            }, null);
        }

        void OnValueChanged(GattCharacteristic c, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            ui.Post(_ => { Log.Write("<- " + Log.Hex(bytes)); parser.Feed(bytes); }, null);
        }

        void OnPacket(Packet p)
        {
            if (p.IsCd)
            {
                WriteAsync(Proto.AckFor(p), "ack").ContinueWith(t => Log.Write("Ack failed: " + t.Exception.GetBaseException().Message), TaskContinuationOptions.OnlyOnFaulted);
            }
            if (p.IsCd && p.Module == Proto.ModFaceReply && p.Cmd == Proto.CmdFaces && pendingFaces != null)
                pendingFaces.TrySetResult(p);
        }

        public async Task WriteAsync(byte[] data, string label)
        {
            await writeLock.WaitAsync();
            try
            {
                if (wc == null) throw new InvalidOperationException("Not connected");
                Log.Write("-> " + Log.Hex(data) + "  " + label);
                var opt = (wc.CharacteristicProperties & GattCharacteristicProperties.Write) != 0
                    ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
                for (int off = 0; off < data.Length; off += 20)
                {
                    int n = Math.Min(20, data.Length - off);
                    var part = new byte[n];
                    Array.Copy(data, off, part, 0, n);
                    var w = new DataWriter();
                    w.WriteBytes(part);
                    var st = await wc.WriteValueAsync(w.DetachBuffer(), opt).AsTask();
                    if (st != GattCommunicationStatus.Success) throw new Exception("Write failed (" + st + ")");
                }
            }
            finally { writeLock.Release(); }
        }

        // Asks for the stored faces and returns the active image's footprint. Throws TimeoutException if the badge doesn't answer.
        public async Task<int> ReadActiveFootprintAsync(int timeoutMs)
        {
            pendingFaces = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = pendingFaces.Task;
            await WriteAsync(Proto.Make(Proto.ModFaceReply, Proto.CmdFaces), "read stored faces");
            var done = await Task.WhenAny(waiter, Task.Delay(timeoutMs));
            pendingFaces = null;
            if (done != waiter) throw new TimeoutException("No reply from the badge");
            return Proto.ActiveFootprint(waiter.Result.Payload);
        }

        public void Close()
        {
            Connected = false;
            try { if (nc != null) nc.ValueChanged -= OnValueChanged; } catch { }
            try { if (dev != null) dev.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
            try { if (svc != null) svc.Dispose(); } catch { }
            try { if (session != null) session.Dispose(); } catch { }
            try { if (dev != null) dev.Dispose(); } catch { }
            svc = null; session = null; dev = null; wc = null; nc = null;
        }
    }
}
