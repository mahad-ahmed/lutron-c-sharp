using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Home {
    public sealed class LutronClient {
        private static readonly Lazy<LutronClient> lazy = new Lazy<LutronClient>(() => new LutronClient());

        private const string HOST = "192.168.1.9";
        private const int PORT = 23;

        private readonly byte[] USERNAME = Encoding.UTF8.GetBytes("[REDACTED]\r\n");
        private readonly byte[] PASSWORD = Encoding.UTF8.GetBytes("[REDACTED]\r\n");

        private TcpClient client = null;
        private NetworkStream stream = null;

        public bool AutoRetry { get; set; } = true;

        private string lastFailedMessage = null;

        private readonly ConcurrentDictionary<int, double> levelsQueue = new ConcurrentDictionary<int, double>();
        private Thread levelsThread;

        private Thread listenerThread;
        private string buffer = "";
        private readonly Regex regex = new Regex("~OUTPUT,\\d+,1,\\d+\\.\\d\\d");

        private readonly List<IOnLevelChangeListener> onLevelChangeListeners = new List<IOnLevelChangeListener>();

        public IConnectionStateListener ConnectionStateListener { get; set; } = null;

        private LutronClient() { }

        public static LutronClient Instance() {
            return lazy.Value;
        }

        public void AddOnLevelChangeListener(IOnLevelChangeListener listener) {
            this.onLevelChangeListeners.Add(listener);
        }

        public void RemoveOnLevelChangeListener(IOnLevelChangeListener listener) {
            this.onLevelChangeListeners.Remove(listener);
        }

        public void Connect() {
            Disconnect();

            try {
                client = new TcpClient(HOST, PORT);
                stream = client.GetStream();

                listenerThread = new Thread(new ThreadStart(ListenerThread));
                listenerThread.Start();

                levelsThread = new Thread(new ThreadStart(LevelQueueThread));
                levelsThread.Start();
            }
            catch {
                throw;
            }
        }

        public void Disconnect() {
            if (levelsThread != null) {
                levelsThread.Interrupt();
            }

            if (listenerThread != null) {
                listenerThread.Interrupt();
            }

            if (stream != null) {
                try {
                    stream.Write(Encoding.UTF8.GetBytes("logout\r\n", 0, 8));
                }
                catch { }
                stream.Close();
            }

            if (client != null) {
                client.Close();
            }
        }

        public bool IsKindaConnected() {
            return (client != null && client.Connected && client.Client != null && client.Client.Connected);
        }

        private void LevelQueueThread() {
            try {
                while (true) {
                    Thread.Sleep(50);
                    foreach (KeyValuePair<int, double> entry in levelsQueue) {
                        int key = entry.Key;
                        levelsQueue.TryRemove(entry.Key, out double value);
                        SetLevel(key, value);
                    }
                }
            }
            catch (ThreadInterruptedException) { }
        }

        private void ListenerThread() {
            try {
                bool checkLogin = false;
                char c;
                while ((c = (char)stream.ReadByte()) != '\uFFFF') {
                    if (c != '\n') {
                        buffer += c;
                    }
                    if (c == ':') {
                        if (buffer.EndsWith("login:")) {
                            stream.Write(USERNAME);
                            stream.Flush();
                        }
                        else if (buffer.EndsWith("password:")) {
                            checkLogin = true;
                            stream.Write(PASSWORD);
                            stream.Flush();
                        }
                    }
                    else if (checkLogin) {
                        if (buffer.EndsWith("NET>")) {
                            if(ConnectionStateListener != null) {
                                ConnectionStateListener.OnConnected();
                            }
                            if (AutoRetry && lastFailedMessage != null) {
                                stream.Write(Encoding.UTF8.GetBytes(lastFailedMessage, 0, lastFailedMessage.Length));
                                stream.Flush();

                                lastFailedMessage = null;
                            }
                        }
                        else if (buffer.EndsWith("bad login")) {
                            //new Thread(()->connectionStateListener.onStateChanged(this, ConnectionStateListener.STATUS_BAD_LOGIN)).start();
                        }
                        else if (buffer.EndsWith("login attempts.")) {
                            //new Thread(()->connectionStateListener.onStateChanged(this, ConnectionStateListener.STATUS_TOO_MANY_ATTEMPTS)).start();
                        }
                        else {
                            continue;
                        }

                        checkLogin = false;
                    }
                    else if (c == '\n') {
                        string[] tmp = buffer.ToString().Split('\n');
                        string str = tmp[tmp.Length - 1];
                        foreach (Match match in regex.Matches(str)) {
                            string[] arr = match.Value.Substring(8).Split(',');
                            int integrationId;
                            double level;
                            if (arr.Length > 2 && int.TryParse(arr[0], out integrationId) && double.TryParse(arr[2], out level)) {
                                foreach (IOnLevelChangeListener listener in onLevelChangeListeners) {
                                    listener.OnLevelChange(integrationId, level);
                                }
                            }
                            buffer = "";
                        }
                    }
                }
            }
            catch (Exception) {
                //Debug.WriteLine("Aight, Imma head out..");
            }
        }



        private void SendMessage(String message) {
            try {
                message += "\r\n";
                stream.Write(Encoding.UTF8.GetBytes(message, 0, message.Length));
                stream.FlushAsync();
            }
            catch (Exception) {
                if (AutoRetry) {
                    lastFailedMessage = message;
                    Connect();
                }
            }
        }

        public void RequestLevel(int integrationId) {
            SendMessage("?OUTPUT," + integrationId + ",1");
        }

        public void SetLevel(int integrationId, double level) {
            if (level > 100 || level < 0) {
                return;
            }
            SendMessage("#OUTPUT," + integrationId + ",1," + level);
        }

        public void QueueLevel(int integrationId, double level) {
            if (level > 100 || level < 0) {
                return;
            }
            levelsQueue.AddOrUpdate(integrationId, level, (k, v) => level);
            //levelsQueue[integrationId] = level;
        }

        public void OpenCurtain(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",3");
        }

        public void StopCurtain(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",4");
        }

        public void CloseCurtain(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",2");
        }

        public void RaiseShade(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",2");
        }

        public void StopShade(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",4");
        }

        public void DropShade(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",3");
        }

        public void LedStop(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",1,0");
        }

        public void LedOn(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",1,100");
        }

        public void LedOff(int integrationId) {
            SendMessage("#OUTPUT," + integrationId + ",1,0");
        }




        public interface IOnLevelChangeListener {
            public void OnLevelChange(int integrationId, double level);
        }

        public interface IConnectionStateListener {
            public void OnConnected();
        }
    }
}
