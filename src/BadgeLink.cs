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
        int chunk = 20;           // GATT write size; raised to the negotiated maximum on connect
        bool stopping;

        public bool Previewing { get; private set; }
        public event Action<int> ControlEvent;   // on-screen control (module 0x1C), e.g. 2 = shutter
        public event Action PreviewEnded;        // the badge left live mode by itself

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

            chunk = 20;
            try { if (session != null && session.MaxPduSize > 23) chunk = Math.Min(244, session.MaxPduSize - 3); } catch { }
            Log.Write("Write size " + chunk + " bytes");
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
            if (p.Module == 0x1c)
            {
                Log.Write("Badge control: action " + p.Cmd + (p.Cmd == 2 ? " (shutter)" : ""));
                var h = ControlEvent; if (h != null && p.IsCd) h(p.Cmd);
            }
            if (p.IsCd && p.Module == 0x27 && p.Cmd == 0x01 && Previewing && !stopping)
            {
                Log.Write("Badge left live mode");
                Previewing = false;
                var h = PreviewEnded; if (h != null) h();
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
                if (data.Length > 64) Log.Write("-> " + Log.Hex(Proto.Head(data, 16)) + " … (" + data.Length + " bytes)  " + label);
                else Log.Write("-> " + Log.Hex(data) + "  " + label);
                var opt = (wc.CharacteristicProperties & GattCharacteristicProperties.Write) != 0
                    ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
                int off = 0;
                while (off < data.Length)
                {
                    int n = Math.Min(chunk, data.Length - off);
                    var part = new byte[n];
                    Array.Copy(data, off, part, 0, n);
                    var w = new DataWriter();
                    w.WriteBytes(part);
                    GattCommunicationStatus st;
                    try { st = await wc.WriteValueAsync(w.DetachBuffer(), opt).AsTask(); }
                    catch (Exception e) { if (chunk > 20) { StepDown(e.Message); continue; } throw; }
                    if (st != GattCommunicationStatus.Success)
                    {
                        if (chunk > 20) { StepDown(st.ToString()); continue; }
                        throw new Exception("Write failed (" + st + ")");
                    }
                    off += n;
                }
            }
            finally { writeLock.Release(); }
        }

        void StepDown(string why)
        {
            int next = chunk > 180 ? 180 : chunk > 128 ? 128 : 20;
            Log.Write("Write of " + chunk + " bytes refused (" + why + "), trying " + next);
            chunk = next;
        }

        // ---- Live screen ("real-time preview", module 0x27), as HandyBadge ----
        public async Task StartPreviewAsync()
        {
            stopping = false;
            await WriteAsync(new byte[] { 0xcd, 0, 6, 0x12, 1, 0x0c, 0, 1, 1 }, "enable preview mode");
            await WriteAsync(new byte[] { 0xcd, 0, 5, 0x27, 1, 0x00, 0x00, 0, 0 }, "preview start");
            Previewing = true;
        }

        public static byte[] WrapFrame(byte[] jpeg)
        {
            int L = jpeg.Length;
            var o = new byte[6 + 4 + L + 6];
            new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }.CopyTo(o, 0);
            o[6] = (byte)(L >> 24); o[7] = (byte)(L >> 16); o[8] = (byte)(L >> 8); o[9] = (byte)L;
            jpeg.CopyTo(o, 10);
            new byte[] { 0xff, 0xee, 0xdd, 0xcc, 0xbb, 0xaa }.CopyTo(o, 10 + L);
            return o;
        }

        public Task SendFrameAsync(byte[] jpeg)
        {
            if (!Previewing) return Task.FromResult(0);
            return WriteAsync(WrapFrame(jpeg), "preview frame (" + jpeg.Length + " B)");
        }

        public async Task StopPreviewAsync()
        {
            if (!Previewing) return;
            stopping = true;
            Previewing = false;
            try { await WriteAsync(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0, 0, 0, 0, 0xff, 0xee, 0xdd, 0xcc, 0xbb, 0xaa }, "preview end frame"); } catch { }
            try { await WriteAsync(new byte[] { 0xcd, 0, 6, 0x27, 1, 0x01, 0, 1, 0 }, "preview stop"); } catch { }
            try { await WriteAsync(new byte[] { 0xcd, 0, 6, 0x12, 1, 0x0c, 0, 1, 0 }, "disable preview mode"); } catch { }
            await Task.Delay(300);
            stopping = false;
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
            Connected = false; Previewing = false;
            try { if (nc != null) nc.ValueChanged -= OnValueChanged; } catch { }
            try { if (dev != null) dev.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
            try { if (svc != null) svc.Dispose(); } catch { }
            try { if (session != null) session.Dispose(); } catch { }
            try { if (dev != null) dev.Dispose(); } catch { }
            svc = null; session = null; dev = null; wc = null; nc = null;
        }
    }
}
