// HCeluSavienoshanaWinForms.cs
// Windows Forms versija HCeluSavienoshana.html rīkam - pilna vizuāla
// lietotne (sānjosla ar vadīklām + zīmēšanas laukums), nevis konsole.
// VISA loģika (virzienu konvencija, F1-F9 transformācijas, .bin bitu
// iepakošana, savienojuma derīguma pārbaude) ir IDENTISKA oriģinālajam
// HTML/JS un iepriekšējai konsoles versijai (HCeluSavienoshana.cs).
//
// SVARĪGI: šis rīks izmanto SAVU PAŠA virzienu ciparu konvenciju (skat.
// Moves masīvu zemāk) - tā ATŠĶIRAS no citur šajā projektā izmantotās
// (zirgaGaljumsGen.cs u.c.) konvencijas - TĪŠI, lai faili paliktu
// saderīgi ar oriģinālo HTML rīku.
//
// PROJEKTA IZVEIDE VISUAL STUDIO:
//   Fails -> Jauns -> Projekts -> "Windows Forms App (.NET Framework)"
//   Izdzēs noklusējuma Form1.cs un Form1.Designer.cs (un Program.cs, ja
//   grib - šis fails jau satur savu Main()).
//   Pievieno šo failu projektam.
//   Palaišana: F5.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HCeluSavienoshana
{
    // ================== TĪRĀ LOĢIKA (bez UI) ==================
    // Identiska konsoles versijai (HCeluSavienoshana.cs) - pārbaudīta pret
    // JS atsauci (visas 9 F1-F9 transformācijas un .bin iepakošana/
    // atpakošana precīzi sakrīt).
    static class HcLogic
    {
        public static readonly (int dx, int dy)[] Moves =
        {
            (1, -2), (2, -1), (2, 1), (1, 2), (-1, 2), (-2, 1), (-2, -1), (-1, -2)
        };

        public static (int dx, int dy) MoveFor(int digit) => Moves[digit - 1];

        public static int? GetKnightMoveDigit(float x1, float y1, float x2, float y2)
        {
            int dx = (int)Math.Round(x2 - x1), dy = (int)Math.Round(y2 - y1);
            for (int i = 0; i < 8; i++)
                if (Moves[i].dx == dx && Moves[i].dy == dy)
                    return i + 1;
            return null;
        }

        public static List<PointF> MovesToPoints(List<int> data, float startX = 0, float startY = 0)
        {
            var pts = new List<PointF> { new PointF(startX, startY) };
            float cx = startX, cy = startY;
            foreach (var v in data)
            {
                var (dx, dy) = MoveFor(v);
                cx += dx; cy += dy;
                pts.Add(new PointF(cx, cy));
            }
            return pts;
        }

        // "Rotē" gājienu virkni, lai tā SĀKTOS pie breakIdx (nevis pie
        // faila oriģinālā sākuma) - VIENKĀRŠI PĀRKĀRTOJOT gājienu ciparus
        // (tāpat kā F1-F9 transformācijas - nekas netiek atmests, nekāda
        // "slēgtas cilpas" prasība NAV vajadzīga). Rezultāts VIENMĒR ir
        // ģeometriski derīgs ceļš (katrs atsevišķais cipars PATS PAR SEVI
        // vienmēr ir derīgs zirdziņa gājiens, neatkarīgi no secības) -
        // vienkārši tas var apmeklēt CITAS šūnas nekā oriģinālā secība.
        // Vai jaunais sākums/beigas savienojas ar 1. ceļu - to jau parasti
        // pārbauda UpdateStatus()/BuildCombined() - šeit nekas papildu
        // nav jāpārbauda.
        // Rada ĪSTU divu-galu "spraugu" 2. ceļā - TIEŠI TĀPAT, kā 1. ceļa
        // "Lūzuma punkts" izņem VIENU gājienu (malu) starp #M un #M+1,
        // radot divus PATIESI atšķirīgus galapunktus (nevis tikai pārkārto
        // VISUS gājienus, kas cikliskam ceļam vienmēr atgrieztos pie sevis
        // - tā bija manas iepriekšējās versijas kļūda). Rezultāts satur
        // VISUS pārējos N-1 gājienus (nekas no PAŠĀM ŠŪNĀM netiek atmests -
        // tikai VIENA mala/savienojums starp divām blakusesošām šūnām).
        public static List<int> RotateAtIndex(List<int> data, int breakIdx)
        {
            if (breakIdx <= 0 || breakIdx >= data.Count) return data;
            var rotated = new List<int>();
            rotated.AddRange(data.Skip(breakIdx + 1)); // SIC: sākam PĒC izņemtā gājiena
            rotated.AddRange(data.Take(breakIdx));      // ... un beidzam TIEŠI PIRMS tā
            return rotated;
        }

        public static List<int> ParseHrzText(string text)
        {
            var result = new List<int>();
            foreach (char ch in text)
                if (ch >= '1' && ch <= '8')
                    result.Add(ch - '0');
            return result;
        }

        public static List<int> ParseBin(byte[] bytes)
        {
            var resData = new List<int>();
            for (int i = 0; i + 2 < bytes.Length; i += 3)
            {
                int val = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
                for (int j = 7; j >= 0; j--)
                {
                    int bits = (val >> (j * 3)) & 0x7;
                    resData.Add(bits == 0 ? 8 : bits);
                }
            }
            for (int i = 1; i < resData.Count; i++)
            {
                int prev = resData[i - 1];
                int curr = resData[i];
                int opposite = prev > 4 ? prev - 4 : prev + 4;
                if (curr == opposite)
                {
                    resData = resData.Take(i).ToList();
                    break;
                }
            }
            return resData;
        }

        public static List<int> LoadPathFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".bin")
                return ParseBin(File.ReadAllBytes(path));
            return ParseHrzText(File.ReadAllText(path));
        }

        static readonly Dictionary<int, int> F1 = new Dictionary<int, int> { { 1, 5 }, { 2, 4 }, { 3, 3 }, { 4, 2 }, { 5, 1 }, { 6, 8 }, { 7, 7 }, { 8, 6 } };
        static readonly Dictionary<int, int> F6 = new Dictionary<int, int> { { 1, 1 }, { 2, 8 }, { 3, 7 }, { 4, 6 }, { 5, 5 }, { 6, 4 }, { 7, 3 }, { 8, 2 } };
        static readonly Dictionary<int, int> F7 = new Dictionary<int, int> { { 1, 7 }, { 2, 6 }, { 3, 5 }, { 4, 4 }, { 5, 3 }, { 6, 2 }, { 7, 1 }, { 8, 8 } };
        static readonly Dictionary<int, int> F8 = new Dictionary<int, int> { { 1, 3 }, { 2, 2 }, { 3, 1 }, { 4, 8 }, { 5, 7 }, { 6, 6 }, { 7, 5 }, { 8, 4 } };

        public static List<int> TransformData2(List<int> data2, int mode)
        {
            if (data2.Count == 0) return data2;
            List<int> newData;
            if (mode == 5)
            {
                newData = new List<int>(data2);
                newData.Reverse();
                return newData;
            }
            if (mode == 9)
            {
                newData = new List<int>();
                foreach (var v in data2)
                {
                    int d = v + 4;
                    if (d > 8) d -= 8;
                    newData.Add(d);
                }
                newData.Reverse();
                return newData;
            }
            newData = new List<int>();
            foreach (var v in data2)
            {
                int d;
                switch (mode)
                {
                    case 1: d = F1[v] + 1; break;
                    case 2: d = v + 2; break;
                    case 3: d = v + 4; break;
                    case 4: d = v + 6; break;
                    case 6: d = F6[v] + 1; break;
                    case 7: d = F7[v] + 1; break;
                    case 8: d = F8[v] + 1; break;
                    default: d = v; break;
                }
                if (d > 8) d -= 8;
                newData.Add(d);
            }
            return newData;
        }

        public static void SaveHrz(List<int> combined, string path)
        {
            File.WriteAllText(path, string.Concat(combined.Select(d => d.ToString())));
        }

        public static void SaveBin(List<int> combined, string path)
        {
            var dataToSave = new List<int>(combined);
            if (dataToSave.Count % 8 != 0)
            {
                int lastMove = dataToSave[dataToSave.Count - 1];
                int oppositeMove = lastMove > 4 ? lastMove - 4 : lastMove + 4;
                dataToSave.Add(oppositeMove);
            }
            int blocksCount = (int)Math.Ceiling(dataToSave.Count / 8.0);
            var buffer = new byte[blocksCount * 3];
            for (int i = 0; i < blocksCount; i++)
            {
                int val = 0;
                for (int k = 0; k < 8; k++)
                {
                    int moveIndex = i * 8 + k;
                    int move = 8;
                    if (moveIndex < dataToSave.Count) move = dataToSave[moveIndex];
                    int bits = (move == 8) ? 0 : move;
                    val |= (bits << ((7 - k) * 3));
                }
                buffer[i * 3] = (byte)((val >> 16) & 0xFF);
                buffer[i * 3 + 1] = (byte)((val >> 8) & 0xFF);
                buffer[i * 3 + 2] = (byte)(val & 0xFF);
            }
            File.WriteAllBytes(path, buffer);
        }
    }

    // ================== GALVENĀ FORMA ==================
    public class MainForm : Form
    {
        // --- stāvoklis ---
        List<int> data1 = new List<int>();
        List<int> data2 = new List<int>();
        List<int> originalData2 = new List<int>();
        List<PointF> pts1 = new List<PointF>();
        List<PointF> pts2 = new List<PointF>();
        int scale = 12;
        bool isAutoCenter = true;

        // --- UI vadīklas ---
        Panel sidebar, canvasHost;
        Label fileInfo1, fileInfo2;
        NumericUpDown splitIndex1, splitIndex2, offsetX2, offsetY2, gridSize, centerX, centerY;
        Label splitInfo2;
        List<int> data2Effective = new List<int>();
        Label statusBox;

        static readonly Color BgDark = Color.FromArgb(0x1e, 0x1e, 0x1e);
        static readonly Color BgSidebar = Color.FromArgb(0x2d, 0x2d, 0x30);
        static readonly Color BgGroup = Color.FromArgb(0x3e, 0x3e, 0x42);
        static readonly Color FgText = Color.FromArgb(0xd4, 0xd4, 0xd4);
        static readonly Color ColBlue = Color.FromArgb(0x00, 0x7a, 0xcc);
        static readonly Color ColRed = Color.FromArgb(0xe7, 0x4c, 0x3c);
        static readonly Color ColGreen = Color.FromArgb(0x27, 0xae, 0x60);
        static readonly Color ColYellow = Color.FromArgb(0xd3, 0x54, 0x00);
        static readonly Color ColLightBlue = Color.FromArgb(0x34, 0x98, 0xdb);
        static readonly Color StatusOk = Color.FromArgb(0x27, 0xae, 0x60);
        static readonly Color StatusBad = Color.FromArgb(0xc0, 0x39, 0x2b);

        public MainForm()
        {
            Text = "Hamiltona Ciklu Apvienotājs";
            Width = 1400;
            Height = 900;
            BackColor = BgDark;
            ForeColor = FgText;
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            UpdateStatus();
        }

        // ---------- UI izveide (bez Designer faila - viss kodā) ----------

        // Izveido 4 mazas +/- pogas ap doto skaitlisko lodziņu (-100, -10,
        // ... [lodziņš] ..., +10, +100) - pats lodziņš tiek PĀRVIETOTS starp
        // tām (tā Location.X tiek pārrakstīts). Atgriež X pozīciju TIEŠI
        // pēc pēdējās (+100) pogas, lai zvanītājs zina, kur novietot nākamo
        // vadīklu šajā pašā rindā.
        int MakeStepperRow(Control.ControlCollection parentControls, NumericUpDown field, int startX, int y)
        {
            int x = startX;
            var bMinus100 = MakeButton("-100", ColBlue, (s, e) => { field.Value = Clamp(field, field.Value - 100); }, x, y, 44, 24);
            parentControls.Add(bMinus100); x += 48;
            var bMinus10 = MakeButton("-10", ColBlue, (s, e) => { field.Value = Clamp(field, field.Value - 10); }, x, y, 38, 24);
            parentControls.Add(bMinus10); x += 42;
            field.Location = new Point(x, field.Location.Y);
            x += field.Width + 4;
            var bPlus10 = MakeButton("+10", ColBlue, (s, e) => { field.Value = Clamp(field, field.Value + 10); }, x, y, 38, 24);
            parentControls.Add(bPlus10); x += 42;
            var bPlus100 = MakeButton("+100", ColBlue, (s, e) => { field.Value = Clamp(field, field.Value + 100); }, x, y, 44, 24);
            parentControls.Add(bPlus100); x += 48;
            return x;
        }

        Button MakeButton(string text, Color color, EventHandler onClick, int x, int y, int width, int height = 26)
        {
            var b = new Button
            {
                Text = text,
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(x, y),
                Size = new Size(width, height)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            return b;
        }

        Label MakeLabel(string text, int x, int y, int width = 0, int height = 0)
        {
            var l = new Label { Text = text, ForeColor = FgText, Location = new Point(x, y) };
            if (width > 0) { l.AutoSize = false; l.Size = new Size(width, height > 0 ? height : 18); }
            else l.AutoSize = true;
            return l;
        }

        GroupBox MakeGroup(string title, int x, int y, int width, int height)
        {
            return new GroupBox
            {
                Text = title,
                ForeColor = Color.White,
                BackColor = BgGroup,
                Location = new Point(x, y),
                Size = new Size(width, height)
            };
        }

        void BuildUi()
        {
            sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 690,
                BackColor = BgSidebar,
                AutoScroll = true
            };

            const int GW = 650; // grupu platums
            int gy = 10;

            // ---- 1. Pamata ceļš ----
            var g1 = MakeGroup("1. Pamata Ceļš (.hrz, .bin) — Zaļš", 10, gy, GW, 160);
            var btnLoad1 = MakeButton("📂 Atlasīt failu...", ColBlue, BtnLoad1_Click, 15, 25, 190);
            fileInfo1 = MakeLabel("Gājienu skaits: 0", 15, 58, 300);
            var lblSplit = MakeLabel("Lūzuma punkts:", 15, 88, 110);
            splitIndex1 = new NumericUpDown { Minimum = 0, Maximum = 1000000, Width = 80, Location = new Point(130, 86) };
            splitIndex1.ValueChanged += (s, e) => Recalculate();
            var btnEnds1 = MakeButton("Pievienot galos", ColLightBlue, (s, e) => { splitIndex1.Value = data1.Count; }, 220, 84, 140);
            var btnHalf1 = MakeButton("Set 50%", ColGreen, (s, e) => { if (data1.Count > 0) splitIndex1.Value = data1.Count / 2; }, 370, 84, 90);
            var infoSplit = MakeLabel("💡 Ja Lūzuma punkts = Gājienu skaits, 2. ceļš tiek iesprausts starp 1. ceļa beigām un sākumu!",
                                       15, 120, 610, 34);
            infoSplit.ForeColor = Color.FromArgb(0xf3, 0x9c, 0x12);
            g1.Controls.AddRange(new Control[] { btnLoad1, fileInfo1, lblSplit, splitIndex1, btnEnds1, btnHalf1, infoSplit });
            gy += g1.Height + 10;

            // ---- 2. Importētais ceļš ----
            var g2 = MakeGroup("2. Importēt 2. Ceļu (.hrz, .bin) — Dzeltens", 10, gy, GW, 316);
            var btnLoad2 = MakeButton("📂 Atlasīt failu...", ColBlue, BtnLoad2_Click, 15, 25, 190);
            fileInfo2 = MakeLabel("Gājienu skaits: 0", 15, 58, 300);

            // "2. Ceļa Nobīde X" rinda: pogas -100/-10 pirms lodziņa un
            // +10/+100 pēc tā - katra uzreiz maina offsetX2 par attiecīgo
            // vērtību (sk. MakeStepperRow). Y nobīdei tieši tāds pats
            // komplekts savā rindā zemāk.
            var lblOffX = MakeLabel("2. Ceļa Nobīde X:", 15, 88, 150);
            offsetX2 = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = 70, Location = new Point(170, 86) };
            int afterX = MakeStepperRow(g2.Controls, offsetX2, 170, 84);
            var btnAutoAlign = MakeButton("🧲 Piesaistīt atvērumus", ColYellow, BtnAutoAlign_Click, afterX + 16, 84, 210);

            var lblOffY = MakeLabel("Y:", 15, 124, 30);
            offsetY2 = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = 70, Location = new Point(50, 122) };
            MakeStepperRow(g2.Controls, offsetY2, 50, 120);

            offsetX2.ValueChanged += (s, e) => Recalculate();
            offsetY2.ValueChanged += (s, e) => Recalculate();

            // JAUNS: "Lūzuma punkts" arī importētajam (2.) ceļam - ļauj
            // izvēlēties, PIE KURAS 2. ceļa šūnas tas efektīvi "sākas"
            // (rotē), nevis vienmēr piespiedu kārtā izmantot faila
            // burtisko sākumu/beigas. Der TIKAI, ja 2. ceļš PATS veido
            // slēgtu cilpu (pēdējais punkts ar derīgu zirdziņa gājienu
            // savienojas atpakaļ ar pirmo) - citādi rotācija nav
            // ģeometriski iespējama, un lietotājs par to tiek informēts.
            var lblSplit2 = MakeLabel("2. Lūzuma punkts:", 15, 160, 110);
            splitIndex2 = new NumericUpDown { Minimum = 0, Maximum = 1000000, Width = 80, Location = new Point(130, 158) };
            splitIndex2.ValueChanged += (s, e) => Recalculate();
            var btnEnds2 = MakeButton("Bez rotācijas", ColLightBlue, (s, e) => { splitIndex2.Value = 0; }, 220, 156, 130);
            splitInfo2 = MakeLabel("", 360, 160, 270, 34);
            splitInfo2.ForeColor = Color.Cyan;

            var lblTransform = MakeLabel("2. Objekta Transformācijas (F1-F9):", 15, 198, 400);
            string[] tLabels = { "F1 ↕️", "F2 ⤿90°", "F3 ⤿180°", "F4 ⤿270°", "F5 ⏪", "F6 ↔️", "F7 ⤡", "F8 ⤢", "F9 🔄" };
            g2.Controls.AddRange(new Control[] { btnLoad2, fileInfo2, lblOffX, offsetX2, lblOffY, offsetY2, btnAutoAlign,
                                                  lblSplit2, splitIndex2, btnEnds2, splitInfo2, lblTransform });
            int tx = 15, ty = 226;
            for (int i = 1; i <= 9; i++)
            {
                int mode = i;
                var tb = MakeButton(tLabels[i - 1], ColBlue, (s, e) => { data2 = HcLogic.TransformData2(data2, mode); Recalculate(); }, tx, ty, 68);
                g2.Controls.Add(tb);
                tx += 72;
                if (i == 5) { tx = 15; ty += 32; }
            }
            var btnRestore2 = MakeButton("Atjaunot 2.", ColRed, (s, e) => { data2 = new List<int>(originalData2); splitIndex2.Value = 0; Recalculate(); }, tx, ty, 90);
            g2.Controls.Add(btnRestore2);
            gy += g2.Height + 10;

            // ---- 3. Savienošana un kontrole ----
            var g3 = MakeGroup("3. Savienošana & Kontrole", 10, gy, GW, 266);
            statusBox = new Label
            {
                Text = "Gaidīšana: ielādē abus ceļus!",
                BackColor = StatusBad,
                ForeColor = Color.White,
                Location = new Point(15, 25),
                Size = new Size(615, 55),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6)
            };
            var lblScale = MakeLabel("Mērogs (px):", 15, 92, 90);
            gridSize = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 12, Width = 55, Location = new Point(110, 90) };
            gridSize.ValueChanged += (s, e) => { scale = (int)gridSize.Value; Recalculate(); };
            var btnAutoCenter = MakeButton("Auto-Centrs", ColBlue, (s, e) => { isAutoCenter = true; Recalculate(); }, 190, 88, 110);

            // "Centrs X" un "Centrs Y" - katram sava rinda ar tādām pašām
            // -100/-10/+10/+100 pogām kā 2. ceļa nobīdei augstāk.
            var lblCX = MakeLabel("Centrs X:", 15, 130, 90);
            centerX = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = 65, Location = new Point(110, 128) };
            MakeStepperRow(g3.Controls, centerX, 110, 126);
            centerX.ValueChanged += (s, e) => { isAutoCenter = false; Recalculate(); };

            var lblCY = MakeLabel("Y:", 15, 168, 30);
            centerY = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = 65, Location = new Point(50, 166) };
            MakeStepperRow(g3.Controls, centerY, 50, 164);
            centerY.ValueChanged += (s, e) => { isAutoCenter = false; Recalculate(); };

            var legend = MakeLabel("Leģenda: ■ Kvadrāts = Ieeja   |   ● Aplis = Izeja", 15, 206, 500);
            legend.ForeColor = Color.Silver;
            g3.Controls.AddRange(new Control[] { statusBox, lblScale, gridSize, lblCX, centerX, lblCY, centerY, btnAutoCenter, legend });
            gy += g3.Height + 10;

            // ---- 4. Eksports ----
            var g4 = MakeGroup("4. Apvienotā Hamiltona Cikla Eksports", 10, gy, GW, 80);
            var btnSaveHrz = MakeButton("💾 Saglabāt .HRZ", ColGreen, (s, e) => DoSave("hrz"), 15, 30, 300, 34);
            var btnSaveBin = MakeButton("💾 Saglabāt .BIN", ColGreen, (s, e) => DoSave("bin"), 325, 30, 300, 34);
            g4.Controls.AddRange(new Control[] { btnSaveHrz, btnSaveBin });
            gy += g4.Height + 10;

            sidebar.Controls.AddRange(new Control[] { g1, g2, g3, g4 });
            sidebar.AutoScrollMinSize = new Size(GW + 30, gy);

            // ---- Zīmēšanas laukums (canvas ekvivalents) ----
            canvasHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark,
                AutoScroll = true
            };
            canvasHost.Paint += CanvasHost_Paint;
            canvasHost.MouseDown += CanvasHost_MouseDown;

            Controls.Add(canvasHost);
            Controls.Add(sidebar);
        }

        // ---------- Faila ielāde ----------

        void BtnLoad1_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "HC ceļa faili (*.hrz;*.vrz;*.bin;*.txt)|*.hrz;*.vrz;*.bin;*.txt|Visi faili (*.*)|*.*" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    data1 = HcLogic.LoadPathFile(dlg.FileName);
                    fileInfo1.Text = "Gājienu skaits: " + data1.Count;
                    splitIndex1.Maximum = Math.Max(1, data1.Count);
                    splitIndex1.Value = data1.Count; // noklusējums: "Pievienot galos" (kā HTML)
                    Recalculate();
                }
            }
        }

        void BtnLoad2_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "HC ceļa faili (*.hrz;*.vrz;*.bin;*.txt)|*.hrz;*.vrz;*.bin;*.txt|Visi faili (*.*)|*.*" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    data2 = HcLogic.LoadPathFile(dlg.FileName);
                    originalData2 = new List<int>(data2);
                    fileInfo2.Text = "Gājienu skaits: " + data2.Count;
                    splitIndex2.Maximum = Math.Max(1, data2.Count);
                    splitIndex2.Value = 0;
                    Recalculate();
                }
            }
        }

        void BtnAutoAlign_Click(object sender, EventArgs e)
        {
            if (pts1.Count == 0 || data2.Count == 0) return;
            int splitIdx = (int)splitIndex1.Value;
            bool isConnectingEnds = (splitIdx == data1.Count || splitIdx == 0);
            var outPoint = isConnectingEnds ? pts1[pts1.Count - 1] : pts1[splitIdx];
            var desiredEntry = new PointF(outPoint.X + 1, outPoint.Y - 2);

            // SVARĪGI: tā kā nobīde (offsetX2/Y2) tagad piesaista FORMAS
            // ROBEŽLODZIŅA STŪRI (nevis tieši pts2[0]), jāaprēķina, kāda
            // nobīde nepieciešama, lai TIEŠI pts2[0] (2.Ieeja) nonāktu vēlamajā
            // vietā - ņemot vērā starpību starp robežlodziņa stūri un pts2[0].
            int splitIdx2v = data2.Count > 0 ? (int)splitIndex2.Value : 0;
            var rawData2Eff = HcLogic.RotateAtIndex(data2, splitIdx2v);
            var rawPts2 = HcLogic.MovesToPoints(rawData2Eff, 0, 0);
            float minX2 = rawPts2.Min(p => p.X);
            float minY2 = rawPts2.Min(p => p.Y);
            // rawPts2[0] vienmēr ir (0,0), tāpēc pts2[0] = (0-minX2+offsetX2, 0-minY2+offsetY2)
            offsetX2.Value = Clamp(offsetX2, (decimal)(desiredEntry.X + minX2));
            offsetY2.Value = Clamp(offsetY2, (decimal)(desiredEntry.Y + minY2));
            Recalculate();
        }

        static decimal Clamp(NumericUpDown nud, decimal v)
        {
            if (v < nud.Minimum) return nud.Minimum;
            if (v > nud.Maximum) return nud.Maximum;
            return v;
        }

        void DoSave(string format)
        {
            if (data1.Count == 0 || data2.Count == 0)
            {
                MessageBox.Show("Lūdzu, ielādē abus Hamiltona ceļus!", "Trūkst datu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var combined = BuildCombined();
            if (combined == null)
            {
                MessageBox.Show("Nevar saglabāt: saites starp objektiem neatbilst leģitīmiem zirga gājieniem!",
                    "Nederīgs savienojums", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string ext = format == "hrz" ? "hrz" : "bin";
            using (var dlg = new SaveFileDialog
            {
                FileName = "combined_hamilton_cycle." + ext,
                Filter = format == "hrz" ? "HRZ faili (*.hrz)|*.hrz" : "BIN faili (*.bin)|*.bin"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (format == "hrz") HcLogic.SaveHrz(combined, dlg.FileName);
                    else HcLogic.SaveBin(combined, dlg.FileName);
                    MessageBox.Show("Saglabāts: " + dlg.FileName, "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // ---------- Aprēķini (identiski calculateAndDraw() / saveCombined() JS) ----------

        (PointF outPt, PointF inReturnPt) GetObj1Link(bool isConnectingEnds, int splitIdx)
        {
            if (isConnectingEnds)
                return (pts1[pts1.Count - 1], pts1[0]);
            return (pts1[splitIdx], pts1[splitIdx + 1]);
        }

        List<int> BuildCombined()
        {
            int splitIdx1v = (int)splitIndex1.Value;
            bool isConnectingEnds = (splitIdx1v == data1.Count || splitIdx1v == 0);
            var (obj1Out, obj1InReturn) = GetObj1Link(isConnectingEnds, splitIdx1v);
            var obj2In = pts2[0];
            var obj2Out = pts2[pts2.Count - 1];

            var link1 = HcLogic.GetKnightMoveDigit(obj1Out.X, obj1Out.Y, obj2In.X, obj2In.Y);
            var link2 = HcLogic.GetKnightMoveDigit(obj2Out.X, obj2Out.Y, obj1InReturn.X, obj1InReturn.Y);
            if (!link1.HasValue || !link2.HasValue) return null;

            var combined = new List<int>();
            if (isConnectingEnds)
            {
                combined.AddRange(data1);
                combined.Add(link1.Value);
                combined.AddRange(data2Effective);
                combined.Add(link2.Value);
            }
            else
            {
                combined.AddRange(data1.Take(splitIdx1v));
                combined.Add(link1.Value);
                combined.AddRange(data2Effective);
                combined.Add(link2.Value);
                combined.AddRange(data1.Skip(splitIdx1v + 1));
            }
            return combined;
        }

        void UpdateStatus()
        {
            if (data1.Count == 0 || data2.Count == 0)
            {
                statusBox.BackColor = StatusBad;
                statusBox.Text = "Gaidīšana: ielādē abus ceļus!";
                return;
            }
            int splitIdx1v = (int)splitIndex1.Value;
            bool isConnectingEnds = (splitIdx1v == data1.Count || splitIdx1v == 0);
            if (splitIdx1v < 0 || splitIdx1v > data1.Count) { statusBox.BackColor = StatusBad; statusBox.Text = "Nederīgs lūzuma punkts!"; return; }

            var (obj1Out, obj1InReturn) = GetObj1Link(isConnectingEnds, splitIdx1v);
            var obj2In = pts2[0];
            var obj2Out = pts2[pts2.Count - 1];

            var link1 = HcLogic.GetKnightMoveDigit(obj1Out.X, obj1Out.Y, obj2In.X, obj2In.Y);
            var link2 = HcLogic.GetKnightMoveDigit(obj2Out.X, obj2Out.Y, obj1InReturn.X, obj1InReturn.Y);

            string s1 = isConnectingEnds ? "1. Izeja (gals)" : ("1. Lūzums (#" + splitIdx1v + ")");
            string s2 = isConnectingEnds ? "1. Ieeja (sākums)" : ("1. Atgriešanās (#" + (splitIdx1v + 1) + ")");

            if (link1.HasValue && link2.HasValue)
            {
                statusBox.BackColor = StatusOk;
                statusBox.Text = "✅ SAITES IR DERĪGAS!\r\nSaite 1 (" + s1 + " -> 2. Ieeja): " + link1 +
                                  "\r\nSaite 2 (2. Izeja -> " + s2 + "): " + link2;
            }
            else
            {
                statusBox.BackColor = StatusBad;
                statusBox.Text = "❌ Nederīgas saites attālums!\r\nSaite 1 (" + s1 + " -> 2. Ieeja): " +
                                  (link1.HasValue ? "OK (" + link1 + ")" : "Nav zirga gājiens") +
                                  "\r\nSaite 2 (2. Izeja -> " + s2 + "): " +
                                  (link2.HasValue ? "OK (" + link2 + ")" : "Nav zirga gājiens");
            }
        }

        void Recalculate()
        {
            offsetX2ForCalc = (int)offsetX2.Value;
            offsetY2ForCalc = (int)offsetY2.Value;

            pts1 = HcLogic.MovesToPoints(data1);

            int splitIdx2v = data2.Count > 0 ? (int)splitIndex2.Value : 0;
            data2Effective = HcLogic.RotateAtIndex(data2, splitIdx2v);

            // SVARĪGI (labots pēc lietotāja ieteikuma): NEDRĪKST piesaistīt
            // ROTĀCIJAS SĀKUMA PUNKTU (data2Effective[0]) tieši nobīdei - tā kā
            // rotācija maina, KURA šūna ir "pirmā", visa forma katru reizi
            // "lektu" pa ekrānu. Tā vietā vispirms aprēķinām formu BRĪVĀ
            // (nefiksētā) koordinātu sistēmā, atrodam tās ROBEŽLODZIŅA stūri
            // (min X, min Y) un TO piesaistam nobīdei - tā forma VIENMĒR
            // paliek tajā pašā ekrāna vietā, mainās tikai tas, KURŠ punkts
            // tajā ir apzīmēts kā "lūzums".
            var rawPts2 = HcLogic.MovesToPoints(data2Effective, 0, 0);
            float minX2 = rawPts2.Min(p => p.X);
            float minY2 = rawPts2.Min(p => p.Y);
            pts2 = rawPts2.Select(p => new PointF(p.X - minX2 + offsetX2ForCalc, p.Y - minY2 + offsetY2ForCalc)).ToList();

            splitInfo2.Text = (splitIdx2v > 0 && splitIdx2v < data2.Count)
                ? ("✂️ 2. ceļā izveidota sprauga starp #" + splitIdx2v + " un #" + (splitIdx2v + 1) +
                   " - divi atšķirīgi gali (visas šūnas saglabātas).")
                : "";

            UpdateStatus();
            canvasHost.Invalidate();
        }
        int offsetX2ForCalc = 0, offsetY2ForCalc = 0;

        // ---------- Zīmēšana (canvas ekvivalents) ----------

        (float drawOffX, float drawOffY) ComputeDrawOffset(int viewportWidth, int viewportHeight)
        {
            var allPts = pts1.Concat(pts2).ToList();
            float minX = allPts.Count > 0 ? Math.Min(0, allPts.Min(p => p.X)) : 0;
            float maxX = allPts.Count > 0 ? Math.Max(0, allPts.Max(p => p.X)) : 0;
            float minY = allPts.Count > 0 ? Math.Min(0, allPts.Min(p => p.Y)) : 0;
            float maxY = allPts.Count > 0 ? Math.Max(0, allPts.Max(p => p.Y)) : 0;

            float midX, midY;
            if (isAutoCenter)
            {
                midX = (maxX + minX) / 2f;
                midY = (maxY + minY) / 2f;
                centerX.ValueChanged -= null; // (bez darbības - tikai skaidrības labad)
            }
            else
            {
                midX = (float)centerX.Value;
                midY = (float)centerY.Value;
            }
            return (viewportWidth / 2f - midX * scale, viewportHeight / 2f - midY * scale);
        }

        void CanvasHost_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(canvasHost.AutoScrollPosition.X, canvasHost.AutoScrollPosition.Y);

            int viewW = Math.Max(canvasHost.ClientSize.Width, 600);
            int viewH = Math.Max(canvasHost.ClientSize.Height, 600);
            var (drawOffX, drawOffY) = ComputeDrawOffset(viewW, viewH);

            if (isAutoCenter && pts1.Count > 0)
            {
                var allPts = pts1.Concat(pts2).ToList();
                float minX = Math.Min(0, allPts.Min(p => p.X)), maxX = Math.Max(0, allPts.Max(p => p.X));
                float minY = Math.Min(0, allPts.Min(p => p.Y)), maxY = Math.Max(0, allPts.Max(p => p.Y));
                centerX.Value = Clamp(centerX, (decimal)Math.Round((maxX + minX) / 2f));
                centerY.Value = Clamp(centerY, (decimal)Math.Round((maxY + minY) / 2f));
            }

            Func<PointF, PointF> ToScreen = p => new PointF(drawOffX + p.X * scale, drawOffY + p.Y * scale);

            // 1. ceļš (zaļš)
            if (pts1.Count > 1)
                using (var pen = new Pen(Color.Lime, 1.8f))
                    g.DrawLines(pen, pts1.Select(ToScreen).ToArray());

            // 2. ceļš (dzeltens)
            if (pts2.Count > 1)
                using (var pen = new Pen(Color.FromArgb(0xf1, 0xc4, 0x0f), 1.8f))
                    g.DrawLines(pen, pts2.Select(ToScreen).ToArray());

            // saišu (sarkanas, punktētas) zīmēšana + marķieri
            if (data1.Count > 0 && data2.Count > 0)
            {
                int splitIdx1v = (int)splitIndex1.Value;
                if (splitIdx1v >= 0 && splitIdx1v <= data1.Count)
                {
                    bool isConnectingEnds = (splitIdx1v == data1.Count || splitIdx1v == 0);
                    var (obj1Out, obj1InReturn) = GetObj1Link(isConnectingEnds, splitIdx1v);
                    var obj2In = pts2[0];
                    var obj2Out = pts2[pts2.Count - 1];
                    var link1 = HcLogic.GetKnightMoveDigit(obj1Out.X, obj1Out.Y, obj2In.X, obj2In.Y);
                    var link2 = HcLogic.GetKnightMoveDigit(obj2Out.X, obj2Out.Y, obj1InReturn.X, obj1InReturn.Y);
                    if (link1.HasValue && link2.HasValue)
                    {
                        using (var dashPen = new Pen(ColRed, 2f) { DashPattern = new float[] { 4, 4 } })
                        {
                            g.DrawLine(dashPen, ToScreen(obj1Out), ToScreen(obj2In));
                            g.DrawLine(dashPen, ToScreen(obj2Out), ToScreen(obj1InReturn));
                        }
                    }

                    if (!isConnectingEnds && splitIdx1v > 0 && splitIdx1v < data1.Count)
                    {
                        // SVARĪGI: pie "lūzuma" ir DIVI atšķirīgi nozīmīgi punkti (nevis
                        // viens) - izmantojam TOS PAŠUS kvadrāta/apļa marķierus, ko citur,
                        // lai vizuālā valoda būtu vienota un uzreiz saprotama:
                        //   - obj1Out (pirms pārrāvuma) = "izeja" (aplis) - ŠIM jāsavienojas
                        //     ar 2. ceļa IEEJU (kvadrātu).
                        //   - obj1InReturn (pēc pārrāvuma) = "ieeja" (kvadrāts) - TO sasniedz
                        //     2. ceļa IZEJA (aplis).
                        DrawMarker(g, ToScreen(obj1Out), false, Color.Cyan,
                                   "1. Lūzums-izeja (#" + splitIdx1v + ") → uz 2.Ieeju", -20);
                        DrawMarker(g, ToScreen(obj1InReturn), true, Color.Cyan,
                                   "1. Atgriešanās-ieeja (#" + (splitIdx1v + 1) + ") ← no 2.Izejas", 14);
                    }
                }
            }

            // marķieri 1. ceļam
            if (pts1.Count > 0)
            {
                DrawMarker(g, ToScreen(pts1[0]), true, Color.Lime, "1. Ieeja (0)");
                DrawMarker(g, ToScreen(pts1[pts1.Count - 1]), false, Color.Lime, "1. Izeja (" + data1.Count + ")");
            }
            // marķieri 2. ceļam
            if (pts2.Count > 0)
            {
                var p2In = ToScreen(pts2[0]);
                var p2Out = ToScreen(pts2[pts2.Count - 1]);
                // SVARĪGI: ja 2. ceļš ir SLĒGTA cilpa, pts2[0] un pts2[-1] ir
                // TIEŠI VIENĀ punktā (jebkurai rotācijai) - aplis (izeja),
                // zīmēts pēc kvadrāta (ieeja), to PILNĪBĀ nosegtu, padarot
                // ieejas marķieri neredzamu. Tāpēc, ja abi sakrīt (vai ir ļoti
                // tuvu), nedaudz izbīdām abus PRETĒJOS virzienos, lai abi
                // paliktu redzami un atšķirami - tieši tāpat, kā to jau
                // darām 1. ceļa "lūzuma" marķieriem.
                double dxp = p2In.X - p2Out.X, dyp = p2In.Y - p2Out.Y;
                bool sameSpot = (dxp * dxp + dyp * dyp) < 4f; // < 2px attālums
                if (sameSpot)
                {
                    var nIn = new PointF(p2In.X - 6, p2In.Y - 6);
                    var nOut = new PointF(p2Out.X + 6, p2Out.Y + 6);
                    DrawMarker(g, nIn, true, Color.FromArgb(0xf1, 0xc4, 0x0f), "2. Ieeja", -20);
                    DrawMarker(g, nOut, false, Color.FromArgb(0xf1, 0xc4, 0x0f), "2. Izeja", 14);
                }
                else
                {
                    DrawMarker(g, p2In, true, Color.FromArgb(0xf1, 0xc4, 0x0f), "2. Ieeja");
                    DrawMarker(g, p2Out, false, Color.FromArgb(0xf1, 0xc4, 0x0f), "2. Izeja");
                }
            }

            // AutoScroll satura izmērs
            if (pts1.Count > 0 || pts2.Count > 0)
            {
                var allPts = pts1.Concat(pts2).ToList();
                float minX = Math.Min(0, allPts.Min(p => p.X)), maxX = Math.Max(0, allPts.Max(p => p.X));
                float minY = Math.Min(0, allPts.Min(p => p.Y)), maxY = Math.Max(0, allPts.Max(p => p.Y));
                int neededW = (int)((maxX - minX + 4) * scale);
                int neededH = (int)((maxY - minY + 4) * scale);
                var newSize = new Size(Math.Max(neededW, viewW), Math.Max(neededH, viewH));
                if (canvasHost.AutoScrollMinSize != newSize)
                    canvasHost.AutoScrollMinSize = newSize;
            }
        }

        void DrawMarker(Graphics g, PointF screenPt, bool isEntry, Color color, string label, int labelOffsetY = -6)
        {
            const float size = 12f;
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(Color.White, 2f))
            {
                if (isEntry)
                {
                    g.FillRectangle(brush, screenPt.X - size / 2, screenPt.Y - size / 2, size, size);
                    g.DrawRectangle(pen, screenPt.X - size / 2, screenPt.Y - size / 2, size, size);
                }
                else
                {
                    g.FillEllipse(brush, screenPt.X - size / 2, screenPt.Y - size / 2, size, size);
                    g.DrawEllipse(pen, screenPt.X - size / 2, screenPt.Y - size / 2, size, size);
                }
            }
            using (var f = new Font("Arial", 9, FontStyle.Bold))
            using (var outline = new SolidBrush(Color.Black))
            {
                // melns kontūrs zem baltā teksta - lasāms pat uz gaišas (dzeltenas/
                // ciānas) līnijas fona, nevis tikai uz tumšā canvas
                float tx = screenPt.X + 10, ty = screenPt.Y + labelOffsetY;
                foreach (var d in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    g.DrawString(label, f, outline, tx + d.Item1, ty + d.Item2);
                g.DrawString(label, f, Brushes.White, tx, ty);
            }
        }

        void CanvasHost_MouseDown(object sender, MouseEventArgs e)
        {
            if (pts1.Count == 0 && pts2.Count == 0) return;

            int viewW = Math.Max(canvasHost.ClientSize.Width, 600);
            int viewH = Math.Max(canvasHost.ClientSize.Height, 600);
            var (drawOffX, drawOffY) = ComputeDrawOffset(viewW, viewH);

            float clickX = e.X - canvasHost.AutoScrollPosition.X;
            float clickY = e.Y - canvasHost.AutoScrollPosition.Y;
            float wx = (clickX - drawOffX) / scale;
            float wy = (clickY - drawOffY) / scale;

            // Meklējam tuvāko punktu ABOS ceļos (1. UN 2.) un izvēlamies to,
            // kurš ir tuvāk klikšķim - tā klikšķis uz ZAĻĀ ceļa iestata
            // "1. Lūzuma punktu", bet klikšķis uz DZELTENĀ - "2. Lūzuma punktu".
            double best1DistSq = double.MaxValue;
            int closest1 = -1;
            for (int i = 0; i < pts1.Count; i++)
            {
                double dx = pts1[i].X - wx, dy = pts1[i].Y - wy;
                double distSq = dx * dx + dy * dy;
                if (distSq < best1DistSq) { best1DistSq = distSq; closest1 = i; }
            }

            double best2DistSq = double.MaxValue;
            int closest2 = -1;
            for (int i = 0; i < pts2.Count; i++)
            {
                double dx = pts2[i].X - wx, dy = pts2[i].Y - wy;
                double distSq = dx * dx + dy * dy;
                if (distSq < best2DistSq) { best2DistSq = distSq; closest2 = i; }
            }

            if (closest1 == -1 && closest2 == -1) return;

            if (best1DistSq <= best2DistSq)
            {
                if (closest1 == 0 || closest1 == pts1.Count - 1)
                    splitIndex1.Value = Clamp(splitIndex1, data1.Count);
                else
                    splitIndex1.Value = Clamp(splitIndex1, closest1);
            }
            else
            {
                // SVARĪGI: pts2 nāk no data2Effective, kas (ja splitIndex2 != 0)
                // IZLAIŽ tieši VIENU gājienu pie iepriekšējā lūzuma punkta - tāpēc
                // closest2 ir indekss ŠAJĀ (ar izlaisto gājienu) secībā, nevis
                // tiešā oriģinālā data2 indeksācijā. Pareizā atpakaļpārrēķina
                // formula (pārbaudīta): ja iepriekš NEBIJA rotācijas (prevSplit==0,
                // data2Effective==data2 tieši), atbilstība ir tieša (closest2 pats
                // par sevi); citādi oriģinālais indekss = (prevSplit+1+closest2)
                // mod N, jo pts2[0] vienmēr atbilst oriģinālajam punktam
                // prevSplit+1, un secība turpinās aplinkus caur cikla "šuvi".
                int prevSplit = (int)splitIndex2.Value;
                int newOriginalIndex = (prevSplit == 0)
                    ? closest2
                    : (prevSplit + 1 + closest2) % data2.Count;
                splitIndex2.Value = Clamp(splitIndex2, newOriginalIndex);
            }
            Recalculate();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
