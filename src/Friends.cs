using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Mascot
{
    public sealed class FriendBook
    {
        public string relay;
        public string pin;
        public string token;
        public string friend;
        public string[] friends;
    }

    public sealed class FriendNote
    {
        public string from;
        public string text;
    }

    public sealed class FriendWire
    {
        public bool ok;
        public string error;
        public string pin;
        public string token;
        public string to;
        public string from;
        public string text;
        public FriendNote[] messages;
    }

    // ponytail: pin is the address. Anyone who knows it can send. Add an accept list if strangers show up.
    static class Friends
    {
        const long TtlMs = 15000;
        const int MaxNotes = 20;
        const int MaxText = 2000;

        sealed class Slot
        {
            public string Pin;
            public string Token;
            public long Seen;
            public List<FriendNote> Notes = new List<FriendNote>();
        }

        sealed class Hub
        {
            readonly Dictionary<string, Slot> _map = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
            readonly object _gate = new object();

            public FriendWire Inbox(string pin, string token, long now)
            {
                lock (_gate)
                    return InboxCore(pin, token, now);
            }

            public FriendWire Send(string token, string to, string text, long now)
            {
                return Send(token, to, text, now, null);
            }

            public FriendWire Send(string token, string to, string text, long now, string fromPin)
            {
                lock (_gate)
                    return SendCore(token, to, text, now, fromPin);
            }

            FriendWire InboxCore(string pin, string token, long now)
            {
                pin = CleanPin(pin);
                if (pin.Length == 0 || string.IsNullOrEmpty(token))
                    return Fail("Bad username");
                Slot slot;
                bool has = _map.TryGetValue(pin, out slot);
                bool fresh = has && now - slot.Seen <= TtlMs;
                if (has && fresh && slot.Token != token)
                    return Fail("Username taken");
                if (!has || slot.Token != token)
                {
                    slot = new Slot();
                    slot.Pin = pin;
                    slot.Token = token;
                    _map[pin] = slot;
                }
                slot.Seen = now;
                FriendWire wire = new FriendWire();
                wire.ok = true;
                wire.messages = slot.Notes.ToArray();
                slot.Notes.Clear();
                return wire;
            }

            FriendWire SendCore(string token, string to, string text, long now, string fromPin)
            {
                to = CleanPin(to);
                if (to.Length == 0)
                    return Fail("Bad username");
                text = text == null ? "" : text.Trim();
                if (text.Length == 0)
                    return Fail("Empty message");
                if (text.Length > MaxText)
                    return Fail("Message too long");
                string from = null;
                if (!string.IsNullOrEmpty(token))
                {
                    foreach (Slot slot in _map.Values)
                    {
                        if (slot.Token == token && now - slot.Seen <= TtlMs)
                        {
                            from = slot.Pin;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(from))
                    from = CleanPin(fromPin);
                if (string.IsNullOrEmpty(from))
                    return Fail("Not signed in");
                Slot dest;
                if (!_map.TryGetValue(to, out dest) || now - dest.Seen > TtlMs)
                    return Fail("Friend offline");
                if (dest.Notes.Count >= MaxNotes)
                    dest.Notes.RemoveAt(0);
                FriendNote note = new FriendNote();
                note.from = from;
                note.text = text;
                dest.Notes.Add(note);
                FriendWire wire = new FriendWire();
                wire.ok = true;
                return wire;
            }

            static FriendWire Fail(string error)
            {
                FriendWire wire = new FriendWire();
                wire.ok = false;
                wire.error = error;
                return wire;
            }
        }

        public static int SelfCheck()
        {
            Hub hub = new Hub();
            if (hub.Inbox("ab", "token-aaaa", 0).ok)
                return Fail("short pin");
            FriendWire claim = hub.Inbox("blue", "token-aaaa", 1000);
            if (!claim.ok)
                return Fail("claim");
            if (hub.Inbox("blue", "token-bbbb", 2000).ok)
                return Fail("taken");
            FriendWire sent = hub.Send("token-aaaa", "blue", "hello", 3000);
            if (!sent.ok)
                return Fail("send self");
            hub.Inbox("green", "token-cccc", 3000);
            if (!hub.Send("token-cccc", "blue", "yo", 4000).ok)
                return Fail("send");
            FriendWire box = hub.Inbox("blue", "token-aaaa", 5000);
            if (!box.ok || box.messages == null || box.messages.Length != 2 || box.messages[1].text != "yo" || box.messages[1].from != "green")
                return Fail("inbox");
            box = hub.Inbox("blue", "token-aaaa", 6000);
            if (!box.ok || box.messages.Length != 0)
                return Fail("drain");
            if (hub.Send("token-cccc", "blue", "later", 6000 + TtlMs + 1).ok)
                return Fail("stale");
            if (!hub.Inbox("blue", "token-bbbb", 6000 + TtlMs + 1).ok)
                return Fail("takeover");
            if (hub.Send("nope", "blue", "x", 6000 + TtlMs + 2).ok)
                return Fail("stranger");
            if (!hub.Send(null, "blue", "direct", 6000 + TtlMs + 3, "green").ok)
                return Fail("direct");
            FriendWire hopped = hub.Inbox("blue", "token-bbbb", 6000 + TtlMs + 4);
            if (!hopped.ok || hopped.messages == null || hopped.messages.Length != 1 || hopped.messages[0].text != "direct" || hopped.messages[0].from != "green")
                return Fail("direct inbox");
            string kind;
            string pin;
            string from;
            string to;
            string text;
            if (!ReadPacket("P\nblue\n", out kind, out pin, out from, out to, out text) || kind != "P" || pin != "blue")
                return Fail("packet p");
            if (!ReadPacket("M\ngreen\nblue\nhello there", out kind, out pin, out from, out to, out text) || kind != "M" || from != "green" || to != "blue" || text != "hello there")
                return Fail("packet m");
            if (!ReadPacket("7041", out kind, out pin, out from, out to, out text) || kind != "P" || pin != "7041")
                return Fail("packet raw");
            if (SafeName("..\\..\\notes.txt") != "notes.txt" || SafeName("hi there!.txt") != "hi there.txt")
                return Fail("name");
            if (CleanPin("Bob_1") != "bob_1" || CleanPin("ab") != "" || CleanPin("hey") != "hey")
                return Fail("user");
            string[] pals = Normalize(new string[] { "7041", "no", "7041" }, "abcd");
            if (pals.Length != 2 || pals[0] != "7041" || pals[1] != "abcd")
                return Fail("pals");
            if (ChunkCount(0) != 1 || ChunkCount(ChunkBytes) != 1 || ChunkCount(ChunkBytes + 1) != 2)
                return Fail("chunks");
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("yo"));
            string fromPin;
            string toPin;
            string name;
            int index;
            int total;
            string body;
            if (!TryFile(FilePacket("green", "blue", "hi.txt", 0, 1, b64), out fromPin, out toPin, out name, out index, out total, out body) || fromPin != "green" || toPin != "blue" || name != "hi.txt" || body != b64)
                return Fail("file packet");
            if (TryFile(FilePacket("green", "blue", "hi.txt", 0, ChunkCount(MaxFile) + 1, b64), out fromPin, out toPin, out name, out index, out total, out body))
                return Fail("file cap");
            if (!AckMatches(AckPacket("blue", "green", "hi.txt", 0), "green", "hi.txt", 0))
                return Fail("ack");
            if (AckMatches(AckPacket("blue", "green", "hi.txt", 1), "green", "hi.txt", 0))
                return Fail("ack bad");
            byte[] joined = JoinFile(new string[] { Convert.ToBase64String(Encoding.UTF8.GetBytes("hello ")), Convert.ToBase64String(Encoding.UTF8.GetBytes("file")) });
            if (joined == null || Encoding.UTF8.GetString(joined) != "hello file")
                return Fail("join");
            return 0;
        }

        static int Fail(string why)
        {
            Console.Error.WriteLine(why);
            return 50;
        }

        static FriendBook _book;
        static Hub _live;
        static volatile bool _run;
        static Thread _poll;
        static string _lastErr = "";
        static Form _chat;
        static Form _send;
        static TextBox _sendBox;
        static PetForm _pet;
        static UdpClient _hear;
        static readonly object _udpLock = new object();
        static readonly Dictionary<string, PeerHit> _peers = new Dictionary<string, PeerHit>(StringComparer.OrdinalIgnoreCase);

        sealed class PeerHit
        {
            public IPEndPoint End;
            public long Seen;
            public long Replied;
        }

        public static int Serve(string[] args)
        {
            int port = 8787;
            if (args.Length > 1)
                int.TryParse(args[1], out port);
            if (port < 1 || port > 65535)
                port = 8787;
            _live = new Hub();
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                Log("relay bind failed " + ex.Message);
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            Log("relay listening 0.0.0.0:" + port);
            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(delegate(object state) { ServeClient((TcpClient)state); }, client);
            }
        }

        static void ServeClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 4000;
                client.SendTimeout = 4000;
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    MemoryStream buf = new MemoryStream();
                    byte[] chunk = new byte[1024];
                    int headerEnd = -1;
                    while (headerEnd < 0 && buf.Length < 8192)
                    {
                        int n = stream.Read(chunk, 0, chunk.Length);
                        if (n <= 0)
                            return;
                        buf.Write(chunk, 0, n);
                        headerEnd = Find(buf, "\r\n\r\n");
                    }
                    if (headerEnd < 0)
                        return;
                    string head = Encoding.ASCII.GetString(buf.GetBuffer(), 0, headerEnd);
                    string[] lines = head.Split('\n');
                    string[] req = lines[0].Trim().Split(' ');
                    string method = req.Length > 0 ? req[0] : "";
                    string path = req.Length > 1 ? req[1] : "/";
                    int q = path.IndexOf('?');
                    if (q >= 0)
                        path = path.Substring(0, q);
                    int length = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(), out length);
                    }
                    if (length < 0 || length > 8192)
                        length = 0;
                    int have = (int)buf.Length - (headerEnd + 4);
                    while (have < length)
                    {
                        int n = stream.Read(chunk, 0, chunk.Length);
                        if (n <= 0)
                            break;
                        buf.Write(chunk, 0, n);
                        have += n;
                    }
                    string body = "";
                    if (length > 0 && have > 0)
                        body = Encoding.UTF8.GetString(buf.GetBuffer(), headerEnd + 4, Math.Min(length, have));
                    byte[] payload = Encoding.UTF8.GetBytes(Route(method, path, body));
                    string resp = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] header = Encoding.ASCII.GetBytes(resp);
                    stream.Write(header, 0, header.Length);
                    stream.Write(payload, 0, payload.Length);
                }
            }
            catch
            {
            }
        }

        static int Find(MemoryStream buf, string needle)
        {
            byte[] data = buf.GetBuffer();
            int len = (int)buf.Length;
            byte[] mark = Encoding.ASCII.GetBytes(needle);
            for (int i = 0; i + mark.Length <= len; i++)
            {
                bool ok = true;
                for (int j = 0; j < mark.Length; j++)
                {
                    if (data[i + j] != mark[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return i;
            }
            return -1;
        }

        static string Route(string method, string path, string body)
        {
            if (method == "GET")
                return "{\"ok\":true}";
            FriendWire wire = null;
            try
            {
                wire = new JavaScriptSerializer().Deserialize<FriendWire>(body);
            }
            catch
            {
            }
            if (wire == null)
                wire = new FriendWire();
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            FriendWire res;
            if (path == "/inbox")
                res = _live.Inbox(wire.pin, wire.token, now);
            else if (path == "/send")
                res = _live.Send(wire.token, wire.to, wire.text, now, wire.from);
            else
            {
                res = new FriendWire();
                res.ok = false;
                res.error = "unknown";
            }
            return new JavaScriptSerializer().Serialize(res);
        }

        public static void Start(PetForm pet)
        {
            _pet = pet;
            if (_run)
                return;
            _book = Load();
            if (_book == null)
                return;
            EnsureRelay();
            _run = true;
            Hear();
            Shout();
            AllowLan();
            Log("started user=" + _book.pin + " friend=" + (_book.friend ?? "") + " relay=" + _book.relay + " ips=" + LocalIps());
            _poll = new Thread(Poll);
            _poll.IsBackground = true;
            _poll.Start();
        }

        public static void Stop()
        {
            _run = false;
        }

        public static void Open(PetForm pet)
        {
            Start(pet);
            if (!_run || _book == null)
                return;
            if (_chat != null && !_chat.IsDisposed)
            {
                _chat.Activate();
                return;
            }
            _book.friends = Normalize(_book.friends, _book.friend);
            FriendSheet form = new FriendSheet("FRIENDS");
            int x = form.PadL;
            int y = form.HeadBottom;
            int inner = 296;
            int row = form.BoxH;
            Label mine = form.Lab("YOUR USERNAME");
            mine.SetBounds(x, y, inner, form.LabelH);
            y += form.LabelH;
            TextBox mineBox = form.Field();
            mineBox.Text = _book.pin ?? "";
            RoundButton rename = form.Chip("RENAME", true);
            mineBox.SetBounds(x + 8, y + 6, inner - 100, row - 10);
            rename.SetBounds(x + inner - 84, y, 84, row);
            form.Track(mineBox);
            y += row + 12;
            Label friendsLabel = form.Lab("FRIENDS");
            friendsLabel.SetBounds(x, y, inner, form.LabelH);
            y += form.LabelH;
            ListBox list = form.Names();
            list.SetBounds(x + 6, y + 4, inner - 12, 108);
            form.Track(list);
            FillFriends(list);
            y += 116;
            Label palLabel = form.Lab("FRIEND USERNAME");
            palLabel.SetBounds(x, y, inner, form.LabelH);
            y += form.LabelH;
            TextBox pal = form.Field();
            RoundButton connect = form.Chip("ADD", true);
            pal.SetBounds(x + 8, y + 6, inner - 100, row - 10);
            connect.SetBounds(x + inner - 84, y, 84, row);
            form.Track(pal);
            y += row + 12;
            RoundButton remove = form.Chip("REMOVE", false);
            remove.SetBounds(x, y, 100, row);
            y += row + 8;
            Label status = form.Lab(TargetLine());
            status.SetBounds(x, y, inner, form.LabelH * 2);
            y += form.LabelH * 2;

            list.SelectedIndexChanged += delegate
            {
                if (list.SelectedItem == null)
                    return;
                _book.friend = (string)list.SelectedItem;
                Save(_book);
                status.Text = TargetLine();
                status.ForeColor = form.Mute;
            };
            connect.Click += delegate
            {
                string to = CleanPin(pal.Text);
                if (to.Length == 0 || to == CleanPin(_book.pin))
                {
                    status.Text = NameRule;
                    status.ForeColor = form.Alert;
                    return;
                }
                _book.friends = Normalize(_book.friends, to);
                _book.friend = to;
                Save(_book);
                pal.Text = "";
                FillFriends(list);
                status.Text = TargetLine();
                status.ForeColor = form.Mute;
                if (_pet != null)
                    _pet.ShowLine("Sending to " + to + ". Ctrl+M to message.");
            };
            remove.Click += delegate
            {
                if (list.SelectedItem == null)
                    return;
                string gone = (string)list.SelectedItem;
                List<string> keep = new List<string>();
                for (int i = 0; i < _book.friends.Length; i++)
                {
                    if (_book.friends[i] != gone)
                        keep.Add(_book.friends[i]);
                }
                _book.friends = keep.ToArray();
                if (_book.friend == gone)
                    _book.friend = keep.Count > 0 ? keep[0] : "";
                Save(_book);
                FillFriends(list);
                status.Text = TargetLine();
                status.ForeColor = form.Mute;
            };

            rename.Click += delegate
            {
                string name = CleanPin(mineBox.Text);
                if (name.Length == 0)
                {
                    status.Text = NameRule;
                    status.ForeColor = form.Alert;
                    return;
                }
                if (NameInUse(name))
                {
                    status.Text = "Username taken";
                    status.ForeColor = form.Alert;
                    return;
                }
                _book.pin = name;
                Save(_book);
                mineBox.Text = name;
                status.Text = "Username saved";
                status.ForeColor = form.Mute;
            };
            form.Controls.Add(mine);
            form.Controls.Add(mineBox);
            form.Controls.Add(rename);
            form.Controls.Add(friendsLabel);
            form.Controls.Add(list);
            form.Controls.Add(palLabel);
            form.Controls.Add(pal);
            form.Controls.Add(connect);
            form.Controls.Add(remove);
            form.Controls.Add(status);
            form.AcceptButton = connect;
            form.Fit(y + form.PadB);
            form.Shown += delegate { pal.Focus(); };
            form.FormClosed += delegate { _chat = null; };
            _chat = form;
            form.Show();
        }

        public static void Prompt(PetForm pet)
        {
            Start(pet);
            if (!_run || _book == null)
                return;
            _book.friends = Normalize(_book.friends, _book.friend);
            if (_book.friends.Length == 0)
            {
                Open(pet);
                return;
            }
            if (_send != null && !_send.IsDisposed)
            {
                TakeFocus(_send, _sendBox);
                return;
            }
            FriendSheet form = new FriendSheet("MESSAGE");
            int x = form.PadL;
            int y = form.HeadBottom;
            int inner = 296;
            Label toLabel = form.Lab("TO");
            toLabel.SetBounds(x, y, inner, form.LabelH);
            y += form.LabelH;
            ComboBox who = new ComboBox();
            who.DropDownStyle = ComboBoxStyle.DropDownList;
            who.FlatStyle = FlatStyle.Flat;
            who.BackColor = form.BackColor;
            who.ForeColor = form.ForeColor;
            who.Font = form.Font;
            who.SetBounds(x + 6, y + 4, inner - 12, form.BoxH);
            form.Track(who);
            for (int i = 0; i < _book.friends.Length; i++)
                who.Items.Add(_book.friends[i]);
            int pick = _book.friend == null ? -1 : who.Items.IndexOf(_book.friend);
            who.SelectedIndex = pick < 0 ? 0 : pick;
            y += form.BoxH + 12;
            Label msgLabel = form.Lab("MESSAGE");
            msgLabel.SetBounds(x, y, inner, form.LabelH);
            y += form.LabelH;
            TextBox msg = form.Field();
            msg.SetBounds(x + 8, y + 6, inner - 16, form.BoxH - 10);
            form.Track(msg);
            y += form.BoxH + 12;
            RoundButton send = form.Chip("SEND", true);
            send.SetBounds(x, y, 84, form.BoxH);
            Label status = form.Lab("");
            status.SetBounds(x + 96, y, inner - 96, form.BoxH);
            y += form.BoxH;
            send.Click += delegate
            {
                string to = who.SelectedItem as string;
                to = CleanPin(to);
                if (to.Length == 0)
                {
                    status.Text = "Pick a friend";
                    status.ForeColor = form.Alert;
                    return;
                }
                _book.friend = to;
                Save(_book);
                Push(form, send, status, msg, to);
            };
            form.Controls.Add(toLabel);
            form.Controls.Add(who);
            who.SelectedIndexChanged += delegate
            {
                string to = CleanPin(who.SelectedItem as string);
                if (to.Length == 0)
                    return;
                _book.friend = to;
                Save(_book);
            };
            form.Controls.Add(msgLabel);
            form.Controls.Add(msg);
            form.Controls.Add(send);
            form.Controls.Add(status);
            form.AcceptButton = send;
            form.Fit(y + form.PadB);
            form.Shown += delegate { TakeFocus(form, msg); };
            form.FormClosed += delegate { _send = null; _sendBox = null; };
            _send = form;
            _sendBox = msg;
            form.Show();
            TakeFocus(form, msg);
        }

        static void Push(Form form, Button send, Label status, TextBox msg, string to)
        {
            FriendSheet sheet = form as FriendSheet;
            string text = msg.Text == null ? "" : msg.Text.Trim();
            if (text.Length == 0)
            {
                status.Text = "Type a message";
                if (sheet != null)
                    status.ForeColor = sheet.Alert;
                return;
            }
            if (text.Length > MaxText)
            {
                status.Text = "Message too long";
                if (sheet != null)
                    status.ForeColor = sheet.Alert;
                return;
            }
            send.Enabled = false;
            status.Text = "Sending…";
            if (sheet != null)
                status.ForeColor = sheet.Mute;
            ThreadPool.QueueUserWorkItem(delegate
            {
                FriendWire res = SendText(to, text);
                string line = res == null ? "Relay unreachable" : (res.ok ? "Sent" : (res.error ?? "Failed"));
                if (form.IsDisposed)
                    return;
                form.BeginInvoke(new Action(delegate
                {
                    send.Enabled = true;
                    status.Text = line;
                    if (sheet != null)
                        status.ForeColor = (res != null && res.ok) ? sheet.Mute : sheet.Alert;
                    if (res != null && res.ok)
                    {
                        msg.Text = "";
                        TakeFocus(form, msg);
                    }
                }));
            });
        }

        static void Poll()
        {
            while (_run)
            {
                Shout();
                FriendWire res = Call("/inbox", new FriendWire { pin = _book.pin, token = _book.token });
                if (!_run || _pet == null || _pet.IsDisposed)
                    break;
                if (res == null)
                {
                    _lastErr = "";
                }
                else if (!res.ok)
                {
                    string err = res.error ?? "Failed";
                    if (err != _lastErr)
                    {
                        _lastErr = err;
                        PostLine(err);
                    }
                }
                else
                {
                    _lastErr = "";
                    if (res.messages != null)
                    {
                        for (int i = 0; i < res.messages.Length; i++)
                        {
                            FriendNote note = res.messages[i];
                            if (note == null || string.IsNullOrEmpty(note.text))
                                continue;
                            PostNote(note.text);
                        }
                    }
                }
                Thread.Sleep(2000);
            }
        }

        static FriendWire SendText(string to, string text)
        {
            FriendWire res = Call("/send", new FriendWire { token = _book.token, to = to, text = text });
            Log("send to=" + to + " local=" + WireText(res) + " peers=" + PeerList());
            if (res != null && res.error == "Not signed in")
            {
                FriendWire hello = Call("/inbox", new FriendWire { pin = _book.pin, token = _book.token });
                if (hello != null && hello.ok)
                {
                    if (hello.messages != null)
                    {
                        for (int i = 0; i < hello.messages.Length; i++)
                        {
                            FriendNote note = hello.messages[i];
                            if (note == null || string.IsNullOrEmpty(note.text))
                                continue;
                            PostNote(note.text);
                        }
                    }
                    res = Call("/send", new FriendWire { token = _book.token, to = to, text = text });
                    Log("send retry local=" + WireText(res));
                }
                else
                {
                    res = hello;
                    Log("sign-in " + WireText(hello));
                }
            }
            if (res != null && res.error == "Friend offline")
            {
                IPEndPoint end = PeerEnd(to);
                if (end == null)
                    Log("no beacon for " + to);
                else
                {
                    byte[] buf = Encoding.UTF8.GetBytes("M\n" + _book.pin + "\n" + to + "\n" + text);
                    if (UdpSend(end, buf))
                    {
                        Log("udp sent " + end.Address + ":" + end.Port);
                        res = new FriendWire();
                        res.ok = true;
                    }
                    else
                        Log("udp failed " + end.Address + ":" + end.Port);
                }
            }
            return res;
        }

        public static void SendFile(string path)
        {
            Thread worker = new Thread(new ThreadStart(delegate { SendFileNow(path); }));
            worker.IsBackground = true;
            worker.Start();
        }

        const int ChunkBytes = 768;
        const int MaxFile = 32 * 1024 * 1024;
        static readonly object _fileSend = new object();
        static readonly object _ackLock = new object();
        static readonly ManualResetEvent _ackHit = new ManualResetEvent(false);
        static string _ackName = "";
        static int _ackIndex = -1;

        static int ChunkCount(int len)
        {
            if (len <= 0)
                return 1;
            return (len + ChunkBytes - 1) / ChunkBytes;
        }

        static string FilePacket(string from, string to, string name, int index, int total, string b64)
        {
            return "F\n" + from + "\n" + to + "\n" + name + "\n" + index + "\n" + total + "\n" + b64;
        }

        static string AckPacket(string from, string to, string name, int index)
        {
            return "A\n" + from + "\n" + to + "\n" + name + "\n" + index;
        }

        static void SendFileNow(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                PostLine("Drop a file");
                return;
            }
            if (_book == null || CleanPin(_book.friend).Length == 0)
            {
                PostLine("Connect a friend first");
                return;
            }
            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                Log("file read " + ex.Message);
                PostLine("Could not read file");
                return;
            }
            if (size > MaxFile)
            {
                PostLine("Max is 32 MB");
                return;
            }
            string to = CleanPin(_book.friend);
            IPEndPoint end = PeerEnd(to);
            if (end == null)
            {
                Log("no beacon for file " + to);
                PostLine("Friend offline");
                return;
            }
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                Log("file read " + ex.Message);
                PostLine("Could not read file");
                return;
            }
            string name = SafeName(Path.GetFileName(path));
            int total = ChunkCount(raw.Length);
            Log("file drop " + name + " bytes=" + raw.Length);
            PostLine("Sending " + name);
            lock (_fileSend)
            {
                for (int i = 0; i < total; i++)
                {
                    int n = Math.Min(ChunkBytes, raw.Length - i * ChunkBytes);
                    string b64 = Convert.ToBase64String(raw, i * ChunkBytes, n);
                    byte[] buf = Encoding.UTF8.GetBytes(FilePacket(_book.pin, to, name, i, total, b64));
                    bool ok = false;
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        lock (_ackLock)
                        {
                            _ackName = name;
                            _ackIndex = i;
                            _ackHit.Reset();
                        }
                        if (!UdpSend(end, buf))
                            break;
                        if (_ackHit.WaitOne(400))
                        {
                            ok = true;
                            break;
                        }
                    }
                    if (!ok)
                    {
                        Log("file failed " + name + " chunk " + i + " no ack " + end.Address + ":" + end.Port);
                        PostLine("No answer from friend. Update their mascot.exe");
                        return;
                    }
                }
            }
            Log("file sent " + name + " bytes=" + raw.Length);
            PostLine("Sent " + name + " to " + to);
        }

        static void Hear()
        {
            if (_hear != null)
                return;
            try
            {
                _hear = new UdpClient();
                _hear.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _hear.Client.Bind(new IPEndPoint(IPAddress.Any, 8788));
                _hear.EnableBroadcast = true;
                _hear.Client.ReceiveBufferSize = 256 * 1024;
            }
            catch (Exception ex)
            {
                Log("beacon listen failed " + ex.Message);
                return;
            }
            Log("beacon listening on 8788");
            Thread hear = new Thread(new ThreadStart(delegate
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                while (_run && _hear != null)
                {
                    try
                    {
                        byte[] data = _hear.Receive(ref remote);
                        string raw = Encoding.UTF8.GetString(data);
                        if (raw.StartsWith("A\n"))
                        {
                            bool hit = false;
                            lock (_ackLock)
                            {
                                if (_book != null && AckMatches(raw, _book.pin, _ackName, _ackIndex))
                                {
                                    _ackHit.Set();
                                    hit = true;
                                }
                            }
                            if (!hit)
                                Log("file ack ignored");
                            continue;
                        }
                        if (raw.StartsWith("F\n"))
                        {
                            IPEndPoint fromEnd = new IPEndPoint(remote.Address, remote.Port);
                            TakeFile(raw, fromEnd);
                            continue;
                        }
                        string kind;
                        string pin;
                        string msgFrom;
                        string msgTo;
                        string msgText;
                        if (!ReadPacket(raw, out kind, out pin, out msgFrom, out msgTo, out msgText))
                            continue;
                        if (remote.Address == null)
                            continue;
                        long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                        if (kind == "M")
                        {
                            if (_book != null && msgTo == _book.pin)
                            {
                                Log("got message from " + msgFrom + " via " + remote.Address + ":" + remote.Port);
                                PostNote(msgText);
                            }
                            continue;
                        }
                        if (pin.Length == 0)
                            continue;
                        if (_book != null && pin == _book.pin)
                        {
                            if (!Ours(remote.Address))
                                NameClash(remote.Address.ToString());
                            continue;
                        }
                        bool reply = false;
                        lock (_peers)
                        {
                            PeerHit hit;
                            if (!_peers.TryGetValue(pin, out hit))
                                hit = new PeerHit();
                            string whereIp = remote.Address.ToString();
                            string prevIp = hit.End == null ? "" : hit.End.Address.ToString();
                            if (prevIp != whereIp)
                                Log("heard " + pin + " at " + whereIp);
                            bool stable = remote.Port == 8788;
                            bool first = hit.End == null || prevIp != whereIp;
                            hit.End = new IPEndPoint(remote.Address, remote.Port);
                            hit.Seen = now;
                            if (first || (stable && now - hit.Replied > 30000))
                            {
                                hit.Replied = now;
                                reply = first;
                            }
                            _peers[pin] = hit;
                        }
                        if (reply && _book != null)
                        {
                            UdpSend(remote, Encoding.UTF8.GetBytes("P\n" + _book.pin + "\n"));
                            Log("replied to " + remote.Address + ":" + remote.Port);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_run)
                            break;
                        Log("beacon read " + ex.Message);
                        Thread.Sleep(2000);
                    }
                }
            }));
            hear.IsBackground = true;
            hear.Start();
        }

        static void Shout()
        {
            if (_hear == null || _book == null || CleanPin(_book.pin).Length == 0)
                return;
            byte[] buf = Encoding.UTF8.GetBytes("P\n" + _book.pin + "\n");
            UdpSend(new IPEndPoint(IPAddress.Broadcast, 8788), buf);
        }

        static bool UdpSend(IPEndPoint end, byte[] buf)
        {
            if (_hear == null || end == null || buf == null)
                return false;
            try
            {
                lock (_udpLock)
                    _hear.Send(buf, buf.Length, end);
                return true;
            }
            catch (Exception ex)
            {
                Log("udp send failed " + end + " " + ex.Message);
                return false;
            }
        }

        static IPEndPoint PeerEnd(string pin)
        {
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            lock (_peers)
            {
                PeerHit hit;
                if (!_peers.TryGetValue(pin, out hit) || hit.End == null || now - hit.Seen > TtlMs)
                    return null;
                return new IPEndPoint(hit.End.Address, hit.End.Port);
            }
        }

        static bool ReadPacket(string packet, out string kind, out string pin, out string from, out string to, out string text)
        {
            kind = "";
            pin = "";
            from = "";
            to = "";
            text = "";
            if (string.IsNullOrEmpty(packet))
                return false;
            string[] parts = packet.Replace("\r", "").Split(new char[] { '\n' }, 4);
            if (parts[0] == "P" && parts.Length >= 2)
            {
                kind = "P";
                pin = CleanPin(parts[1]);
                return pin.Length > 0;
            }
            if (parts[0] == "M" && parts.Length >= 4)
            {
                kind = "M";
                from = CleanPin(parts[1]);
                to = CleanPin(parts[2]);
                text = parts[3].Trim();
                return from.Length > 0 && to.Length > 0 && text.Length > 0;
            }
            pin = CleanPin(packet.Trim());
            if (pin.Length == 0)
                return false;
            kind = "P";
            return true;
        }

        // ponytail: stop-and-wait UDP, whole file in RAM. Cap 32MB.
        static Dictionary<string, FileIn> _files = new Dictionary<string, FileIn>();

        sealed class FileIn
        {
            public int Total;
            public string[] Parts;
            public int Got;
        }

        static bool TryFile(string packet, out string from, out string to, out string name, out int index, out int total, out string b64)
        {
            from = "";
            to = "";
            name = "";
            b64 = "";
            index = 0;
            total = 0;
            if (string.IsNullOrEmpty(packet))
                return false;
            string[] parts = packet.Replace("\r", "").Split(new char[] { '\n' }, 7);
            if (parts.Length < 7 || parts[0] != "F")
                return false;
            from = CleanPin(parts[1]);
            to = CleanPin(parts[2]);
            name = SafeName(parts[3]);
            if (!int.TryParse(parts[4], out index) || !int.TryParse(parts[5], out total))
                return false;
            b64 = parts[6];
            if (from.Length == 0 || to.Length == 0 || name.Length == 0)
                return false;
            if (index < 0 || total < 1 || index >= total || total > ChunkCount(MaxFile))
                return false;
            return true;
        }

        static bool AckMatches(string packet, string ourPin, string name, int index)
        {
            if (string.IsNullOrEmpty(packet))
                return false;
            string[] parts = packet.Replace("\r", "").Split('\n');
            if (parts.Length < 5 || parts[0] != "A")
                return false;
            int got;
            if (!int.TryParse(parts[4], out got))
                return false;
            return CleanPin(parts[2]) == CleanPin(ourPin) && SafeName(parts[3]) == name && got == index;
        }

        static void TakeFile(string packet, IPEndPoint remote)
        {
            string from;
            string to;
            string name;
            int index;
            int total;
            string b64;
            if (!TryFile(packet, out from, out to, out name, out index, out total, out b64))
                return;
            if (_book == null || to != _book.pin)
                return;
            UdpSend(remote, Encoding.UTF8.GetBytes(AckPacket(_book.pin, from, name, index)));
            string[] done = null;
            lock (_files)
            {
                if (_files.Count > 4)
                    _files.Clear();
                string key = from + "\n" + name + "\n" + total;
                FileIn box;
                if (!_files.TryGetValue(key, out box))
                {
                    box = new FileIn();
                    box.Total = total;
                    box.Parts = new string[total];
                    _files[key] = box;
                }
                if (box.Parts[index] == null)
                    box.Got++;
                box.Parts[index] = b64;
                if (index == 0)
                    Log("file in " + name + " chunks=" + total);
                if (box.Got < box.Total)
                    return;
                done = box.Parts;
                _files.Remove(key);
            }
            byte[] raw = JoinFile(done);
            if (raw == null)
                return;
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "received");
                Directory.CreateDirectory(dir);
                string dest = UniqueFile(dir, name);
                File.WriteAllBytes(dest, raw);
                Log("file saved " + dest);
                PostFile(name, dest);
            }
            catch (Exception ex)
            {
                Log("file save " + ex.Message);
            }
        }

        static byte[] JoinFile(string[] parts)
        {
            try
            {
                MemoryStream ms = new MemoryStream();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null)
                        return null;
                    byte[] chunk = Convert.FromBase64String(parts[i]);
                    ms.Write(chunk, 0, chunk.Length);
                }
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        static string UniqueFile(string dir, string name)
        {
            string dest = Path.Combine(dir, name);
            if (!File.Exists(dest))
                return dest;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            for (int i = 2; i < 100; i++)
            {
                dest = Path.Combine(dir, stem + " " + i + ext);
                if (!File.Exists(dest))
                    return dest;
            }
            return Path.Combine(dir, stem + " " + DateTime.Now.Ticks + ext);
        }

        static string SafeName(string name)
        {
            name = Path.GetFileName(name ?? "");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length && sb.Length < 80; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ' ')
                    sb.Append(c);
            }
            string s = sb.ToString().Trim().Trim('.');
            if (s.Length == 0)
                return "file";
            return s;
        }

        static void AllowLan()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo();
                info.FileName = "netsh";
                info.Arguments = "advfirewall firewall add rule name=\"Mascot friends\" dir=in action=allow program=\"" + Application.ExecutablePath + "\" enable=yes profile=any";
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                System.Diagnostics.Process proc = System.Diagnostics.Process.Start(info);
                if (proc == null)
                {
                    Log("firewall rule did not start");
                    return;
                }
                proc.WaitForExit(4000);
                Log("firewall rule exit " + proc.ExitCode);
            }
            catch (Exception ex)
            {
                Log("firewall rule " + ex.Message);
            }
        }

        static void TakeFocus(Form form, TextBox box)
        {
            if (form == null || form.IsDisposed)
                return;
            form.TopMost = true;
            form.Activate();
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(form.Handle);
            if (box != null && !box.IsDisposed)
            {
                form.ActiveControl = box;
                box.Focus();
                box.Select();
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        static void PostLine(string line)
        {
            Post(delegate { _pet.ShowLine(line); });
        }

        static void PostNote(string line)
        {
            Post(delegate { _pet.DeliverNote(line); });
        }

        static void PostFile(string name, string path)
        {
            Post(delegate { _pet.DeliverCard(name, null, path); });
        }

        static void Post(Action action)
        {
            PetForm pet = _pet;
            if (pet == null || pet.IsDisposed)
                return;
            try
            {
                pet.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        static bool _relayReady;
        static bool _relayStarting;
        static string _lastCallErr = "";

        internal static void Log(string line)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mascot-error.txt");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + line + "\r\n");
            }
            catch
            {
            }
        }

        static string WireText(FriendWire wire)
        {
            if (wire == null)
                return "no response";
            if (wire.ok)
                return "ok";
            return wire.error ?? "failed";
        }

        static string PeerList()
        {
            lock (_peers)
            {
                if (_peers.Count == 0)
                    return "none";
                string text = "";
                foreach (KeyValuePair<string, PeerHit> pair in _peers)
                {
                    if (text.Length > 0)
                        text += " ";
                    string where = pair.Value.End == null ? "?" : pair.Value.End.ToString();
                    text += pair.Key + "@" + where;
                }
                return text;
            }
        }

        static string LocalIps()
        {
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(Dns.GetHostName());
                string text = "";
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(all[i]))
                        continue;
                    if (text.Length > 0)
                        text += ",";
                    text += all[i];
                }
                return text.Length == 0 ? "none" : text;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        static void EnsureRelay()
        {
            if (_relayReady || _book == null || string.IsNullOrEmpty(_book.relay))
                return;
            Uri uri;
            if (!Uri.TryCreate(_book.relay, UriKind.Absolute, out uri))
                return;
            bool local = uri.Host == "127.0.0.1" || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            if (!local)
            {
                Log("using remote relay " + _book.relay);
                return;
            }
            if (Probe(uri))
            {
                _relayReady = true;
                Log("relay already up " + _book.relay);
                return;
            }
            if (!_relayStarting)
            {
                _relayStarting = true;
                int port = uri.Port > 0 ? uri.Port : 8787;
                Thread relay = new Thread(new ThreadStart(delegate { Serve(new string[] { "--relay", port.ToString() }); }));
                relay.IsBackground = true;
                relay.Start();
            }
            for (int i = 0; i < 40 && !Probe(uri); i++)
                Thread.Sleep(50);
            _relayReady = Probe(uri);
            Log(_relayReady ? "relay ready " + _book.relay : "relay not ready " + _book.relay);
        }

        static bool Probe(Uri uri)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri.GetLeftPart(UriPartial.Authority) + "/");
                req.Method = "GET";
                req.Timeout = 400;
                req.Proxy = new WebProxy();
                using ((HttpWebResponse)req.GetResponse())
                    return true;
            }
            catch
            {
                return false;
            }
        }

        static FriendWire Call(string path, FriendWire body)
        {
            EnsureRelay();
            if (_book == null || string.IsNullOrEmpty(_book.relay))
                return null;
            return CallUrl(_book.relay.TrimEnd('/') + path, body);
        }

        static FriendWire CallUrl(string url, FriendWire body)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 5000;
                req.Proxy = new WebProxy();
                req.ServicePoint.Expect100Continue = false;
                byte[] buf = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
                req.ContentLength = buf.Length;
                using (Stream stream = req.GetRequestStream())
                    stream.Write(buf, 0, buf.Length);
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    return new JavaScriptSerializer().Deserialize<FriendWire>(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                string line = url + " " + ex.GetType().Name + " " + ex.Message;
                if (line != _lastCallErr)
                {
                    _lastCallErr = line;
                    Log(line);
                }
                return null;
            }
        }

        static FriendBook Load()
        {
            string path = BookPath();
            FriendBook book = new FriendBook();
            book.relay = "http://127.0.0.1:8787";
            if (File.Exists(path))
            {
                try
                {
                    FriendBook saved = new JavaScriptSerializer().Deserialize<FriendBook>(File.ReadAllText(path));
                    if (saved != null)
                        book = saved;
                }
                catch
                {
                }
            }
            if (CleanPin(book.pin).Length == 0 || string.IsNullOrEmpty(book.token) || string.IsNullOrEmpty(book.relay))
            {
                if (!Ask(book))
                    return null;
                Save(book);
            }
            book.pin = CleanPin(book.pin);
            book.relay = book.relay.Trim().TrimEnd('/');
            book.friends = Normalize(book.friends, book.friend);
            if (CleanPin(book.friend).Length == 0 && book.friends.Length > 0)
                book.friend = book.friends[0];
            return book;
        }

        static string[] Normalize(string[] pins, string extra)
        {
            List<string> list = new List<string>();
            if (pins != null)
            {
                for (int i = 0; i < pins.Length; i++)
                    KeepPin(list, pins[i]);
            }
            KeepPin(list, extra);
            return list.ToArray();
        }

        static void KeepPin(List<string> list, string pin)
        {
            pin = CleanPin(pin);
            if (pin.Length == 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == pin)
                    return;
            }
            list.Add(pin);
        }

        static void FillFriends(ListBox list)
        {
            string pick = _book.friend;
            list.Items.Clear();
            if (_book.friends == null)
                return;
            for (int i = 0; i < _book.friends.Length; i++)
                list.Items.Add(_book.friends[i]);
            if (pick == null)
                return;
            int at = list.Items.IndexOf(pick);
            if (at >= 0)
                list.SelectedIndex = at;
        }

        const string NameRule = "Username is 3–16 letters, numbers, or _";
        static bool _renaming;

        static bool Ours(IPAddress ip)
        {
            if (ip == null || IPAddress.IsLoopback(ip))
                return true;
            string[] parts = LocalIps().Split(',');
            string needle = ip.ToString();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == needle)
                    return true;
            }
            return false;
        }

        static bool NameInUse(string name)
        {
            name = CleanPin(name);
            if (name.Length == 0)
                return false;
            lock (_peers)
            {
                PeerHit hit;
                if (!_peers.TryGetValue(name, out hit) || hit.End == null)
                    return false;
                return !Ours(hit.End.Address);
            }
        }

        static void NameClash(string where)
        {
            if (_renaming || _book == null || CleanPin(_book.pin).Length == 0)
                return;
            _renaming = true;
            Log("username taken " + _book.pin + " at " + where);
            _book.pin = "";
            Save(_book);
            PostLine("Username taken");
            Post(delegate
            {
                try
                {
                    if (Ask(_book))
                        Save(_book);
                }
                finally
                {
                    _renaming = false;
                }
            });
        }

        static string TargetLine()
        {
            if (CleanPin(_book.friend).Length == 0)
                return "Add a friend. Ctrl+M sends.";
            return "Files go to " + _book.friend + ". Ctrl+M sends.";
        }

        static void Save(FriendBook book)
        {
            File.WriteAllText(BookPath(), new JavaScriptSerializer().Serialize(book));
        }

        static string BookPath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "friends.json");
        }

        static bool Ask(FriendBook book)
        {
            Form form = new Form();
            form.Text = "Your username";
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ClientSize = new Size(380, 210);
            form.TopMost = true;
            form.Font = new Font("Segoe UI", 9f);

            Label help = new Label();
            help.Text = "Pick a username (3–16 letters, numbers, or _). Friends type it to reach you.";
            help.Bounds = new Rectangle(16, 12, 348, 36);
            TextBox pin = new TextBox();
            pin.Bounds = new Rectangle(16, 52, 348, 24);
            Label relayLabel = new Label();
            relayLabel.Text = "Relay (same address on every PC)";
            relayLabel.Bounds = new Rectangle(16, 84, 348, 18);
            TextBox relay = new TextBox();
            relay.Text = string.IsNullOrEmpty(book.relay) ? "http://127.0.0.1:8787" : book.relay;
            relay.Bounds = new Rectangle(16, 104, 348, 24);
            Label err = new Label();
            err.Bounds = new Rectangle(16, 132, 348, 32);
            Button save = new Button();
            save.Text = "Save";
            save.Bounds = new Rectangle(16, 170, 80, 28);
            save.Click += delegate
            {
                string cleaned = CleanPin(pin.Text);
                string url = relay.Text == null ? "" : relay.Text.Trim();
                if (cleaned.Length == 0)
                {
                    err.Text = NameRule;
                    return;
                }
                if (NameInUse(cleaned))
                {
                    err.Text = "Username taken";
                    return;
                }
                if (url.IndexOf("http://", StringComparison.OrdinalIgnoreCase) != 0 && url.IndexOf("https://", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    err.Text = "Relay must start with http:// or https://";
                    return;
                }
                book.pin = cleaned;
                book.relay = url.TrimEnd('/');
                book.token = Guid.NewGuid().ToString("N");
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(help);
            form.Controls.Add(pin);
            form.Controls.Add(relayLabel);
            form.Controls.Add(relay);
            form.Controls.Add(err);
            form.Controls.Add(save);
            form.AcceptButton = save;
            return form.ShowDialog() == DialogResult.OK;
        }

        public static string CleanPin(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            string s = text.Trim().ToLowerInvariant();
            if (s.Length < 3 || s.Length > 16)
                return "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                    return "";
            }
            return s;
        }
    }

    sealed class FriendSheet : Form
    {
        readonly Skin _skin;
        readonly Font _tiny;
        readonly Font _head;
        readonly List<Control> _chrome = new List<Control>();
        readonly System.Windows.Forms.Timer _clock;
        readonly string _title;

        public int PadL { get { return _skin.TimerPadL; } }
        public int PadT { get { return _skin.TimerPadT; } }
        public int PadR { get { return _skin.TimerPadR; } }
        public int PadB { get { return _skin.TimerPadB; } }
        public int LabelH { get { return TextRenderer.MeasureText("Ag", _tiny).Height + 2; } }
        public int BoxH { get { return LabelH + 12; } }
        public int HeadBottom { get { return PadT + TextRenderer.MeasureText(_title, _head).Height + 12; } }
        public Color Alert { get { return _skin.TimerAlert; } }
        public Color Mute { get { return _skin.TimerMuted; } }

        public FriendSheet(string title)
        {
            _title = title;
            _skin = Skin.Load();
            _tiny = new Font("Consolas", Math.Max(8f, _skin.TimerFont));
            _head = new Font("Consolas", Math.Max(11f, _skin.TimerFont + 2f), FontStyle.Bold);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = _skin.TimerBg;
            ForeColor = _skin.TimerFg;
            DoubleBuffered = true;
            KeyPreview = true;
            Font = _tiny;
            Text = title;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Escape)
                    return;
                e.Handled = true;
                Close();
            };
            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 1000;
            _clock.Tick += delegate { Invalidate(new Rectangle(0, 0, Width, HeadBottom)); };
            _clock.Start();
            FormClosed += delegate
            {
                _clock.Stop();
                _clock.Dispose();
                _tiny.Dispose();
                _head.Dispose();
            };
        }

        public Label Lab(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = _skin.TimerMuted;
            label.BackColor = _skin.TimerBg;
            label.Font = _tiny;
            label.AutoSize = false;
            return label;
        }

        public TextBox Field()
        {
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.AutoSize = false;
            box.BackColor = _skin.TimerBg;
            box.ForeColor = _skin.TimerFg;
            box.Font = _tiny;
            box.HandleCreated += delegate(object s, EventArgs e)
            {
                TextBox field = (TextBox)s;
                SendMessage(field.Handle, 0xD3, (IntPtr)3, (IntPtr)(4 | (4 << 16)));
            };
            return box;
        }

        public RoundButton Chip(string text, bool on)
        {
            RoundButton button = new RoundButton();
            button.Text = text;
            button.Font = _tiny;
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.Radius = _skin.TimerRadius;
            button.BackColor = on ? _skin.TimerButton : _skin.TimerBg;
            button.ForeColor = on ? _skin.TimerFg : _skin.TimerMuted;
            button.FlatAppearance.BorderColor = on ? _skin.TimerBorder : _skin.TimerMuted;
            button.FlatAppearance.MouseOverBackColor = _skin.TimerButton;
            return button;
        }

        public ListBox Names()
        {
            ListBox list = new ListBox();
            list.BorderStyle = BorderStyle.None;
            list.BackColor = _skin.TimerBg;
            list.ForeColor = _skin.TimerFg;
            list.Font = _tiny;
            list.IntegralHeight = false;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Math.Max(18, LabelH);
            list.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0)
                    return;
                bool on = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush fill = new SolidBrush(on ? _skin.TimerButton : _skin.TimerBg))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), _tiny, e.Bounds, _skin.TimerFg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
            return list;
        }

        public void Track(Control control)
        {
            _chrome.Add(control);
        }

        public void Fit(int height)
        {
            ClientSize = new Size(296 + PadL + PadR, height);
            using (GraphicsPath path = Skin.Round(new Rectangle(0, 0, Width, Height), _skin.TimerRadius))
            {
                Region next = new Region(path);
                Region prev = Region;
                Region = next;
                if (prev != null)
                    prev.Dispose();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(_skin.TimerBorder, _skin.TimerBorderW))
            {
                if (_skin.TimerRadius >= 2)
                {
                    Rectangle box = new Rectangle(1, 1, Width - 3, Height - 3);
                    using (GraphicsPath path = Skin.Round(box, _skin.TimerRadius))
                        g.DrawPath(pen, path);
                }
            }
            using (SolidBrush ink = new SolidBrush(_skin.TimerFg))
            using (SolidBrush mute = new SolidBrush(_skin.TimerMuted))
            {
                g.DrawString(_title, _head, ink, PadL, PadT);
                string clock = DateTime.Now.ToString("HH:mm:ss");
                Size cs = TextRenderer.MeasureText(clock, _tiny, new Size(200, 40), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int xClose = Width - PadR - 14;
                g.DrawString(clock, _tiny, mute, xClose - cs.Width - 10, PadT + 2);
            }
            using (Pen xpen = new Pen(_skin.TimerMuted, 1.6f))
            {
                int x0 = Width - PadR - 12;
                int y0 = PadT + 2;
                g.DrawLine(xpen, x0, y0, x0 + 10, y0 + 10);
                g.DrawLine(xpen, x0 + 10, y0, x0, y0 + 10);
            }
            for (int i = 0; i < _chrome.Count; i++)
            {
                Control control = _chrome[i];
                if (control == null || !control.Visible)
                    continue;
                Rectangle r = new Rectangle(control.Left - 6, control.Top - 4, control.Width + 12, control.Height + 8);
                int rad = _skin.TimerRadius;
                if (rad * 2 > r.Width) rad = Math.Max(0, r.Width / 2);
                if (rad * 2 > r.Height) rad = Math.Max(0, r.Height / 2);
                using (GraphicsPath path = Skin.Round(r, rad))
                using (Pen edge = new Pen(_skin.TimerBorder, 1.5f))
                    g.DrawPath(edge, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < PadT + 22 && e.X > Width - PadR - 24)
                Close();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
