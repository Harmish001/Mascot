using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Mascot
{
    sealed class AvatarConfig
    {
        public string ModelPath;
        public float YawOffset;
        public float Turn;
        public float Scale;
        public bool Is2d;

        public static AvatarConfig Load()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string cfg = Path.Combine(dir, "avatar.json");
            string model = "1qh22.glb";
            float yaw = 0f;
            float turn = 360f;
            float scale = 1f;
            string mode = "3d";
            if (File.Exists(cfg))
            {
                string json = File.ReadAllText(cfg);
                model = JsonString(json, "model", model);
                yaw = JsonFloat(json, "yawOffset", yaw);
                turn = JsonFloat(json, "turn", turn);
                scale = JsonFloat(json, "scale", scale);
                mode = JsonString(json, "mode", mode);
            }
            if (turn < 1f || turn > 360f)
                turn = 360f;
            if (scale <= 0f || scale > 3f)
                scale = 1f;
            if (!Path.IsPathRooted(model))
                model = Path.Combine(dir, model);
            AvatarConfig c = new AvatarConfig();
            c.ModelPath = model;
            c.YawOffset = yaw;
            c.Turn = turn;
            c.Scale = scale;
            c.Is2d = mode == "2d" || mode == "2D";
            return c;
        }

        static string JsonString(string json, string key, string fallback)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0)
                return fallback;
            int colon = json.IndexOf(':', i + token.Length);
            if (colon < 0)
                return fallback;
            int q1 = json.IndexOf('"', colon + 1);
            int q2 = q1 < 0 ? -1 : json.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0)
                return fallback;
            return json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\\", "\\").Replace("\\/", "/");
        }

        static float JsonFloat(string json, string key, float fallback)
        {
            string token = "\"" + key + "\"";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0)
                return fallback;
            int colon = json.IndexOf(':', i + token.Length);
            if (colon < 0)
                return fallback;
            int s = colon + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\n' || json[s] == '\r' || json[s] == '\t'))
                s++;
            int e = s;
            while (e < json.Length && "0123456789+-.".IndexOf(json[e]) >= 0)
                e++;
            float v;
            if (e > s && float.TryParse(json.Substring(s, e - s), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }
    }

    sealed class GlbMesh
    {
        public float[] Positions;
        public float[] Normals;
        public float[] Uvs;
        public uint[] Indices;
        public int TexW;
        public int TexH;
        public byte[] Rgba;
        public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
        public float[] BindPos;
        public float ClipTime;
        public bool Skinned;
        public bool LayFlat;
        int[] _parent;
        int[] _roots;
        float[] _restMat;
        float[] _restT;
        float[] _restR;
        float[] _restS;
        bool[] _isMatrix;
        int[] _kids;
        int[] _kidAt;
        int[] _kidN;
        int[] _jointOf;
        float[] _ibm;
        Chan[] _chans;
        float[] _curT;
        float[] _curR;
        float[] _curS;
        float[] _global;
        float[] _skin;
        float[] _scratch;
        ushort[] _joints;
        float[] _weights;

        struct Chan
        {
            public int Node;
            public int Path;
            public float[] Times;
            public float[] Values;
        }

        public int VertexCount
        {
            get { return Positions.Length / 3; }
        }

        public static int SelfCheck(string path)
        {
            GlbMesh mesh = Load(path);
            if (mesh.VertexCount < 3 || mesh.Indices.Length < 3 || mesh.Indices.Length % 3 != 0)
                return Fail("mesh empty");
            if (mesh.TexW < 1 || mesh.Rgba == null)
                return Fail("texture missing");
            if (mesh.MaxY <= mesh.MinY)
                return Fail("flat bounds");
            if (mesh.Skinned)
            {
                float[] at0 = (float[])mesh.Positions.Clone();
                mesh.Pose(mesh.ClipTime * 0.37f);
                int moved = 0;
                int n = mesh.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int o = i * 3;
                    float dx = at0[o] - mesh.Positions[o];
                    float dy = at0[o + 1] - mesh.Positions[o + 1];
                    float dz = at0[o + 2] - mesh.Positions[o + 2];
                    if (dx * dx + dy * dy + dz * dz > 1e-8f)
                        moved++;
                }
                mesh.Pose(0f);
                if (moved < 30)
                    return Fail("clip did not move");
            }
            string gil = Path.Combine(Path.GetDirectoryName(path), "cute_gil.glb");
            if (File.Exists(gil))
            {
                GlbMesh cute = Load(gil);
                float hx = cute.MaxX - cute.MinX;
                float hy = cute.MaxY - cute.MinY;
                float hz = cute.MaxZ - cute.MinZ;
                if (hy < hx || hy < hz)
                    return Fail("cute_gil lying down");
                int blue = 0;
                int samples = 0;
                int step = Math.Max(1, (cute.TexW * cute.TexH) / 200);
                for (int i = 0; i < cute.TexW * cute.TexH; i += step)
                {
                    int o = i * 4;
                    samples++;
                    if (cute.Rgba[o + 2] > cute.Rgba[o] + 30 && cute.Rgba[o + 2] > cute.Rgba[o + 1] + 30)
                        blue++;
                }
                if (blue * 2 > samples)
                    return Fail("cute_gil normal map");
            }
            Console.WriteLine("mesh ok " + mesh.VertexCount + (mesh.Skinned ? " skinned" : ""));
            return 0;
        }

        static int Fail(string why)
        {
            Console.Error.WriteLine(why);
            return 1;
        }

        public static GlbMesh Load(string path)
        {
            string json;
            byte[] bin;
            string dir = Path.GetDirectoryName(path);
            if (path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                ReadGltf(path, out json, out bin);
            else
                ReadGlb(path, out json, out bin);
            return Build(json, bin, dir);
        }

        static void ReadGlb(string path, out string json, out byte[] bin)
        {
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 20 || file[0] != 'g' || file[1] != 'l' || file[2] != 'T' || file[3] != 'F')
                throw new InvalidOperationException("Not a GLB: " + path);
            int jsonLen = BitConverter.ToInt32(file, 12);
            json = System.Text.Encoding.UTF8.GetString(file, 20, jsonLen).Trim('\0', ' ', '\n', '\r', '\t');
            int binStart = 20 + jsonLen;
            int binLen = BitConverter.ToInt32(file, binStart);
            bin = new byte[binLen];
            Buffer.BlockCopy(file, binStart + 8, bin, 0, binLen);
        }

        static void ReadGltf(string path, out string json, out byte[] bin)
        {
            json = File.ReadAllText(path);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = (Dictionary<string, object>)ser.DeserializeObject(json);
            object[] buffers = AsArray(root["buffers"]);
            Dictionary<string, object> buffer = (Dictionary<string, object>)buffers[0];
            string uri = Convert.ToString(buffer["uri"]);
            string dir = Path.GetDirectoryName(path);
            bin = File.ReadAllBytes(Path.Combine(dir, uri.Replace('/', Path.DirectorySeparatorChar)));
        }

        static GlbMesh Build(string json, byte[] bin, string dir)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = (Dictionary<string, object>)ser.DeserializeObject(json);
            object[] views = AsArray(root["bufferViews"]);
            object[] accs = AsArray(root["accessors"]);
            Dictionary<string, object> prim = Prim(root);
            Dictionary<string, object> attrs = (Dictionary<string, object>)prim["attributes"];

            int posAcc = Convert.ToInt32(attrs["POSITION"]);
            int nrmAcc = Convert.ToInt32(attrs["NORMAL"]);
            int uvAcc = Convert.ToInt32(attrs["TEXCOORD_0"]);
            int idxAcc = Convert.ToInt32(prim["indices"]);

            GlbMesh mesh = new GlbMesh();
            mesh.Positions = ReadVec(bin, views, accs, posAcc, 3);
            mesh.Normals = ReadVec(bin, views, accs, nrmAcc, 3);
            mesh.Uvs = ReadVec(bin, views, accs, uvAcc, 2);
            mesh.Indices = ReadIndices(bin, views, accs, idxAcc);
            LoadTexture(mesh, root, bin, dir);
            AttachSkin(mesh, root, bin, views, accs);
            if (mesh.Skinned)
            {
                mesh.BindPos = (float[])mesh.Positions.Clone();
                mesh.FitClip();
            }
            else
            {
                BakeRest(mesh, root);
                Bounds(mesh);
            }
            return mesh;
        }

        static int BaseImage(Dictionary<string, object> root)
        {
            if (!root.ContainsKey("images"))
                return -1;
            Dictionary<string, object> prim = Prim(root);
            if (!prim.ContainsKey("material") || !root.ContainsKey("materials") || !root.ContainsKey("textures"))
                return 0;
            object[] materials = AsArray(root["materials"]);
            Dictionary<string, object> mat = (Dictionary<string, object>)materials[Convert.ToInt32(prim["material"])];
            if (!mat.ContainsKey("pbrMetallicRoughness"))
                return 0;
            Dictionary<string, object> pbr = (Dictionary<string, object>)mat["pbrMetallicRoughness"];
            if (!pbr.ContainsKey("baseColorTexture"))
                return -1;
            Dictionary<string, object> info = (Dictionary<string, object>)pbr["baseColorTexture"];
            object[] textures = AsArray(root["textures"]);
            Dictionary<string, object> tex = (Dictionary<string, object>)textures[Convert.ToInt32(info["index"])];
            return Convert.ToInt32(tex["source"]);
        }

        static void LoadTexture(GlbMesh mesh, Dictionary<string, object> root, byte[] bin, string dir)
        {
            int imageIndex = BaseImage(root);
            if (imageIndex < 0)
            {
                mesh.TexW = 1;
                mesh.TexH = 1;
                mesh.Rgba = new byte[] { 255, 255, 255, 255 };
                return;
            }
            object[] images = AsArray(root["images"]);
            Dictionary<string, object> image = (Dictionary<string, object>)images[imageIndex];
            Bitmap src;
            if (image.ContainsKey("uri"))
            {
                string uri = Convert.ToString(image["uri"]).Replace('/', Path.DirectorySeparatorChar);
                src = new Bitmap(Path.Combine(dir, uri));
            }
            else
            {
                object[] views = AsArray(root["bufferViews"]);
                int imageView = Convert.ToInt32(image["bufferView"]);
                int offset, length;
                ViewRange(views[imageView], out offset, out length);
                src = new Bitmap(new MemoryStream(bin, offset, length, false));
            }
            using (src)
            using (Bitmap bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                bool opaque = !Image.IsAlphaPixelFormat(src.PixelFormat);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                Bitmap use = bmp;
                Bitmap scaled = null;
                if (bmp.Width > 1024 || bmp.Height > 1024)
                {
                    int nw = bmp.Width;
                    int nh = bmp.Height;
                    while (nw > 1024 || nh > 1024)
                    {
                        nw /= 2;
                        nh /= 2;
                    }
                    scaled = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                    using (Graphics g2 = Graphics.FromImage(scaled))
                    {
                        g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        g2.DrawImage(bmp, 0, 0, nw, nh);
                    }
                    use = scaled;
                }
                mesh.TexW = use.Width;
                mesh.TexH = use.Height;
                BitmapData data = use.LockBits(new Rectangle(0, 0, use.Width, use.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = data.Stride;
                byte[] raw = new byte[Math.Abs(stride) * use.Height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                use.UnlockBits(data);
                int tw = use.Width;
                int th = use.Height;
                if (scaled != null)
                    scaled.Dispose();
                mesh.Rgba = new byte[tw * th * 4];
                for (int y = 0; y < th; y++)
                {
                    int srcRow = (stride < 0 ? (th - 1 - y) * (-stride) : y * stride);
                    int dstRow = y * tw * 4;
                    for (int x = 0; x < tw; x++)
                    {
                        int s = srcRow + x * 4;
                        int d = dstRow + x * 4;
                        mesh.Rgba[d] = raw[s + 2];
                        mesh.Rgba[d + 1] = raw[s + 1];
                        mesh.Rgba[d + 2] = raw[s];
                        mesh.Rgba[d + 3] = opaque ? (byte)255 : raw[s + 3];
                    }
                }
            }
        }

        static void Bounds(GlbMesh mesh)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < mesh.Positions.Length; i += 3)
            {
                float x = mesh.Positions[i];
                float y = mesh.Positions[i + 1];
                float z = mesh.Positions[i + 2];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }
            mesh.MinX = minX;
            mesh.MaxX = maxX;
            mesh.MinY = minY;
            mesh.MaxY = maxY;
            mesh.MinZ = minZ;
            mesh.MaxZ = maxZ;
        }

        // ponytail: first node that uses mesh 0. A second instance of the same mesh is dropped.
        static void BakeRest(GlbMesh mesh, Dictionary<string, object> root)
        {
            if (!root.ContainsKey("nodes"))
                return;
            object[] nodes = AsArray(root["nodes"]);
            int meshNode = -1;
            for (int i = 0; i < nodes.Length; i++)
            {
                Dictionary<string, object> node = (Dictionary<string, object>)nodes[i];
                if (node.ContainsKey("mesh") && Convert.ToInt32(node["mesh"]) == 0)
                {
                    meshNode = i;
                    break;
                }
            }
            if (meshNode < 0)
                return;
            int n = nodes.Length;
            int[] parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = -1;
            for (int i = 0; i < n; i++)
            {
                Dictionary<string, object> node = (Dictionary<string, object>)nodes[i];
                if (!node.ContainsKey("children"))
                    continue;
                object[] kids = AsArray(node["children"]);
                for (int c = 0; c < kids.Length; c++)
                    parent[Convert.ToInt32(kids[c])] = i;
            }
            int[] chain = new int[n];
            int depth = 0;
            for (int i = meshNode; i >= 0 && depth < n; i = parent[i])
                chain[depth++] = i;
            float[] world = new float[16];
            float[] local = new float[16];
            float[] acc = new float[16];
            Buffer.BlockCopy(Ident, 0, world, 0, 64);
            for (int s = depth - 1; s >= 0; s--)
            {
                NodeMatrix((Dictionary<string, object>)nodes[chain[s]], local);
                Mul(world, 0, local, 0, acc, 0);
                Buffer.BlockCopy(acc, 0, world, 0, 64);
            }
            Apply(mesh.Positions, world, true);
            Apply(mesh.Normals, world, false);
        }

        static void NodeMatrix(Dictionary<string, object> node, float[] m)
        {
            if (node.ContainsKey("matrix"))
            {
                object[] mat = AsArray(node["matrix"]);
                for (int k = 0; k < 16; k++)
                    m[k] = Convert.ToSingle(mat[k]);
                return;
            }
            float tx = 0f, ty = 0f, tz = 0f, qx = 0f, qy = 0f, qz = 0f, qw = 1f, sx = 1f, sy = 1f, sz = 1f;
            Read3(node, "translation", ref tx, ref ty, ref tz);
            Read4(node, "rotation", ref qx, ref qy, ref qz, ref qw);
            Read3(node, "scale", ref sx, ref sy, ref sz);
            FromTRS(tx, ty, tz, qx, qy, qz, qw, sx, sy, sz, m);
        }

        static void Apply(float[] p, float[] m, bool point)
        {
            for (int i = 0; i < p.Length; i += 3)
            {
                float x = p[i];
                float y = p[i + 1];
                float z = p[i + 2];
                float nx = m[0] * x + m[4] * y + m[8] * z;
                float ny = m[1] * x + m[5] * y + m[9] * z;
                float nz = m[2] * x + m[6] * y + m[10] * z;
                if (point)
                {
                    p[i] = nx + m[12];
                    p[i + 1] = ny + m[13];
                    p[i + 2] = nz + m[14];
                }
                else
                {
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-8f)
                    {
                        nx /= len;
                        ny /= len;
                        nz /= len;
                    }
                    p[i] = nx;
                    p[i + 1] = ny;
                    p[i + 2] = nz;
                }
            }
        }

        static Dictionary<string, object> Prim(Dictionary<string, object> root)
        {
            object[] meshes = AsArray(root["meshes"]);
            Dictionary<string, object> mesh = (Dictionary<string, object>)meshes[0];
            object[] prims = AsArray(mesh["primitives"]);
            return (Dictionary<string, object>)prims[0];
        }

        static float[] ReadVec(byte[] bin, object[] views, object[] accs, int accIndex, int components)
        {
            Dictionary<string, object> acc = (Dictionary<string, object>)accs[accIndex];
            int count = Convert.ToInt32(acc["count"]);
            int offset = AccOffset(views, acc);
            float[] data = new float[count * components];
            Buffer.BlockCopy(bin, offset, data, 0, data.Length * 4);
            return data;
        }

        static uint[] ReadIndices(byte[] bin, object[] views, object[] accs, int accIndex)
        {
            Dictionary<string, object> acc = (Dictionary<string, object>)accs[accIndex];
            int count = Convert.ToInt32(acc["count"]);
            int offset = AccOffset(views, acc);
            int component = Convert.ToInt32(acc["componentType"]);
            uint[] data = new uint[count];
            if (component == 5125)
            {
                Buffer.BlockCopy(bin, offset, data, 0, count * 4);
                return data;
            }
            if (component == 5123)
            {
                for (int i = 0; i < count; i++)
                    data[i] = BitConverter.ToUInt16(bin, offset + i * 2);
                return data;
            }
            throw new InvalidOperationException("index type " + component);
        }

        static int AccOffset(object[] views, Dictionary<string, object> acc)
        {
            int viewIndex = Convert.ToInt32(acc["bufferView"]);
            int viewOff, viewLen;
            ViewRange(views[viewIndex], out viewOff, out viewLen);
            int extra = acc.ContainsKey("byteOffset") ? Convert.ToInt32(acc["byteOffset"]) : 0;
            return viewOff + extra;
        }

        static void ViewRange(object viewObj, out int offset, out int length)
        {
            Dictionary<string, object> view = (Dictionary<string, object>)viewObj;
            offset = view.ContainsKey("byteOffset") ? Convert.ToInt32(view["byteOffset"]) : 0;
            length = Convert.ToInt32(view["byteLength"]);
        }

        static object[] AsArray(object o)
        {
            object[] arr = o as object[];
            if (arr != null)
                return arr;
            ArrayList list = o as ArrayList;
            if (list != null)
                return list.ToArray();
            throw new InvalidOperationException("expected array");
        }

        static void AttachSkin(GlbMesh mesh, Dictionary<string, object> root, byte[] bin, object[] views, object[] accs)
        {
            Dictionary<string, object> prim = Prim(root);
            Dictionary<string, object> attrs = (Dictionary<string, object>)prim["attributes"];
            if (!attrs.ContainsKey("JOINTS_0") || !root.ContainsKey("skins"))
                return;
            object[] skins = AsArray(root["skins"]);
            Dictionary<string, object> skin = (Dictionary<string, object>)skins[0];
            object[] jointList = AsArray(skin["joints"]);
            int jointCount = jointList.Length;
            mesh._jointOf = new int[jointCount];
            for (int i = 0; i < jointCount; i++)
                mesh._jointOf[i] = Convert.ToInt32(jointList[i]);
            mesh._ibm = ReadVec(bin, views, accs, Convert.ToInt32(skin["inverseBindMatrices"]), 16);
            mesh._joints = ReadJoints(bin, views, accs, Convert.ToInt32(attrs["JOINTS_0"]));
            mesh._weights = ReadVec(bin, views, accs, Convert.ToInt32(attrs["WEIGHTS_0"]), 4);

            object[] nodes = AsArray(root["nodes"]);
            int n = nodes.Length;
            mesh._parent = new int[n];
            mesh._restT = new float[n * 3];
            mesh._restR = new float[n * 4];
            mesh._restS = new float[n * 3];
            mesh._isMatrix = new bool[n];
            mesh._restMat = new float[n * 16];
            mesh._curT = new float[n * 3];
            mesh._curR = new float[n * 4];
            mesh._curS = new float[n * 3];
            mesh._global = new float[n * 16];
            mesh._skin = new float[jointCount * 16];
            mesh._scratch = new float[16];
            mesh._kidAt = new int[n];
            mesh._kidN = new int[n];
            for (int i = 0; i < n; i++)
                mesh._parent[i] = -1;
            List<int>[] kids = new List<int>[n];
            for (int i = 0; i < n; i++)
                kids[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                Dictionary<string, object> node = (Dictionary<string, object>)nodes[i];
                if (node.ContainsKey("matrix"))
                {
                    mesh._isMatrix[i] = true;
                    object[] mat = AsArray(node["matrix"]);
                    for (int k = 0; k < 16; k++)
                        mesh._restMat[i * 16 + k] = Convert.ToSingle(mat[k]);
                }
                else
                {
                    float tx = 0f, ty = 0f, tz = 0f, qx = 0f, qy = 0f, qz = 0f, qw = 1f, sx = 1f, sy = 1f, sz = 1f;
                    Read3(node, "translation", ref tx, ref ty, ref tz);
                    Read4(node, "rotation", ref qx, ref qy, ref qz, ref qw);
                    Read3(node, "scale", ref sx, ref sy, ref sz);
                    mesh._restT[i * 3] = tx;
                    mesh._restT[i * 3 + 1] = ty;
                    mesh._restT[i * 3 + 2] = tz;
                    mesh._restR[i * 4] = qx;
                    mesh._restR[i * 4 + 1] = qy;
                    mesh._restR[i * 4 + 2] = qz;
                    mesh._restR[i * 4 + 3] = qw;
                    mesh._restS[i * 3] = sx;
                    mesh._restS[i * 3 + 1] = sy;
                    mesh._restS[i * 3 + 2] = sz;
                    FromTRS(tx, ty, tz, qx, qy, qz, qw, sx, sy, sz, mesh._scratch);
                    Buffer.BlockCopy(mesh._scratch, 0, mesh._restMat, i * 16 * 4, 64);
                }
                if (!node.ContainsKey("children"))
                    continue;
                object[] ch = AsArray(node["children"]);
                for (int c = 0; c < ch.Length; c++)
                {
                    int child = Convert.ToInt32(ch[c]);
                    mesh._parent[child] = i;
                    kids[i].Add(child);
                }
            }
            int kidTotal = 0;
            for (int i = 0; i < n; i++)
                kidTotal += kids[i].Count;
            mesh._kids = new int[kidTotal];
            int cursor = 0;
            for (int i = 0; i < n; i++)
            {
                mesh._kidAt[i] = cursor;
                mesh._kidN[i] = kids[i].Count;
                for (int c = 0; c < kids[i].Count; c++)
                    mesh._kids[cursor++] = kids[i][c];
            }
            int scene = root.ContainsKey("scene") ? Convert.ToInt32(root["scene"]) : 0;
            object[] scenes = AsArray(root["scenes"]);
            Dictionary<string, object> sc = (Dictionary<string, object>)scenes[scene];
            object[] roots = AsArray(sc["nodes"]);
            mesh._roots = new int[roots.Length];
            for (int i = 0; i < roots.Length; i++)
                mesh._roots[i] = Convert.ToInt32(roots[i]);

            float dur = 0f;
            List<Chan> chans = new List<Chan>();
            if (root.ContainsKey("animations"))
            {
                object[] anims = AsArray(root["animations"]);
                Dictionary<string, object> anim = (Dictionary<string, object>)anims[0];
                object[] channels = AsArray(anim["channels"]);
                object[] samplers = AsArray(anim["samplers"]);
                for (int i = 0; i < channels.Length; i++)
                {
                    Dictionary<string, object> channel = (Dictionary<string, object>)channels[i];
                    Dictionary<string, object> target = (Dictionary<string, object>)channel["target"];
                    string path = Convert.ToString(target["path"]);
                    int pathId = path == "translation" ? 0 : path == "rotation" ? 1 : path == "scale" ? 2 : -1;
                    if (pathId < 0)
                        continue;
                    Dictionary<string, object> sampler = (Dictionary<string, object>)samplers[Convert.ToInt32(channel["sampler"])];
                    Chan chan = new Chan();
                    chan.Node = Convert.ToInt32(target["node"]);
                    chan.Path = pathId;
                    chan.Times = ReadVec(bin, views, accs, Convert.ToInt32(sampler["input"]), 1);
                    chan.Values = ReadVec(bin, views, accs, Convert.ToInt32(sampler["output"]), pathId == 1 ? 4 : 3);
                    if (chan.Times.Length > 0 && chan.Times[chan.Times.Length - 1] > dur)
                        dur = chan.Times[chan.Times.Length - 1];
                    chans.Add(chan);
                }
            }
            mesh._chans = chans.ToArray();
            mesh.ClipTime = dur > 0f ? dur : 1f;
            mesh.Skinned = true;
        }

        void FitClip()
        {
            MinX = MinY = MinZ = float.MaxValue;
            MaxX = MaxY = MaxZ = float.MinValue;
            int steps = 4;
            for (int s = 0; s < steps; s++)
            {
                Pose(ClipTime * s / steps);
                GrowBounds();
            }
            Pose(0f);
            float dx = MaxX - MinX;
            float dy = MaxY - MinY;
            float dz = MaxZ - MinZ;
            LayFlat = dy > dx * 1.8f && dy > dz * 1.8f;
        }

        void GrowBounds()
        {
            for (int i = 0; i < Positions.Length; i += 3)
            {
                float x = Positions[i];
                float y = Positions[i + 1];
                float z = Positions[i + 2];
                if (x < MinX) MinX = x;
                if (y < MinY) MinY = y;
                if (z < MinZ) MinZ = z;
                if (x > MaxX) MaxX = x;
                if (y > MaxY) MaxY = y;
                if (z > MaxZ) MaxZ = z;
            }
        }

        public void Pose(float time)
        {
            if (!Skinned)
                return;
            int n = _parent.Length;
            Buffer.BlockCopy(_restT, 0, _curT, 0, _restT.Length * 4);
            Buffer.BlockCopy(_restR, 0, _curR, 0, _restR.Length * 4);
            Buffer.BlockCopy(_restS, 0, _curS, 0, _restS.Length * 4);
            float t = time;
            if (ClipTime > 0f)
            {
                t = time % ClipTime;
                if (t < 0f)
                    t += ClipTime;
            }
            for (int i = 0; i < _chans.Length; i++)
                Apply(_chans[i], t);
            for (int i = 0; i < _roots.Length; i++)
                Walk(_roots[i], Ident, 0);
            int joints = _jointOf.Length;
            for (int j = 0; j < joints; j++)
                Mul(_global, _jointOf[j] * 16, _ibm, j * 16, _skin, j * 16);
            int count = BindPos.Length / 3;
            float[] pos = Positions;
            for (int v = 0; v < count; v++)
            {
                int p = v * 3;
                int w = v * 4;
                float x = BindPos[p];
                float y = BindPos[p + 1];
                float z = BindPos[p + 2];
                float ox = 0f, oy = 0f, oz = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float weight = _weights[w + k];
                    if (weight == 0f)
                        continue;
                    int joint = _joints[w + k];
                    if (joint >= joints)
                        continue;
                    int m = joint * 16;
                    ox += weight * (_skin[m] * x + _skin[m + 4] * y + _skin[m + 8] * z + _skin[m + 12]);
                    oy += weight * (_skin[m + 1] * x + _skin[m + 5] * y + _skin[m + 9] * z + _skin[m + 13]);
                    oz += weight * (_skin[m + 2] * x + _skin[m + 6] * y + _skin[m + 10] * z + _skin[m + 14]);
                }
                pos[p] = ox;
                pos[p + 1] = oy;
                pos[p + 2] = oz;
            }
        }

        void Walk(int node, float[] parent, int parentAt)
        {
            int dst = node * 16;
            if (_isMatrix[node])
                Mul(parent, parentAt, _restMat, dst, _global, dst);
            else
            {
                int t = node * 3;
                int r = node * 4;
                FromTRS(_curT[t], _curT[t + 1], _curT[t + 2], _curR[r], _curR[r + 1], _curR[r + 2], _curR[r + 3], _curS[t], _curS[t + 1], _curS[t + 2], _scratch);
                Mul(parent, parentAt, _scratch, 0, _global, dst);
            }
            int at = _kidAt[node];
            int kn = _kidN[node];
            for (int i = 0; i < kn; i++)
                Walk(_kids[at + i], _global, dst);
        }

        void Apply(Chan chan, float time)
        {
            float[] times = chan.Times;
            int last = times.Length - 1;
            int comps = chan.Path == 1 ? 4 : 3;
            int node = chan.Node;
            if (last < 0)
                return;
            int hi = last;
            int lo = 0;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            int i1 = lo;
            int i0 = i1 > 0 ? i1 - 1 : 0;
            float span = times[i1] - times[i0];
            float u = span > 1e-8f ? (time - times[i0]) / span : 0f;
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            int a = i0 * comps;
            int b = i1 * comps;
            if (chan.Path == 1)
            {
                float ox, oy, oz, ow;
                Slerp(chan.Values[a], chan.Values[a + 1], chan.Values[a + 2], chan.Values[a + 3], chan.Values[b], chan.Values[b + 1], chan.Values[b + 2], chan.Values[b + 3], u, out ox, out oy, out oz, out ow);
                int r = node * 4;
                _curR[r] = ox;
                _curR[r + 1] = oy;
                _curR[r + 2] = oz;
                _curR[r + 3] = ow;
                return;
            }
            float[] dst = chan.Path == 0 ? _curT : _curS;
            int o = node * 3;
            dst[o] = chan.Values[a] + (chan.Values[b] - chan.Values[a]) * u;
            dst[o + 1] = chan.Values[a + 1] + (chan.Values[b + 1] - chan.Values[a + 1]) * u;
            dst[o + 2] = chan.Values[a + 2] + (chan.Values[b + 2] - chan.Values[a + 2]) * u;
        }

        static ushort[] ReadJoints(byte[] bin, object[] views, object[] accs, int accIndex)
        {
            Dictionary<string, object> acc = (Dictionary<string, object>)accs[accIndex];
            int count = Convert.ToInt32(acc["count"]);
            int offset = AccOffset(views, acc);
            int component = Convert.ToInt32(acc["componentType"]);
            ushort[] data = new ushort[count * 4];
            if (component == 5123)
            {
                Buffer.BlockCopy(bin, offset, data, 0, data.Length * 2);
                return data;
            }
            if (component == 5121)
            {
                for (int i = 0; i < data.Length; i++)
                    data[i] = bin[offset + i];
                return data;
            }
            throw new InvalidOperationException("joint type " + component);
        }

        static void Read3(Dictionary<string, object> node, string key, ref float x, ref float y, ref float z)
        {
            if (!node.ContainsKey(key))
                return;
            object[] v = AsArray(node[key]);
            x = Convert.ToSingle(v[0]);
            y = Convert.ToSingle(v[1]);
            z = Convert.ToSingle(v[2]);
        }

        static void Read4(Dictionary<string, object> node, string key, ref float x, ref float y, ref float z, ref float w)
        {
            if (!node.ContainsKey(key))
                return;
            object[] v = AsArray(node[key]);
            x = Convert.ToSingle(v[0]);
            y = Convert.ToSingle(v[1]);
            z = Convert.ToSingle(v[2]);
            w = Convert.ToSingle(v[3]);
        }

        static readonly float[] Ident = new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f };

        static void FromTRS(float tx, float ty, float tz, float qx, float qy, float qz, float qw, float sx, float sy, float sz, float[] m)
        {
            float x2 = qx + qx, y2 = qy + qy, z2 = qz + qz;
            float xx = qx * x2, xy = qx * y2, xz = qx * z2;
            float yy = qy * y2, yz = qy * z2, zz = qz * z2;
            float wx = qw * x2, wy = qw * y2, wz = qw * z2;
            m[0] = (1f - (yy + zz)) * sx;
            m[1] = (xy + wz) * sx;
            m[2] = (xz - wy) * sx;
            m[3] = 0f;
            m[4] = (xy - wz) * sy;
            m[5] = (1f - (xx + zz)) * sy;
            m[6] = (yz + wx) * sy;
            m[7] = 0f;
            m[8] = (xz + wy) * sz;
            m[9] = (yz - wx) * sz;
            m[10] = (1f - (xx + yy)) * sz;
            m[11] = 0f;
            m[12] = tx;
            m[13] = ty;
            m[14] = tz;
            m[15] = 1f;
        }

        static void Mul(float[] a, int ao, float[] b, int bo, float[] c, int co)
        {
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++)
                        s += a[ao + k * 4 + row] * b[bo + col * 4 + k];
                    c[co + col * 4 + row] = s;
                }
            }
        }

        static void Slerp(float ax, float ay, float az, float aw, float bx, float by, float bz, float bw, float t, out float ox, out float oy, out float oz, out float ow)
        {
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
                dot = -dot;
            }
            float w0, w1;
            if (dot > 0.9995f)
            {
                w0 = 1f - t;
                w1 = t;
            }
            else
            {
                float theta = (float)Math.Acos(dot);
                float s = (float)Math.Sin(theta);
                w0 = (float)Math.Sin((1f - t) * theta) / s;
                w1 = (float)Math.Sin(t * theta) / s;
            }
            ox = ax * w0 + bx * w1;
            oy = ay * w0 + by * w1;
            oz = az * w0 + bz * w1;
            ow = aw * w0 + bw * w1;
            float len = (float)Math.Sqrt(ox * ox + oy * oy + oz * oz + ow * ow);
            if (len > 1e-8f)
            {
                ox /= len;
                oy /= len;
                oz /= len;
                ow /= len;
            }
        }
    }

    // Hidden GL window. Skinned GLBs play their clip on the CPU. Draw is unlit so the base color is not shaded into patches.
    sealed class GlAvatar : IDisposable
    {
        const int GL_TRIANGLES = 0x0004;
        const int GL_UNSIGNED_INT = 0x1405;
        const int GL_FLOAT = 0x1406;
        const int GL_RGBA = 0x1908;
        const int GL_UNSIGNED_BYTE = 0x1401;
        const int GL_DEPTH_TEST = 0x0B71;
        const int GL_BLEND = 0x0BE2;
        const int GL_SRC_ALPHA = 0x0302;
        const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        const int GL_TEXTURE_2D = 0x0DE1;
        const int GL_LIGHTING = 0x0B50;
        const int GL_CULL_FACE = 0x0B44;
        const int GL_LIGHT0 = 0x4000;
        const int GL_COLOR_MATERIAL = 0x0B57;
        const int GL_FRONT_AND_BACK = 0x0408;
        const int GL_AMBIENT_AND_DIFFUSE = 0x1602;
        const int GL_POSITION = 0x1203;
        const int GL_DIFFUSE = 0x1201;
        const int GL_AMBIENT = 0x1200;
        const int GL_NORMALIZE = 0x0BA1;
        const int GL_COLOR_BUFFER_BIT = 0x4000;
        const int GL_DEPTH_BUFFER_BIT = 0x0100;
        const int GL_MODELVIEW = 0x1700;
        const int GL_PROJECTION = 0x1701;
        const int GL_TEXTURE_MIN_FILTER = 0x2801;
        const int GL_TEXTURE_MAG_FILTER = 0x2800;
        const int GL_LINEAR = 0x2601;
        const int GL_CLAMP = 0x2900;
        const int GL_TEXTURE_WRAP_S = 0x2802;
        const int GL_TEXTURE_WRAP_T = 0x2803;
        const int GL_UNPACK_ALIGNMENT = 0x0CF5;
        const int GL_PACK_ALIGNMENT = 0x0D05;
        const int GL_LIGHT_MODEL_AMBIENT = 0x0B53;
        const int GL_LIGHT_MODEL_TWO_SIDE = 0x0B52;
        const uint PFD_DRAW_TO_WINDOW = 4;
        const uint PFD_SUPPORT_OPENGL = 32;
        const byte PFD_TYPE_RGBA = 0;
        const byte PFD_MAIN_PLANE = 0;
        const int CS_OWNDC = 0x0020;
        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int SW_SHOWNA = 8;

        public readonly int Width;
        public readonly int Height;
        public byte[] Alpha;

        readonly GlbMesh _mesh;
        readonly float _scale;
        readonly System.Diagnostics.Stopwatch _anim = System.Diagnostics.Stopwatch.StartNew();
        readonly byte[] _rgba;
        readonly byte[] _dst;
        readonly float[] _shadow = new float[17 * 3];
        GCHandle _posPin, _nrmPin, _uvPin, _idxPin, _texPin, _rgbaPin;
        IntPtr _hwnd, _dc, _rc;
        uint _tex;
        uint _fbo;
        uint _color;
        uint _depth;
        bool _alive;
        GlGen _genFbo;
        GlBind _bindFbo;
        GlTex2D _fbTex;
        GlGen _genRb;
        GlBind _bindRb;
        GlStorage _rbStorage;
        GlFbRb _fbRb;
        GlStatus _fbStatus;

        delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        // Class stays registered after the first avatar. Instance delegate would be collected on switch.
        static readonly WndProc ClassProc = DefWindowProc;
        delegate void GlGen(int n, ref uint id);
        delegate void GlBind(int target, uint id);
        delegate void GlTex2D(int target, int attachment, int textarget, uint texture, int level);
        delegate void GlStorage(int target, int internalFormat, int w, int h);
        delegate void GlFbRb(int target, int attachment, int rbTarget, uint rb);
        delegate int GlStatus(int target);

        public GlAvatar(GlbMesh mesh, float scale, int width, int height)
        {
            _mesh = mesh;
            _scale = scale;
            Width = width;
            Height = height;
            Alpha = new byte[width * height];
            _rgba = new byte[width * height * 4];
            _dst = new byte[width * height * 4];
            _posPin = GCHandle.Alloc(mesh.Positions, GCHandleType.Pinned);
            _nrmPin = GCHandle.Alloc(mesh.Normals, GCHandleType.Pinned);
            _uvPin = GCHandle.Alloc(mesh.Uvs, GCHandleType.Pinned);
            _idxPin = GCHandle.Alloc(mesh.Indices, GCHandleType.Pinned);
            _texPin = GCHandle.Alloc(mesh.Rgba, GCHandleType.Pinned);
            _rgbaPin = GCHandle.Alloc(_rgba, GCHandleType.Pinned);
            CreateGl();
            _alive = true;
        }

        public void Dispose()
        {
            if (!_alive)
                return;
            _alive = false;
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            if (_rc != IntPtr.Zero)
                wglDeleteContext(_rc);
            if (_dc != IntPtr.Zero)
                ReleaseDC(_hwnd, _dc);
            if (_hwnd != IntPtr.Zero)
                DestroyWindow(_hwnd);
            Free(_posPin);
            Free(_nrmPin);
            Free(_uvPin);
            Free(_idxPin);
            Free(_texPin);
            Free(_rgbaPin);
        }

        static void Free(GCHandle h)
        {
            if (h.IsAllocated)
                h.Free();
        }

        public void Render(IntPtr dibBits, float yaw, float pitch, float roll, float lift01)
        {
            if (_mesh.Skinned)
            {
                _mesh.Pose((float)_anim.Elapsed.TotalSeconds);
                pitch = 0f;
                roll = 0f;
                lift01 = 0f;
            }
            // ponytail: skip frame if the GL context died (sleep/TDR). Recreate GlAvatar if the pet stays frozen after wake.
            if (!wglMakeCurrent(_dc, _rc))
                return;
            _bindFbo(0x8D40, _fbo);
            glViewport(0, 0, Width, Height);
            glClearColor(0f, 0f, 0f, 0f);
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            float aspect = (float)Width / Height;
            glMatrixMode(GL_PROJECTION);
            glLoadIdentity();
            Perspective(26f, aspect, 0.05f, 40f);

            float ex = (_mesh.MaxX - _mesh.MinX) * _scale;
            float ey = (_mesh.MaxY - _mesh.MinY) * _scale;
            float ez = (_mesh.MaxZ - _mesh.MinZ) * _scale;
            float ext = ex;
            if (ey > ext) ext = ey;
            if (ez > ext) ext = ez;
            float dist = (ext * 0.72f) / (float)Math.Tan(26.0 * Math.PI / 360.0);
            float lookY = _mesh.LayFlat ? 0f : ey * 0.5f;
            float lift = lift01 * ey * 0.22f;
            if (lift < 0f)
                lift = 0f;

            glMatrixMode(GL_MODELVIEW);
            glLoadIdentity();
            glTranslatef(0f, -lookY, -dist);
            glRotatef(yaw, 0f, 1f, 0f);

            glDisable(GL_LIGHTING);
            glDisable(GL_TEXTURE_2D);
            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glDepthMask(0);
            float shadowA = 0.28f * (1f - Math.Min(1f, lift01));
            glColor4f(0f, 0f, 0f, shadowA);
            float rad = Math.Max(_mesh.MaxX - _mesh.MinX, _mesh.MaxZ - _mesh.MinZ) * 0.42f * _scale;
            DrawShadow(rad);

            glDepthMask(1);
            glPushMatrix();
            glTranslatef(0f, lift, 0f);
            glRotatef(pitch, 1f, 0f, 0f);
            glRotatef(roll, 0f, 0f, 1f);
            glScalef(_scale, _scale, _scale);
            if (_mesh.LayFlat)
            {
                glTranslatef(-(_mesh.MinX + _mesh.MaxX) * 0.5f, -(_mesh.MinY + _mesh.MaxY) * 0.5f, -(_mesh.MinZ + _mesh.MaxZ) * 0.5f);
                glRotatef(-90f, 0f, 0f, 1f);
            }
            else
                glTranslatef(-(_mesh.MinX + _mesh.MaxX) * 0.5f, -_mesh.MinY, -(_mesh.MinZ + _mesh.MaxZ) * 0.5f);

            glEnable(GL_DEPTH_TEST);
            glDisable(GL_LIGHTING);
            glEnable(GL_TEXTURE_2D);
            glTexEnvi(0x2300, 0x2200, 0x1E01);
            glBindTexture(GL_TEXTURE_2D, _tex);
            glColor4f(1f, 1f, 1f, 1f);
            glEnableClientState(0x8074);
            glEnableClientState(0x8078);
            glVertexPointer(3, GL_FLOAT, 0, _posPin.AddrOfPinnedObject());
            glTexCoordPointer(2, GL_FLOAT, 0, _uvPin.AddrOfPinnedObject());
            glDrawElements(GL_TRIANGLES, _mesh.Indices.Length, GL_UNSIGNED_INT, _idxPin.AddrOfPinnedObject());
            glDisableClientState(0x8078);
            glDisableClientState(0x8074);
            glPopMatrix();

            glReadPixels(0, 0, Width, Height, GL_RGBA, GL_UNSIGNED_BYTE, _rgbaPin.AddrOfPinnedObject());
            _bindFbo(0x8D40, 0);
            CopyToDib(dibBits);
        }

        void DrawShadow(float rad)
        {
            _shadow[0] = 0f;
            _shadow[1] = 0.01f;
            _shadow[2] = 0f;
            for (int i = 0; i <= 15; i++)
            {
                double a = i / 15.0 * Math.PI * 2.0;
                _shadow[(i + 1) * 3] = (float)Math.Cos(a) * rad;
                _shadow[(i + 1) * 3 + 1] = 0.01f;
                _shadow[(i + 1) * 3 + 2] = (float)Math.Sin(a) * rad;
            }
            glBegin(6);
            for (int i = 0; i < 17; i++)
                glVertex3f(_shadow[i * 3], _shadow[i * 3 + 1], _shadow[i * 3 + 2]);
            glEnd();
        }

        void CopyToDib(IntPtr dibBits)
        {
            int w = Width;
            int h = Height;
            int n = w * h;
            byte[] src = _rgba;
            byte[] alpha = Alpha;
            byte[] dst = _dst;
            for (int y = 0; y < h; y++)
            {
                int srcRow = (h - 1 - y) * w;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int s = (srcRow + x) * 4;
                    byte r = src[s];
                    byte g = src[s + 1];
                    byte b = src[s + 2];
                    byte a = src[s + 3];
                    int p = dstRow + x;
                    alpha[p] = a;
                    int d = p * 4;
                    dst[d] = (byte)(b * a / 255);
                    dst[d + 1] = (byte)(g * a / 255);
                    dst[d + 2] = (byte)(r * a / 255);
                    dst[d + 3] = a;
                }
            }
            Marshal.Copy(dst, 0, dibBits, dst.Length);
        }

        void CreateGl()
        {
            WNDCLASS wc = new WNDCLASS();
            wc.style = CS_OWNDC;
            wc.lpfnWndProc = ClassProc;
            wc.hInstance = GetModuleHandle(null);
            wc.lpszClassName = "MascotGL";
            ushort atom = RegisterClassW(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new InvalidOperationException("RegisterClass " + Marshal.GetLastWin32Error());

            _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "MascotGL", "", WS_POPUP, -16000, -16000, Width, Height, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("CreateWindow " + Marshal.GetLastWin32Error());
            ShowWindow(_hwnd, SW_SHOWNA);
            _dc = GetDC(_hwnd);

            PIXELFORMATDESCRIPTOR pfd = new PIXELFORMATDESCRIPTOR();
            int format = PickFormat(_dc, ref pfd);
            if (!SetPixelFormat(_dc, format, ref pfd))
                throw new InvalidOperationException("SetPixelFormat " + Marshal.GetLastWin32Error());
            _rc = wglCreateContext(_dc);
            if (_rc == IntPtr.Zero || !wglMakeCurrent(_dc, _rc))
                throw new InvalidOperationException("wglCreateContext " + Marshal.GetLastWin32Error());

            glEnable(GL_DEPTH_TEST);
            glDisable(GL_LIGHTING);
            glDisable(GL_CULL_FACE);
            glEnable(GL_TEXTURE_2D);
            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glColorMaterial(GL_FRONT_AND_BACK, GL_AMBIENT_AND_DIFFUSE);
            glLightModeli(GL_LIGHT_MODEL_TWO_SIDE, 1);
            float[] amb = new float[] { 0.55f, 0.55f, 0.55f, 1f };
            float[] diff = new float[] { 0.9f, 0.9f, 0.9f, 1f };
            float[] pos = new float[] { 0.35f, 0.8f, 0.6f, 0f };
            glLightModelfv(GL_LIGHT_MODEL_AMBIENT, amb);
            glLightfv(GL_LIGHT0, GL_AMBIENT, amb);
            glLightfv(GL_LIGHT0, GL_DIFFUSE, diff);
            glLightfv(GL_LIGHT0, GL_POSITION, pos);
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            glPixelStorei(GL_PACK_ALIGNMENT, 1);

            uint tex = 0;
            glGenTextures(1, ref tex);
            _tex = tex;
            glBindTexture(GL_TEXTURE_2D, _tex);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, 0x812F);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, 0x812F);
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, _mesh.TexW, _mesh.TexH, 0, GL_RGBA, GL_UNSIGNED_BYTE, _texPin.AddrOfPinnedObject());

            int err = glGetError();
            if (err != 0)
                throw new InvalidOperationException("GL error " + err);
            CreateTarget();
            ShowWindow(_hwnd, 0);
        }

        void CreateTarget()
        {
            _genFbo = (GlGen)LoadProc("glGenFramebuffers", typeof(GlGen));
            _bindFbo = (GlBind)LoadProc("glBindFramebuffer", typeof(GlBind));
            _fbTex = (GlTex2D)LoadProc("glFramebufferTexture2D", typeof(GlTex2D));
            _genRb = (GlGen)LoadProc("glGenRenderbuffers", typeof(GlGen));
            _bindRb = (GlBind)LoadProc("glBindRenderbuffer", typeof(GlBind));
            _rbStorage = (GlStorage)LoadProc("glRenderbufferStorage", typeof(GlStorage));
            _fbRb = (GlFbRb)LoadProc("glFramebufferRenderbuffer", typeof(GlFbRb));
            _fbStatus = (GlStatus)LoadProc("glCheckFramebufferStatus", typeof(GlStatus));

            uint color = 0;
            glGenTextures(1, ref color);
            _color = color;
            glBindTexture(GL_TEXTURE_2D, _color);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, Width, Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);

            uint fbo = 0;
            _genFbo(1, ref fbo);
            _fbo = fbo;
            _bindFbo(0x8D40, _fbo);
            _fbTex(0x8D40, 0x8CE0, GL_TEXTURE_2D, _color, 0);

            uint depth = 0;
            _genRb(1, ref depth);
            _depth = depth;
            _bindRb(0x8D41, _depth);
            _rbStorage(0x8D41, 0x81A6, Width, Height);
            _fbRb(0x8D40, 0x8D00, 0x8D41, _depth);

            int status = _fbStatus(0x8D40);
            if (status != 0x8CD5)
                throw new InvalidOperationException("FBO status " + status);
            glBindTexture(GL_TEXTURE_2D, _tex);
        }

        static string GlString(int name)
        {
            IntPtr p = glGetString(name);
            if (p == IntPtr.Zero)
                return "?";
            return Marshal.PtrToStringAnsi(p);
        }

        object LoadProc(string name, Type type)
        {
            IntPtr p = wglGetProcAddress(name);
            if (p == IntPtr.Zero || p.ToInt64() < 4)
                p = wglGetProcAddress(name + "EXT");
            if (p == IntPtr.Zero || p.ToInt64() < 4)
                throw new InvalidOperationException("missing " + name + " (" + GlString(0x1F02) + " / " + GlString(0x1F01) + ")");
            return Marshal.GetDelegateForFunctionPointer(p, type);
        }

        static int PickFormat(IntPtr dc, ref PIXELFORMATDESCRIPTOR chosen)
        {
            PIXELFORMATDESCRIPTOR probe = new PIXELFORMATDESCRIPTOR();
            int bytes = Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR));
            probe.nSize = (short)bytes;
            probe.nVersion = 1;
            int max = DescribePixelFormat(dc, 1, bytes, ref probe);
            int best = 0;
            int bestScore = -1;
            for (int i = 1; i <= max; i++)
            {
                PIXELFORMATDESCRIPTOR p = new PIXELFORMATDESCRIPTOR();
                p.nSize = (short)bytes;
                p.nVersion = 1;
                DescribePixelFormat(dc, i, bytes, ref p);
                if ((p.dwFlags & PFD_SUPPORT_OPENGL) == 0 || (p.dwFlags & PFD_DRAW_TO_WINDOW) == 0)
                    continue;
                if ((p.dwFlags & 0x40) != 0)
                    continue;
                if (p.iPixelType != PFD_TYPE_RGBA || p.cColorBits < 24 || p.cDepthBits < 16)
                    continue;
                int score = p.cColorBits + p.cDepthBits * 2 + p.cAlphaBits;
                if ((p.dwFlags & 1) != 0)
                    score -= 5;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                    chosen = p;
                }
            }
            if (best == 0)
                throw new InvalidOperationException("No OpenGL pixel format");
            return best;
        }

        static void Perspective(float fovDeg, float aspect, float near, float far)
        {
            float f = 1f / (float)Math.Tan(fovDeg * (Math.PI / 360.0));
            float[] m = new float[16];
            m[0] = f / aspect;
            m[5] = f;
            m[10] = (far + near) / (near - far);
            m[11] = -1f;
            m[14] = (2f * far * near) / (near - far);
            glLoadMatrixf(m);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASS
        {
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PIXELFORMATDESCRIPTOR
        {
            public short nSize;
            public short nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits;
            public byte cRedShift;
            public byte cGreenBits;
            public byte cGreenShift;
            public byte cBlueBits;
            public byte cBlueShift;
            public byte cAlphaBits;
            public byte cAlphaShift;
            public byte cAccumBits;
            public byte cAccumRedBits;
            public byte cAccumGreenBits;
            public byte cAccumBlueBits;
            public byte cAccumAlphaBits;
            public byte cDepthBits;
            public byte cStencilBits;
            public byte cAuxBuffers;
            public byte iLayerType;
            public byte bReserved;
            public uint dwLayerMask;
            public uint dwVisibleMask;
            public uint dwDamageMask;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern int DescribePixelFormat(IntPtr hdc, int format, int bytes, ref PIXELFORMATDESCRIPTOR pfd);

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);

        [DllImport("opengl32.dll", SetLastError = true)]
        static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll")]
        static extern IntPtr wglGetProcAddress(string name);

        [DllImport("opengl32.dll")]
        static extern bool wglMakeCurrent(IntPtr hdc, IntPtr rc);

        [DllImport("opengl32.dll")]
        static extern bool wglDeleteContext(IntPtr rc);

        [DllImport("opengl32.dll")]
        static extern void glViewport(int x, int y, int w, int h);
        [DllImport("opengl32.dll")]
        static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")]
        static extern void glClear(int mask);
        [DllImport("opengl32.dll")]
        static extern void glEnable(int cap);
        [DllImport("opengl32.dll")]
        static extern void glDisable(int cap);
        [DllImport("opengl32.dll")]
        static extern void glBlendFunc(int s, int d);
        [DllImport("opengl32.dll")]
        static extern void glMatrixMode(int mode);
        [DllImport("opengl32.dll")]
        static extern void glLoadIdentity();
        [DllImport("opengl32.dll")]
        static extern void glLoadMatrixf(float[] m);
        [DllImport("opengl32.dll")]
        static extern void glTranslatef(float x, float y, float z);
        [DllImport("opengl32.dll")]
        static extern void glRotatef(float angle, float x, float y, float z);
        [DllImport("opengl32.dll")]
        static extern void glScalef(float x, float y, float z);
        [DllImport("opengl32.dll")]
        static extern void glPushMatrix();
        [DllImport("opengl32.dll")]
        static extern void glPopMatrix();
        [DllImport("opengl32.dll")]
        static extern void glColor4f(float r, float g, float b, float a);
        [DllImport("opengl32.dll")]
        static extern void glTexEnvi(int target, int pname, int param);
        [DllImport("opengl32.dll")]
        static extern void glColorMaterial(int face, int mode);
        [DllImport("opengl32.dll")]
        static extern void glLightfv(int light, int pname, float[] p);
        [DllImport("opengl32.dll")]
        static extern void glLightModelfv(int pname, float[] p);
        [DllImport("opengl32.dll")]
        static extern void glLightModeli(int pname, int param);
        [DllImport("opengl32.dll")]
        static extern void glGenTextures(int n, ref uint textures);
        [DllImport("opengl32.dll")]
        static extern void glBindTexture(int target, uint texture);
        [DllImport("opengl32.dll")]
        static extern void glTexParameteri(int target, int pname, int param);
        [DllImport("opengl32.dll")]
        static extern void glTexImage2D(int target, int level, int internalFormat, int w, int h, int border, int format, int type, IntPtr data);
        [DllImport("opengl32.dll")]
        static extern void glPixelStorei(int pname, int param);
        [DllImport("opengl32.dll")]
        static extern void glEnableClientState(int cap);
        [DllImport("opengl32.dll")]
        static extern void glDisableClientState(int cap);
        [DllImport("opengl32.dll")]
        static extern void glVertexPointer(int size, int type, int stride, IntPtr pointer);
        [DllImport("opengl32.dll")]
        static extern void glNormalPointer(int type, int stride, IntPtr pointer);
        [DllImport("opengl32.dll")]
        static extern void glTexCoordPointer(int size, int type, int stride, IntPtr pointer);
        [DllImport("opengl32.dll")]
        static extern void glDrawElements(int mode, int count, int type, IntPtr indices);
        [DllImport("opengl32.dll")]
        static extern void glReadPixels(int x, int y, int w, int h, int format, int type, IntPtr pixels);
        [DllImport("opengl32.dll")]
        static extern void glBegin(int mode);
        [DllImport("opengl32.dll")]
        static extern void glEnd();
        [DllImport("opengl32.dll")]
        static extern void glVertex3f(float x, float y, float z);
        [DllImport("opengl32.dll")]
        static extern void glDepthMask(int flag);
        [DllImport("opengl32.dll")]
        static extern int glGetError();

        [DllImport("opengl32.dll")]
        static extern IntPtr glGetString(int name);
    }
}
