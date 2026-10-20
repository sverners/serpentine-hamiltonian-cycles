// PngSensorsToFFseciba.cs
// C# versija png_sensors_to_ffseciba.py - PNG (melns taisnstūris = tīrāmā
// virsma, balti kvadrāti = sensori; PLUS tieva melna līnija/spirāle = ARĪ
// tīrāmā virsma) -> FFseciba.txt priekš 6x6 "plēsto" .abt blokiem.
//
// SVARĪGI: Python versija izmanto scikit-image (skeletonize) un
// scipy.ndimage (label) - šīm NAV tiešu C# ekvivalentu, tāpēc šeit ir
// PAŠRAKSTĪTS Guo-Hall iztieknošanas algoritms un 8-savienojamības
// komponenšu marķēšana, kas dod TO PAŠU rezultātu.
//
// PROJEKTA IZVEIDE VISUAL STUDIO:
//   Konsoles lietotne (.NET Framework) - System.Drawing jau iebūvēts.
//
// LIETOŠANA:
//   PngSensorsToFFseciba.exe [attēls.png] [FFseciba_izvade.txt]
//   (noklusējumi: testaVirsma.png -> FFseciba.txt)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace PngSensorsToFFseciba
{
    class Program
    {
        const int PIXELS_PER_BLOCK = 4;      // 4x4 px attēlā = 1 FF bloks
        const int BLACK_THRESHOLD = 128;     // pikselis tumšāks par šo = "melns"
        const int RUN_LENGTH_THRESHOLD = 100; // min. nepārtraukta melna posma garums
        const int DENSE_MIN_THICKNESS = 12;   // melns apgabals, kurā ietilpst 12x12 px kvadrāts, ir BIEZS (aizpildīts), nevis tieva līnija
        const int DENSE_GROW = 5;             // biezās daļas apmale (px), lai neatzīmētu tās malas kā "tievu līniju"
        const double MIN_RECT_FILL = 0.20;    // "blīvam taisnstūrim" jābūt vismaz tik daļai melnam; citādi tā ir KONTŪRA/RĀMIS, nevis aizpildīta virsma
        const int MIN_LINE_COMPONENT = 60;    // tievās līnijas gabali, kas īsāki par šo (px), tiek uzskatīti par trokšņiem (tikai ja ir biezā daļa)
        const double FILL_MAJORITY = 0.5;    // taisnstūra rūtiņa = "virsma" slieksnis

        const string IMAGE_DEFAULT = "testaVirsma.png";
        const string OUTPUT_DEFAULT = "FFseciba.txt";

        // ================== PALĪGSTRUKTŪRAS ==================

        struct Cell : IEquatable<Cell>
        {
            public int R, C;
            public Cell(int r, int c) { R = r; C = c; }
            public bool Equals(Cell o) => R == o.R && C == o.C;
            public override bool Equals(object o) => o is Cell c && Equals(c);
            public override int GetHashCode() => R * 1000003 + C;
            public override string ToString() => $"({R},{C})";
        }

        // ================== ATTĒLA IELĀDE ==================

        static bool[,] LoadBlackMask(string path, out int width, out int height)
        {
            using (var bmp = new Bitmap(path))
            {
                width = bmp.Width;
                height = bmp.Height;
                var mask = new bool[height, width]; // [y,x] - tāpat kā Python [rinda,kolonna]
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color px = bmp.GetPixel(x, y);
                        // TIEŠI PIL ".convert('L')" formula (ITU-R 601-2 luma)
                        double gray = px.R * 299.0 / 1000.0 + px.G * 587.0 / 1000.0 + px.B * 114.0 / 1000.0;
                        mask[y, x] = gray < BLACK_THRESHOLD;
                    }
                }
                return mask;
            }
        }

        // ================== BLĪVĀ TAISNSTŪRA NOTEIKŠANA ==================

        static int LongestRun(bool[,] mask, bool isRow, int index, int length)
        {
            int best = 0, cur = 0;
            for (int i = 0; i < length; i++)
            {
                bool v = isRow ? mask[index, i] : mask[i, index];
                if (v) { cur++; if (cur > best) best = cur; }
                else cur = 0;
            }
            return best;
        }

        /// <summary>Atrod blīvā taisnstūra precīzās pikseļu robežas pēc garākā
        /// nepārtrauktā melnā posma katrā rindā/kolonnā - tieva skiču līnija
        /// NEKAD nedod tik garu posmu. Atgriež null, ja taisnstūris nav atrasts.</summary>
        static (int x0, int x1, int y0, int y1)? FindRectangleBounds(bool[,] mask, int width, int height)
        {
            var rowOk = new bool[height];
            for (int y = 0; y < height; y++)
                rowOk[y] = LongestRun(mask, true, y, width) > RUN_LENGTH_THRESHOLD;
            var colOk = new bool[width];
            for (int x = 0; x < width; x++)
                colOk[x] = LongestRun(mask, false, x, height) > RUN_LENGTH_THRESHOLD;

            var ys = Enumerable.Range(0, height).Where(y => rowOk[y]).ToList();
            var xs = Enumerable.Range(0, width).Where(x => colOk[x]).ToList();
            if (ys.Count == 0 || xs.Count == 0) return null;
            return (xs.Min(), xs.Max(), ys.Min(), ys.Max());
        }

        /// <summary>Atrod VISU 'true' pikseļu kopējo robežlodziņu.</summary>
        static (int x0, int x1, int y0, int y1) FindBlackBounds(bool[,] mask, int width, int height)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (mask[y, x])
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < minX) throw new InvalidOperationException("Maskā nav neviena 'true' pikseļa!");
            return (minX, maxX, minY, maxY);
        }

        // ================== RŪTIŅU TĪKLA VEIDOŠANA ==================

        /// <summary>Sadala apgabalu step x step rūtiņās pēc VAIRĀKUMA (der blīvam taisnstūrim).</summary>
        static Dictionary<Cell, bool> BuildGridMajority(bool[,] mask, int x0, int x1, int y0, int y1, int step, int limX1, int limY1)
        {
            int nCols = CellCount(x1 - x0 + 1, step);
            int nRows = CellCount(y1 - y0 + 1, step);
            var grid = new Dictionary<Cell, bool>();
            for (int r = 0; r < nRows; r++)
            {
                for (int c = 0; c < nCols; c++)
                {
                    int by0 = y0 + r * step, bx0 = x0 + c * step;
                    int blackCount = 0, total = 0;
                    // pārpalikušie pikseļi pie malas veido daļēju bloku - to uzskata par pilnu rūtiņu
                    // (vairākumu skaita no tiem pikseļiem, kas tur tiešām ir)
                    for (int dy = 0; dy < step && by0 + dy <= limY1; dy++)
                        for (int dx = 0; dx < step && bx0 + dx <= limX1; dx++)
                        { total++; if (mask[by0 + dy, bx0 + dx]) blackCount++; }
                    grid[new Cell(r, c)] = total > 0 && (blackCount / (double)total) >= FILL_MAJORITY;
                }
            }
            return grid;
        }

        // ================== ZHANG-SUEN IZTIEKNOŠANA ==================
        // ================== GUO-HALL IZTIEKNOŠANA ==================
        // Pašrakstīts standarta Guo-Hall "thinning" algoritms - aizstāj
        // Python skimage.morphology.skeletonize (nav tieša C# ekvivalenta).
        // IZVĒLĒTS (nevis vienkāršāks Zhang-Suen) tāpēc, ka Zhang-Suen
        // dažās DIAGONĀLĀS konfigurācijās atstāj nepilnīgi iztieknotus
        // 2-3 pikseļu platus posmus (pārbaudīts praksē ar reāliem
        // attēliem) - Guo-Hall šo problēmu nerada, dodot tīru 1-pikseļa
        // platu skeletu, kas praktiski identisks skimage rezultātam.
        // P2..P9 ir 8 kaimiņi pulksteņrādītāja virzienā, sākot no augšas:
        //   P9 P2 P3
        //   P8 P1 P4
        //   P7 P6 P5

        static bool[,] Skeletonize(bool[,] mask, int width, int height)
        {
            var img = (bool[,])mask.Clone();
            bool changed = true;
            while (changed)
            {
                changed = false;
                changed |= GuoHallStep(img, width, height, subIter: 0);
                changed |= GuoHallStep(img, width, height, subIter: 1);
            }
            return img;
        }

        static bool GuoHallStep(bool[,] img, int width, int height, int subIter)
        {
            var toRemove = new List<Cell>();
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    if (!img[y, x]) continue;

                    bool p2 = img[y - 1, x];
                    bool p3 = img[y - 1, x + 1];
                    bool p4 = img[y, x + 1];
                    bool p5 = img[y + 1, x + 1];
                    bool p6 = img[y + 1, x];
                    bool p7 = img[y + 1, x - 1];
                    bool p8 = img[y, x - 1];
                    bool p9 = img[y - 1, x - 1];

                    int C = ((!p2 && (p3 || p4)) ? 1 : 0) + ((!p4 && (p5 || p6)) ? 1 : 0) +
                            ((!p6 && (p7 || p8)) ? 1 : 0) + ((!p8 && (p9 || p2)) ? 1 : 0);
                    if (C != 1) continue;

                    int n1 = ((p9 || p2) ? 1 : 0) + ((p3 || p4) ? 1 : 0) + ((p5 || p6) ? 1 : 0) + ((p7 || p8) ? 1 : 0);
                    int n2 = ((p2 || p3) ? 1 : 0) + ((p4 || p5) ? 1 : 0) + ((p6 || p7) ? 1 : 0) + ((p8 || p9) ? 1 : 0);
                    int n = Math.Min(n1, n2);
                    if (n < 2 || n > 3) continue;

                    bool m = (subIter == 0) ? ((p6 || p7 || !p9) && p8) : ((p2 || p3 || !p5) && p4);
                    if (m) continue;

                    toRemove.Add(new Cell(y, x));
                }
            }
            foreach (var c in toRemove) img[c.R, c.C] = false;
            return toRemove.Count > 0;
        }

        // ================== SPURU TĪRĪŠANA ==================

        static List<Cell> PxNeighbors(Cell p, HashSet<Cell> coords)
        {
            var res = new List<Cell>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dy == 0 && dx == 0) continue;
                    var q = new Cell(p.R + dy, p.C + dx);
                    if (coords.Contains(q)) res.Add(q);
                }
            return res;
        }

        /// <summary>Iztieknošana bieži rada nelielus "spuru" atzarojumus - šī
        /// funkcija atkārtoti atrod ĪSUS (&lt;= min_branch_length) atzarojumus
        /// un tos noņem, atstājot tikai "galveno" skeleta struktūru.</summary>
        static bool[,] PruneSkeletonSpurs(bool[,] skel, int width, int height, int minBranchLength = 20)
        {
            var img = (bool[,])skel.Clone();
            bool changed = true;
            while (changed)
            {
                changed = false;
                var coords = new HashSet<Cell>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (img[y, x]) coords.Add(new Cell(y, x));
                if (coords.Count < 2) break;

                var endpoints = coords.Where(p => PxNeighbors(p, coords).Count <= 1).ToList();
                foreach (var ep in endpoints)
                {
                    if (!coords.Contains(ep)) continue; // jau noņemts šajā pašā caurgājienā
                    var branch = new List<Cell> { ep };
                    Cell? prev = null;
                    Cell cur = ep;
                    while (true)
                    {
                        var nbs = PxNeighbors(cur, coords).Where(n => !n.Equals(prev ?? new Cell(int.MinValue, int.MinValue))).ToList();
                        if (nbs.Count != 1) break;
                        prev = cur;
                        cur = nbs[0];
                        branch.Add(cur);
                        if (branch.Count > minBranchLength) break;
                    }
                    if (branch.Count <= minBranchLength && branch.Count > 1)   // izolētam pikselim nav ko noņemt (citādi bezgalīgs cikls)
                    {
                        for (int i = 0; i < branch.Count - 1; i++) // paturam pēdējo (zara/savienojuma) punktu
                        {
                            img[branch[i].R, branch[i].C] = false;
                            coords.Remove(branch[i]);
                        }
                        changed = true;
                    }
                }
            }
            return img;
        }

        /// <summary>Atrod un noņem ĪSUS "liekos cikla segmentus" skeleta
        /// grafā - šie rodas pie IELIEKTIEM stūriem (nevis izliektiem):
        /// biezas līnijas skelets (medial axis) pie ieliekta stūra VIENMĒR
        /// rada nelielu, ĪSTU ciklu (nevis tikai vienkāršu "spuru" ar
        /// skaidru strupceļa galu, ko jau apstrādā PruneSkeletonSpurs).
        /// Šeit no katra zara (grāds&gt;=3) punkta izsekojam katru virzienu
        /// pa vienkāršu (grāds-2) ķēdi arvien pieaugošā garumā un katrā
        /// solī pārbaudam: ja šo ĶĒDI izņemtu, vai VISS atlikušais skelets
        /// paliktu VIENĀ vienotā gabalā (nevis sadalītos divās daļās)?
        /// Tas ir VISA GRAFA savienojamības pārbaude (nevis tikai starp
        /// diviem konkrētiem punktiem) - tas ir svarīgi, jo ķēdes
        /// noņemšana var saraut savienojamību KAUT KUR CITUR tālu projām,
        /// ne tikai starp pašiem diviem galapunktiem. Tiklīdz atrasta
        /// droši noņemama ķēde, to noņemam un restartējam meklēšanu.</summary>
        static bool RemoveSmallLoops(bool[,] img, int width, int height, int maxChainLen = 20)
        {
            bool anyChanged = false;
            var coords = new HashSet<Cell>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (img[y, x]) coords.Add(new Cell(y, x));
            int totalSize = coords.Count;

            bool changed = true;
            int safety = 0;
            while (changed && safety < 2000)
            {
                safety++;
                changed = false;
                var branchPoints = coords.Where(p => PxNeighbors(p, coords).Count >= 3).ToList();

                foreach (var bp in branchPoints)
                {
                    if (changed) break;
                    if (!coords.Contains(bp) || PxNeighbors(bp, coords).Count < 3) continue;
                    var dirs = PxNeighbors(bp, coords);

                    foreach (var startDir in dirs)
                    {
                        if (changed) break;
                        var chain = new List<Cell> { startDir };
                        Cell prev = bp;
                        Cell cur = startDir;
                        for (int step = 0; step < maxChainLen; step++)
                        {
                            // pārbaudam: ja noņemtu ŠO ĶĒDI, vai atlikums paliktu
                            // VIENS savienots gabals (ar pareizo, sagaidāmo izmēru)?
                            foreach (var c in chain) coords.Remove(c);
                            bool ok = IsSingleComponent(coords, totalSize - chain.Count);
                            if (ok)
                            {
                                foreach (var c in chain) img[c.R, c.C] = false;
                                changed = true;
                                anyChanged = true;
                                totalSize -= chain.Count;
                                break;
                            }
                            foreach (var c in chain) coords.Add(c); // atjaunojam - mēģinām garāku ķēdi

                            var nbs = PxNeighbors(cur, coords).Where(n => !n.Equals(prev) && !chain.Contains(n)).ToList();
                            if (nbs.Count != 1) break; // strupceļš vai cits zars - beidzam šo virzienu
                            prev = cur;
                            cur = nbs[0];
                            chain.Add(cur);
                        }
                    }
                }
            }
            return anyChanged;
        }

        /// <summary>Pārbauda, vai coords veido TIEŠI VIENU savienotu gabalu
        /// ar PAREIZO (sagaidāmo) kopējo izmēru - ja kāda daļa ir atdalīta
        /// (grafs sadalījies), sasniegtais apmeklēto punktu skaits būs
        /// MAZĀKS par expectedSize, un šī funkcija atgriezīs false.</summary>
        static bool IsSingleComponent(HashSet<Cell> coords, int expectedSize)
        {
            if (coords.Count == 0) return expectedSize == 0;
            var start = coords.First();
            var visited = new HashSet<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var nb in PxNeighbors(cur, coords))
                    if (!visited.Contains(nb)) { visited.Add(nb); queue.Enqueue(nb); }
            }
            return visited.Count == expectedSize;
        }

        // ================== SAVIENOTO KOMPONENŠU MARĶĒŠANA (8-savienojamība) ==================
        // Aizstāj Python scipy.ndimage.label(mask, structure=np.ones((3,3)))

        static bool[,] KeepLargestComponent(bool[,] mask, int width, int height)
        {
            var visited = new bool[height, width];
            var bestComponent = new List<Cell>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[y, x] || visited[y, x]) continue;
                    var component = new List<Cell>();
                    var stack = new Stack<Cell>();
                    stack.Push(new Cell(y, x));
                    visited[y, x] = true;
                    while (stack.Count > 0)
                    {
                        var cur = stack.Pop();
                        component.Add(cur);
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dy == 0 && dx == 0) continue;
                                int ny = cur.R + dy, nx = cur.C + dx;
                                if (ny < 0 || ny >= height || nx < 0 || nx >= width) continue;
                                if (mask[ny, nx] && !visited[ny, nx])
                                {
                                    visited[ny, nx] = true;
                                    stack.Push(new Cell(ny, nx));
                                }
                            }
                    }
                    if (component.Count > bestComponent.Count) bestComponent = component;
                }
            }

            var result = new bool[height, width];
            foreach (var c in bestComponent) result[c.R, c.C] = true;
            return result;
        }

        // ================== SKELETA IZSEKOŠANA UZ RŪTIŅU TĪKLU ==================

        /// <summary>Izseko TĪRU (grāds&lt;=2 gandrīz visur) 1-pikseļa platuma
        /// skeleta ceļu PĒC SECĪBAS un pārvērš to par RŪTIŅU KOPU, ievietojot
        /// "tiltiņa" rūtiņas, kur divas secīgas ceļa rūtiņas ir tikai
        /// DIAGONĀLI (nevis taisnleņķī) blakus.</summary>
        static List<Cell> TraceSkeletonToGridCells(bool[,] skel, int width, int height, int x0, int y0, int step)
        {
            var coords = new HashSet<Cell>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (skel[y, x]) coords.Add(new Cell(y, x));
            if (coords.Count == 0) return new List<Cell>();

            var endpoints = coords.Where(p => PxNeighbors(p, coords).Count <= 1).ToList();
            Cell startPx = endpoints.Count > 0 ? endpoints[0] : coords.First();

            var orderedPx = new List<Cell> { startPx };
            var seenPx = new HashSet<Cell> { startPx };
            Cell? prev = null;
            Cell cur = startPx;
            while (true)
            {
                var nbs = PxNeighbors(cur, coords)
                    .Where(n => !n.Equals(prev ?? new Cell(int.MinValue, int.MinValue)) && !seenPx.Contains(n))
                    .ToList();
                if (nbs.Count == 0) break;
                var nxt = nbs[0];
                orderedPx.Add(nxt);
                seenPx.Add(nxt);
                prev = cur;
                cur = nxt;
            }

            Func<Cell, Cell> toGrid = p => new Cell((p.R - y0) / step, (p.C - x0) / step);

            var gridCells = new List<Cell>();
            foreach (var p in orderedPx)
            {
                var gc = toGrid(p);
                if (gridCells.Count == 0 || !gridCells[gridCells.Count - 1].Equals(gc))
                    gridCells.Add(gc);
            }

            // ievietojam tiltiņa rūtiņas starp SECĪGĀM rūtiņām, kas NAV
            // blakusesošas (ne taisnleņķī, ne diagonāli) - vispārīgi
            // JEBKURA izmēra "lēcienam" (nevis tikai vienai diagonālei),
            // pa taisnu līniju soli pa solim ejot pretī mērķim.
            var bridged = new List<Cell>();
            if (gridCells.Count > 0) bridged.Add(gridCells[0]);
            for (int i = 1; i < gridCells.Count; i++)
            {
                var a = bridged[bridged.Count - 1];
                var b = gridCells[i];
                while (Math.Abs(a.R - b.R) > 1 || Math.Abs(a.C - b.C) > 1)
                {
                    int stepR = Math.Sign(b.R - a.R);
                    int stepC = Math.Sign(b.C - a.C);
                    a = new Cell(a.R + stepR, a.C + stepC);
                    bridged.Add(a);
                }
                if (Math.Abs(a.R - b.R) == 1 && Math.Abs(a.C - b.C) == 1)
                    bridged.Add(new Cell(a.R, b.C)); // pēdējā diagonāle - jebkurš no diviem "stūra" variantiem der
                bridged.Add(b);
            }
            return bridged;
        }

        // ================== KOMBINĒTĀ NOTEIKŠANA ==================

        // ================== BIEZO DAĻU ATPAZĪŠANA (morfoloģija) ==================

        static int[,] IntegralImage(bool[,] m, int w, int h)
        {
            var ii = new int[h + 1, w + 1];
            for (int y = 0; y < h; y++)
            {
                int rowSum = 0;
                for (int x = 0; x < w; x++)
                {
                    if (m[y, x]) rowSum++;
                    ii[y + 1, x + 1] = ii[y, x + 1] + rowSum;
                }
            }
            return ii;
        }

        /// <summary>Summa taisnstūrī [x0..x1] x [y0..y1] (ieskaitot), ar apgriešanu pie attēla malām.</summary>
        static int RectSum(int[,] ii, int w, int h, int x0, int y0, int x1, int y1)
        {
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
            if (x1 > w - 1) x1 = w - 1; if (y1 > h - 1) y1 = h - 1;
            if (x1 < x0 || y1 < y0) return 0;
            return ii[y1 + 1, x1 + 1] - ii[y0, x1 + 1] - ii[y1 + 1, x0] + ii[y0, x0];
        }

        /// <summary>Paplašina maskas 'true' apgabalus par r pikseļiem uz visām pusēm (kvadrāta elements).</summary>
        static bool[,] DilateSquare(bool[,] m, int w, int h, int r)
        {
            var ii = IntegralImage(m, w, h);
            var res = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    res[y, x] = RectSum(ii, w, h, x - r, y - r, x + r, y + r) > 0;
            return res;
        }

        /// <summary>Morfoloģiskā "atvēršana" ar k x k kvadrātu: paliek tikai tie pikseļi, kurus pārklāj
        /// kāds pilnībā melns k x k kvadrāts. Tievas līnijas (plānākas par k) pazūd, biezi apgabali paliek.</summary>
        static bool[,] OpeningSquare(bool[,] m, int w, int h, int k)
        {
            var ii = IntegralImage(m, w, h);
            var anchors = new bool[h, w];
            for (int y = 0; y + k <= h; y++)
                for (int x = 0; x + k <= w; x++)
                    anchors[y, x] = RectSum(ii, w, h, x, y, x + k - 1, y + k - 1) == k * k;
            var ai = IntegralImage(anchors, w, h);
            var res = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    res[y, x] = RectSum(ai, w, h, x - k + 1, y - k + 1, x, y) > 0;
            return res;
        }

        /// <summary>Noņem savienotos gabalus (8-savienojamība), kas mazāki par minPixels pikseļiem.</summary>
        static bool[,] RemoveSmallComponents(bool[,] mask, int w, int h, int minPixels)
        {
            var res = (bool[,])mask.Clone();
            var visited = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!mask[y, x] || visited[y, x]) continue;
                    var comp = new List<Cell>();
                    var stack = new Stack<Cell>();
                    stack.Push(new Cell(y, x)); visited[y, x] = true;
                    while (stack.Count > 0)
                    {
                        var cur = stack.Pop(); comp.Add(cur);
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int ny = cur.R + dy, nx = cur.C + dx;
                                if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                                if (mask[ny, nx] && !visited[ny, nx]) { visited[ny, nx] = true; stack.Push(new Cell(ny, nx)); }
                            }
                    }
                    if (comp.Count < minPixels) foreach (var c in comp) res[c.R, c.C] = false;
                }
            return res;
        }

        static (HashSet<Cell> surface, HashSet<Cell> sensors, int nRows, int nCols) DetectSurfaceAndSensors(
            bool[,] blackMask, int width, int height, int step)
        {
            var rectBounds = FindRectangleBounds(blackMask, width, height);
            RectRejectNote = null;
            if (rectBounds.HasValue && !LegacyGrid)
            {
                // Taisnstūris tiek atrasts pēc garajiem melnajiem posmiem, tāpēc arī TUKŠS RĀMIS vai kontūra (piem. 800x600 rāmis ar
                // 5 px līniju) izskatās pēc "taisnstūra" - ar milzīgu "sensoru" zonu iekšā. Ja taisnstūra iekšpusē melns ir tikai neliela
                // daļa, tā nav aizpildīta virsma, bet tieva līnija - apstrādājam kā līniju.
                var rb0 = rectBounds.Value;
                long area = (long)(rb0.x1 - rb0.x0 + 1) * (rb0.y1 - rb0.y0 + 1), black = 0;
                for (int yy = rb0.y0; yy <= rb0.y1; yy++)
                    for (int xx = rb0.x0; xx <= rb0.x1; xx++)
                        if (blackMask[yy, xx]) black++;
                double fill = black / (double)area;
                if (fill < MIN_RECT_FILL)
                {
                    RectRejectNote = $"[DIAG] atrastais \"taisnstūris\" x {rb0.x0}..{rb0.x1}, y {rb0.y0}..{rb0.y1} atmests: iekšpusē melns ir tikai {fill * 100:0.0}% (< {MIN_RECT_FILL * 100:0}%) - tā ir kontūra/rāmis, nevis aizpildīta virsma; apstrādāts kā tieva līnija";
                    rectBounds = null;
                }
            }

            var lineMask = (bool[,])blackMask.Clone();
            int rx0 = 0, ry0 = 0, rx1 = 0, ry1 = 0;
            bool haveRect = rectBounds.HasValue;
            if (haveRect)
            {
                var rb = rectBounds.Value;
                rx0 = rb.x0; rx1 = rb.x1; ry0 = rb.y0; ry1 = rb.y1;
                for (int y = ry0; y <= ry1; y++)
                    for (int x = rx0; x <= rx1; x++)
                        lineMask[y, x] = false; // izņemam taisnstūri no "līniju" maskas
            }

            // JAUNS: BIEZAS (aizpildītas) melnās daļas ārpus atrastā taisnstūra. Taisnstūri nosaka pēc garākā
            // melnā posma, tāpēc formas, kas nav tīrs taisnstūris (piem. ar izliektu izgriezumu), tiktu
            // apgrieztas. Šeit atrodam visas daļas, kurās ietilpst DENSE_MIN_THICKNESS kvadrāts, un tās
            // apstrādājam kā virsmu (nevis kā tievu līniju). Ja tādu nav, nekas nemainās.
            bool[,] extraMask = null;
            {
                var opened = OpeningSquare(blackMask, width, height, DENSE_MIN_THICKNESS);
                var core = new bool[height, width];
                bool anyCore = false;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (opened[y, x] && !(haveRect && x >= rx0 && x <= rx1 && y >= ry0 && y <= ry1))
                        { core[y, x] = true; anyCore = true; }
                if (anyCore)
                {
                    var grown = DilateSquare(core, width, height, DENSE_GROW);
                    extraMask = new bool[height, width];
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            if (blackMask[y, x] && grown[y, x] && !(haveRect && x >= rx0 && x <= rx1 && y >= ry0 && y <= ry1))
                            { extraMask[y, x] = true; lineMask[y, x] = false; }
                    // atlikušie tievie gabali, kas ir tikai biezās daļas malu "mati", ir troksnis
                    lineMask = RemoveSmallComponents(lineMask, width, height, MIN_LINE_COMPONENT);
                }
                else if (haveRect && !LegacyGrid)
                {
                    // arī tad, ja ārpus taisnstūra nav biezas daļas: sīki melni gabaliņi (piem. 3 px izcilne pie malas)
                    // ir troksnis, nevis līnija - citādi tie pārvēršas par atsevišķām rūtiņām un traucē ceļam
                    lineMask = RemoveSmallComponents(lineMask, width, height, MIN_LINE_COMPONENT);
                }
            }

            bool anyLine = false;
            for (int y = 0; y < height && !anyLine; y++)
                for (int x = 0; x < width && !anyLine; x++)
                    if (lineMask[y, x]) anyLine = true;

            if (anyLine)
            {
                lineMask = Skeletonize(lineMask, width, height);
                // Mijiedarbīgi noņemam mazus ciklus (ieliektu stūru artefaktus)
                // UN īsus atzarus, kamēr neviens no abiem vairs neko nemaina -
                // viena veida tīrīšana dažkārt atklāj otra veida artefaktu.
                bool anyFix = true;
                int fixSafety = 0;
                while (anyFix && fixSafety < 30)
                {
                    fixSafety++;
                    anyFix = false;
                    anyFix |= RemoveSmallLoops(lineMask, width, height);
                    var beforeSpurs = lineMask;
                    lineMask = PruneSkeletonSpurs(lineMask, width, height, minBranchLength: 60);
                    // (PruneSkeletonSpurs pats jau ir stabils/iterē līdz galam, bet
                    // pārbaudam, vai tas kaut ko mainīja, salīdzinot pikseļu skaitu)
                    int c1 = 0, c2 = 0;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        { if (beforeSpurs[y, x]) c1++; if (lineMask[y, x]) c2++; }
                    if (c1 != c2) anyFix = true;
                }
                lineMask = KeepLargestComponent(lineMask, width, height);
            }

            // kombinētā (taisnstūris VAI līnija) maska pilnā robežlodziņa noteikšanai
            var combinedMask = new bool[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    combinedMask[y, x] = blackMask[y, x] || lineMask[y, x];
            var (fullX0, fullX1, fullY0, fullY1) = FindBlackBounds(combinedMask, width, height);

            int nCols = CellCount(fullX1 - fullX0 + 1, step);
            int nRows = CellCount(fullY1 - fullY0 + 1, step);

            var lineCellsOrdered = TraceSkeletonToGridCells(lineMask, width, height, fullX0, fullY0, step);
            var lineCellsSet = new HashSet<Cell>(lineCellsOrdered);

            // SVARĪGI: visi zemāk esošie labojumi (spuru/ciklu tīrīšana,
            // spraugu aizpilde) attiecas TIKAI uz TIEVĀS LĪNIJAS šūnām -
            // NEKAD uz blīvā taisnstūra šūnām (tur KATRA iekšējā šūna
            // dabiski ir grāds-4, "zara punkts" šai nozīmē neko, un
            // mēģinājums to "tīrīt" uz tūkstošiem šūnu būtu ārkārtīgi lēns
            // un nevajadzīgs). Tāpēc šīs funkcijas palaižam PIRMS taisnstūra
            // šūnu pievienošanas klāt.
            PruneGridSpurs(lineCellsSet);
            FillSingleCellGaps(lineCellsSet, new HashSet<Cell>(), blackMask, fullX0, fullY0, step, width, height);
            PruneGridSpurs(lineCellsSet);
            RemoveSmallGridLoops(lineCellsSet);
            PruneGridSpurs(lineCellsSet);

            var surfaceCells = new HashSet<Cell>(lineCellsSet);
            var sensorCells = new HashSet<Cell>();
            var detInfo = new DetectionInfo { HaveRect = haveRect, Rx0 = rx0, Rx1 = rx1, Ry0 = ry0, Ry1 = ry1,
                                              FullX0 = fullX0, FullY0 = fullY0, Step = step,
                                              LineSurface = new HashSet<Cell>(lineCellsSet) };

            if (haveRect)
            {
                var rectGridLocal = BuildGridMajority(blackMask, rx0, rx1, ry0, ry1, step, Math.Min(fullX1, width - 1), Math.Min(fullY1, height - 1));
                int rowOffset = (ry0 - fullY0) / step;
                int colOffset = (rx0 - fullX0) / step;
                foreach (var kv in rectGridLocal)
                {
                    var gr = kv.Key.R + rowOffset;
                    var gc = kv.Key.C + colOffset;
                    var gCell = new Cell(gr, gc);
                    if (kv.Value) { surfaceCells.Add(gCell); detInfo.RectSurface.Add(gCell); }
                    else sensorCells.Add(gCell);
                }
            }

            // biezās daļas ārpus taisnstūra -> rūtiņas pēc vairākuma (globālajā rūtiņu tīklā)
            if (extraMask != null)
            {
                for (int r = 0; r < nRows; r++)
                    for (int c = 0; c < nCols; c++)
                    {
                        int cnt = 0, tot = 0;
                        for (int dy = 0; dy < step; dy++)
                            for (int dx = 0; dx < step; dx++)
                            {
                                int py = fullY0 + r * step + dy, px = fullX0 + c * step + dx;
                                if (py < fullY0 || py > fullY1 || px < fullX0 || px > fullX1 || py >= height || px >= width) continue;
                                tot++;
                                if (extraMask[py, px] || (haveRect && blackMask[py, px] && px >= rx0 && px <= rx1 && py >= ry0 && py <= ry1)) cnt++;
                            }
                        if (tot > 0 && cnt * 2 >= tot)
                        {
                            var cell = new Cell(r, c);
                            if (!surfaceCells.Contains(cell) && !sensorCells.Contains(cell))
                            { surfaceCells.Add(cell); detInfo.ExtraSurface.Add(cell); }
                        }
                    }
            }
            // PĀRA IZMĒRS: ja visā garumā rindu vai visā platumā kolonnu skaits ir NEPĀRA, pievienojam vienu rindu
            // (apakšā) un/vai kolonnu (labajā pusē) - kopējot attiecīgās pēdējās rindas/kolonnas biezās virsmas
            // (un sensoru) rūtiņas. Tā virsma, kurai ir pilns taisnstūris, iegūst pāra izmēru. Tievās līnijas
            // šeit netiek aiztiktas.
            if (!LegacyGrid)
            {
                Func<IEnumerable<Cell>> dense = () => detInfo.RectSurface.Concat(detInfo.ExtraSurface).Concat(detInfo.PadSurface).ToList();
                if (nRows % 2 == 1)
                {
                    int last = nRows - 1;
                    foreach (var c in dense().Where(c => c.R == last).ToList())
                    {
                        var n = new Cell(nRows, c.C);
                        if (surfaceCells.Add(n)) detInfo.PadSurface.Add(n);
                    }
                    foreach (var c in sensorCells.Where(c => c.R == last).ToList()) sensorCells.Add(new Cell(nRows, c.C));
                    nRows++;
                }
                if (nCols % 2 == 1)
                {
                    int last = nCols - 1;
                    foreach (var c in dense().Where(c => c.C == last).ToList())
                    {
                        var n = new Cell(c.R, nCols);
                        if (surfaceCells.Add(n)) detInfo.PadSurface.Add(n);
                    }
                    foreach (var c in sensorCells.Where(c => c.C == last).ToList()) sensorCells.Add(new Cell(c.R, nCols));
                    nCols++;
                }
            }
            LastDetection = detInfo;
            return (surfaceCells, sensorCells, nRows, nCols);
        }

        /// <summary>Aizpilda VIENAS rūtiņas spraugas starp divām surfaceCells
        /// rūtiņām, kas atrodas tieši 2 soļu attālumā (taisnā, 4-virzienu
        /// virzienā) ar tukšu rūtiņu pa vidu - BET TIKAI tad, ja šajā vidus
        /// rūtiņā oriģinālajā (pirms-skeletonize) attēlā PATIEŠĀM ir melni
        /// pikseļi! Tas ir KRITISKI svarīgi: ja vidus rūtiņā NAV NEVIENA
        /// melnā pikseļa, tā ir PATIESĀ, apzināti zīmētā sprauga (ceļa
        /// galapunkts) - to NEDRĪKST aizpildīt. Ja TUR IR melni pikseļi,
        /// tas ir tikai skeleta/režģa kvantēšanas artefakts (ieliekta stūra
        /// "gandrīz-trāpījums") - to droši var aizpildīt.</summary>
        static void FillSingleCellGaps(HashSet<Cell> surfaceCells, HashSet<Cell> sensorCells,
            bool[,] blackMask, int x0, int y0, int step, int width, int height)
        {
            Func<Cell, bool> hasBlackPixels = cell =>
            {
                int py0 = y0 + cell.R * step, px0 = x0 + cell.C * step;
                for (int dy = 0; dy < step; dy++)
                {
                    int py = py0 + dy;
                    if (py < 0 || py >= height) continue;
                    for (int dx = 0; dx < step; dx++)
                    {
                        int px = px0 + dx;
                        if (px < 0 || px >= width) continue;
                        if (blackMask[py, px]) return true;
                    }
                }
                return false;
            };

            var toAdd = new List<Cell>();
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            foreach (var cell in surfaceCells)
            {
                for (int i = 0; i < 4; i++)
                {
                    var mid = new Cell(cell.R + dr[i], cell.C + dc[i]);
                    var far = new Cell(cell.R + 2 * dr[i], cell.C + 2 * dc[i]);
                    if (!surfaceCells.Contains(mid) && !sensorCells.Contains(mid) && surfaceCells.Contains(far)
                        && hasBlackPixels(mid))
                        toAdd.Add(mid);
                }
            }
            foreach (var c in toAdd) surfaceCells.Add(c);
        }

        // ================== HAMILTONA CEĻA MEKLĒTĀJS ==================

        static List<Cell> Neighbors(Cell cell, HashSet<Cell> cellSet)
        {
            var result = new List<Cell>();
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                var nb = new Cell(cell.R + dr[i], cell.C + dc[i]);
                if (cellSet.Contains(nb)) result.Add(nb);
            }
            return result;
        }

        /// <summary>Tīra ĪSUS strupceļa "izciļņus" (grāds&lt;=1 galapunktus) pašā
        /// RŪTIŅU grafā (nevis pikseļu skeletā) - šādi rodas, kad 4px režģa
        /// kvantēšana reproducē pikseļu trasējuma nelielu "turp un atpakaļ"
        /// svārstību kā atsevišķu, īsu rūtiņas zaru (lai gan PATS pikseļu
        /// skelets tajā vietā ir pilnīgi tīrs/bez zariem). Noņem tikai ĻOTI
        /// ĪSUS (&lt;= maxSpurLen rūtiņu) zarus, lai NEKAD neaiztiktu īstos,
        /// garos ceļa galapunktus.</summary>
        static void PruneGridSpurs(HashSet<Cell> surfaceCells, int maxSpurLen = 4)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var endpoints = surfaceCells.Where(c => Neighbors(c, surfaceCells).Count <= 1).ToList();
                foreach (var ep in endpoints)
                {
                    if (!surfaceCells.Contains(ep)) continue;
                    var branch = new List<Cell> { ep };
                    Cell? prev = null;
                    Cell cur = ep;
                    while (true)
                    {
                        var nbs = Neighbors(cur, surfaceCells)
                            .Where(n => !n.Equals(prev ?? new Cell(int.MinValue, int.MinValue))).ToList();
                        if (nbs.Count != 1) break;
                        prev = cur; cur = nbs[0]; branch.Add(cur);
                        if (branch.Count > maxSpurLen) break;
                    }
                    if (branch.Count <= maxSpurLen && branch.Count > 1)   // izolētai rūtiņai (branch.Count == 1) nav ko noņemt
                    {
                        for (int i = 0; i < branch.Count - 1; i++) surfaceCells.Remove(branch[i]);
                        changed = true;
                    }
                }
            }
        }

        /// <summary>TIEŠI TĀDA PATI loģika kā RemoveSmallLoops (pikseļu
        /// skeleta cikliem), BET piemērota RŪTIŅU grafam (4-virzienu
        /// Neighbors, nevis 8-savienojamības PxNeighbors) - rūtiņu
        /// kvantēšana dažviet reproducē pikseļu trasējuma smalku "turp un
        /// atpakaļ" kā īstu mazu CIKLU (nevis vienkāršu strupceļa izciļņu),
        /// ko PruneGridSpurs nepamana.</summary>
        static void RemoveSmallGridLoops(HashSet<Cell> surfaceCells, int maxChainLen = 10)
        {
            int totalSize = surfaceCells.Count;
            bool changed = true;
            int safety = 0;
            while (changed && safety < 2000)
            {
                safety++;
                changed = false;
                var branchPoints = surfaceCells.Where(p => Neighbors(p, surfaceCells).Count >= 3).ToList();

                foreach (var bp in branchPoints)
                {
                    if (changed) break;
                    if (!surfaceCells.Contains(bp) || Neighbors(bp, surfaceCells).Count < 3) continue;
                    var dirs = Neighbors(bp, surfaceCells);

                    foreach (var startDir in dirs)
                    {
                        if (changed) break;
                        var chain = new List<Cell> { startDir };
                        Cell prev = bp;
                        Cell cur = startDir;
                        for (int step = 0; step < maxChainLen; step++)
                        {
                            foreach (var c in chain) surfaceCells.Remove(c);
                            bool ok = IsSingleComponentGrid(surfaceCells, totalSize - chain.Count);
                            if (ok)
                            {
                                changed = true;
                                totalSize -= chain.Count;
                                break;
                            }
                            foreach (var c in chain) surfaceCells.Add(c);

                            var nbs = Neighbors(cur, surfaceCells).Where(n => !n.Equals(prev) && !chain.Contains(n)).ToList();
                            if (nbs.Count != 1) break;
                            prev = cur; cur = nbs[0]; chain.Add(cur);
                        }
                    }
                }
            }
        }

        static bool IsSingleComponentGrid(HashSet<Cell> coords, int expectedSize)
        {
            if (coords.Count == 0) return expectedSize == 0;
            var start = coords.First();
            var visited = new HashSet<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var nb in Neighbors(cur, coords))
                    if (!visited.Contains(nb)) { visited.Add(nb); queue.Enqueue(nb); }
            }
            return visited.Count == expectedSize;
        }

        static int CountFreeNeighbors(Cell cell, HashSet<Cell> cellSet, HashSet<Cell> visited)
        {
            int n = 0;
            foreach (var nb in Neighbors(cell, cellSet))
                if (!visited.Contains(nb)) n++;
            return n;
        }

        /// <summary>ITERATĪVA (steka-balstīta) versija - bez rekursijas, der
        /// jebkuram rūtiņu skaitam bez steka pārpildes riska.</summary>
        static List<Cell> FindHamiltonianPath(HashSet<Cell> cellSet, Cell start, bool requireClosed, long timeBudgetSteps = 3_000_000)
        {
            int total = cellSet.Count;
            var visited = new HashSet<Cell> { start };
            var path = new List<Cell> { start };
            var candStack = new List<List<Cell>> { null };
            var idxStack = new List<int> { 0 };
            long steps = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportMs = 0;
            int bestDepth = 1;
            LastSearchTimedOut = false;
            LastSearchBestDepth = 1;

            while (true)
            {
                steps++;
                if (steps > timeBudgetSteps) { LastSearchBestDepth = bestDepth; return null; }
                if (path.Count > bestDepth) bestDepth = path.Count;
                if ((steps & 0xFF) == 0 && SearchTimeCheck(sw, ref lastReportMs, steps, path.Count, bestDepth, total, requireClosed))
                { LastSearchTimedOut = true; LastSearchBestDepth = bestDepth; return null; }

                if (path.Count == total)
                {
                    var current = path[path.Count - 1];
                    bool found = requireClosed ? Neighbors(current, cellSet).Contains(start) : true;
                    if (found) return path;
                }
                else
                {
                    int depth = path.Count - 1;
                    if (candStack[depth] == null)
                    {
                        var current = path[path.Count - 1];
                        var candidates = Neighbors(current, cellSet).Where(nb => !visited.Contains(nb)).ToList();
                        candidates.Sort((a, b) => CountFreeNeighbors(a, cellSet, visited).CompareTo(CountFreeNeighbors(b, cellSet, visited)));
                        candStack[depth] = candidates;
                        idxStack[depth] = 0;
                    }

                    var cands = candStack[depth];
                    if (idxStack[depth] < cands.Count)
                    {
                        var nb = cands[idxStack[depth]];
                        idxStack[depth]++;
                        visited.Add(nb);
                        path.Add(nb);
                        candStack.Add(null);
                        idxStack.Add(0);
                        continue;
                    }
                }

                if (path.Count <= 1) { LastSearchBestDepth = bestDepth; return null; }
                candStack.RemoveAt(candStack.Count - 1);
                idxStack.RemoveAt(idxStack.Count - 1);
                var removed = path[path.Count - 1];
                path.RemoveAt(path.Count - 1);
                visited.Remove(removed);
            }
        }

        // ================== SLĒGTA CIKLA BŪVĒŠANA: 2-FAKTORS + CIKLU SAPLŪDINĀŠANA ==================
        // Aizpildītām formām (kur katrai rūtiņai ≥2 kaimiņu un šaha krāsojums ir līdzsvarots) rekursīvā meklēšana
        // iestrēgst tuvu beigām. Šī metode ir konstruktīva: 1) atrod 2-faktoru (apakšgrafu, kurā KATRAI rūtiņai ir
        // tieši 2 kaimiņi - tas ir saraksts slēgtu ciklu, kas kopā apmeklē visas rūtiņas) kā divpusēju b-sapārojumu;
        // 2) savieno atsevišķos ciklus: ja 2x2 kvadrātā divas pretējas malas pieder DIVIEM dažādiem cikliem,
        // tās aizstāj ar pārējām divām malām - ciklu skaits samazinās par 1.

        static bool HasEdge(int[][] nb, int x, int y) { return nb[x][0] == y || nb[x][1] == y; }

        static void ReplaceNeighbor(int[][] nb, int x, int oldY, int newY)
        {
            if (nb[x][0] == oldY) nb[x][0] = newY; else nb[x][1] = newY;
        }

        static int UfFind(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

        static bool TryAugment(int a, List<int>[] adj, List<int>[] mate, bool[] visited, Random rng)
        {
            var order = adj[a].OrderBy(_ => rng.Next()).ToList();
            foreach (int w in order)
            {
                if (mate[a].Contains(w) || visited[w]) continue;
                visited[w] = true;
                if (mate[w].Count < 2) { mate[w].Add(a); mate[a].Add(w); return true; }
                foreach (int a2 in mate[w].ToList())
                {
                    mate[w].Remove(a2); mate[a2].Remove(w);          // pagaidām atbrīvojam a2 no w
                    if (TryAugment(a2, adj, mate, visited, rng)) { mate[w].Add(a); mate[a].Add(w); return true; }
                    mate[w].Add(a2); mate[a2].Add(w);                // neizdevās - atjaunojam
                }
            }
            return false;
        }

        static List<Cell> FindHamiltonianCycleByMerging(HashSet<Cell> cells, Cell start, double timeLimitSec)
        {
            List<Cell> result = null;
            // rekursija var būt dziļa - darbinām atsevišķā pavedienā ar lielu steku
            var th = new System.Threading.Thread(() => { result = HamiltonianCycleByMergingCore(cells, start, timeLimitSec); }, 256 * 1024 * 1024);
            th.Start(); th.Join();
            return result;
        }

        static List<Cell> HamiltonianCycleByMergingCore(HashSet<Cell> cells, Cell start, double timeLimitSec)
        {
            int n = cells.Count;
            if (n < 4 || n % 2 == 1 || !cells.Contains(start)) return null;
            var list = cells.ToList();
            var idx = new Dictionary<Cell, int>();
            for (int i = 0; i < n; i++) idx[list[i]] = i;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                adj[i] = Neighbors(list[i], cells).Select(c => idx[c]).ToList();
                if (adj[i].Count < 2) return null;
            }
            var blacks = Enumerable.Range(0, n).Where(i => ((list[i].R + list[i].C) & 1) == 0).ToList();
            if (blacks.Count * 2 != n) return null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int attempt = 0; attempt < 1000 && sw.Elapsed.TotalSeconds < timeLimitSec; attempt++)
            {
                var rng = new Random(12345 + attempt);
                var mate = new List<int>[n];
                for (int i = 0; i < n; i++) mate[i] = new List<int>(2);
                bool ok = true;
                foreach (int a in blacks.OrderBy(_ => rng.Next()))
                {
                    for (int rep = 0; rep < 2 && ok; rep++)
                        if (!TryAugment(a, adj, mate, new bool[n], rng)) ok = false;
                    if (!ok) break;
                }
                if (!ok) { if (attempt == 0) return null; continue; }   // 2-faktors neeksistē

                var nb = new int[n][];
                for (int i = 0; i < n; i++) nb[i] = new int[2];
                var fill = new int[n];
                foreach (int a in blacks)
                    foreach (int w in mate[a])
                    { nb[a][fill[a]++] = w; nb[w][fill[w]++] = a; }

                // ciklu marķējums
                var label = new int[n]; for (int i = 0; i < n; i++) label[i] = -1;
                int cycles = 0;
                for (int i = 0; i < n; i++)
                {
                    if (label[i] != -1) continue;
                    int prev = -1, cur = i;
                    do { label[cur] = cycles; int nx = nb[cur][0] != prev ? nb[cur][0] : nb[cur][1]; prev = cur; cur = nx; } while (cur != i);
                    cycles++;
                }
                var uf = new int[cycles]; for (int i = 0; i < cycles; i++) uf[i] = i;
                int components = cycles;

                bool merged = true;
                while (components > 1 && merged)
                {
                    merged = false;
                    foreach (var c in list)
                    {
                        var ca = new Cell(c.R, c.C); var cb = new Cell(c.R, c.C + 1);
                        var cc = new Cell(c.R + 1, c.C); var cd = new Cell(c.R + 1, c.C + 1);
                        if (!cells.Contains(cb) || !cells.Contains(cc) || !cells.Contains(cd)) continue;
                        int a = idx[ca], b = idx[cb], cI = idx[cc], d = idx[cd];
                        if (HasEdge(nb, a, b) && HasEdge(nb, cI, d) && UfFind(uf, label[a]) != UfFind(uf, label[cI]))
                        {   // augšējā un apakšējā mala -> kreisā un labā
                            ReplaceNeighbor(nb, a, b, cI); ReplaceNeighbor(nb, b, a, d);
                            ReplaceNeighbor(nb, cI, d, a); ReplaceNeighbor(nb, d, cI, b);
                            uf[UfFind(uf, label[a])] = UfFind(uf, label[cI]); components--; merged = true;
                        }
                        else if (HasEdge(nb, a, cI) && HasEdge(nb, b, d) && UfFind(uf, label[a]) != UfFind(uf, label[b]))
                        {   // kreisā un labā mala -> augšējā un apakšējā
                            ReplaceNeighbor(nb, a, cI, b); ReplaceNeighbor(nb, cI, a, d);
                            ReplaceNeighbor(nb, b, d, a); ReplaceNeighbor(nb, d, b, cI);
                            uf[UfFind(uf, label[a])] = UfFind(uf, label[b]); components--; merged = true;
                        }
                        if (components == 1) break;
                    }
                }
                if (components != 1) continue;   // šis 2-faktors netika savienots - mēģinām citu

                // apiet ciklu, sākot no 'start'
                var res = new List<Cell>(n);
                int s0 = idx[start], p = -1, q = s0;
                do { res.Add(list[q]); int nx = nb[q][0] != p ? nb[q][0] : nb[q][1]; p = q; q = nx; } while (q != s0 && res.Count <= n);
                if (res.Count == n) return res;
            }
            return null;
        }

        /// <summary>ATVĒRTS ceļš virsmai ar NEPĀRA rūtiņu skaitu: izņem vienu vairākuma krāsas rūtiņu v, uz atlikušās
        /// (pāra skaita, līdzsvarotās) virsmas atrod slēgtu ciklu, pārgriež to pie kaimiņa u un pievieno v kā ceļa
        /// galu. (Divpusējā grafā v nevar ievietot starp divām ciklā blakus rūtiņām, bet kā gals - var.)</summary>
        static List<Cell> FindHamiltonianPathByMerging(HashSet<Cell> cells, double timeLimitSec, Cell? preferredEnd = null)
        {
            int n = cells.Count;
            if (n < 5 || n % 2 == 0) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int even = cells.Count(c => ((c.R + c.C) & 1) == 0);
            int majority = even * 2 > n ? 0 : 1;            // vairākuma krāsa (viena rūtiņa vairāk)
            if (Math.Abs(even - (n - even)) != 1) return null;

            var rng = new Random(777);
            // vispirms mēģinām, lai ceļa viens gals ir izvēlētā (sākuma) rūtiņa
            var candidates = cells.Where(c => ((c.R + c.C) & 1) == majority)
                                  .OrderBy(c => preferredEnd.HasValue && c.Equals(preferredEnd.Value) ? 0 : 1)
                                  .ThenBy(c => preferredEnd.HasValue ? Math.Abs(c.R - preferredEnd.Value.R) + Math.Abs(c.C - preferredEnd.Value.C) : 0)   // ja izvēlētā šūna nederēs (krāsa), ņemam tuvāko
                                  .ThenBy(c => Neighbors(c, cells).Count).ThenBy(_ => rng.Next()).ToList();
            foreach (var v in candidates)
            {
                if (sw.Elapsed.TotalSeconds >= timeLimitSec) break;
                var nbs = Neighbors(v, cells);
                if (nbs.Count == 0) continue;
                var rest = new HashSet<Cell>(cells); rest.Remove(v);
                var u = nbs[0];
                var cyc = FindHamiltonianCycleByMerging(rest, u, Math.Min(2.0, Math.Max(0.2, timeLimitSec - sw.Elapsed.TotalSeconds)));
                if (cyc == null || cyc.Count != n - 1) continue;
                // cyc[0] = u; cyc[last] ir blakus u. Ceļš: cyc[1..last], cyc[0]=u, v
                var path = new List<Cell>(n);
                for (int i = 1; i < cyc.Count; i++) path.Add(cyc[i]);
                path.Add(cyc[0]);
                path.Add(v);
                path.Reverse();      // ceļš sākas ar v (tā ir izvēlētā sākuma rūtiņa, ja tā der), beidzas pie cyc[1]
                return path;
            }
            return null;
        }

        /// <summary>Pārbauda gala ceļu: katra rūtiņa tieši vienreiz un katrs solis uz blakus rūtiņu.</summary>
        static string VerifyPath(HashSet<Cell> cells, List<Cell> path, bool closed)
        {
            if (path.Count != cells.Count) return $"garums {path.Count} ≠ {cells.Count}";
            if (new HashSet<Cell>(path).Count != path.Count) return "ir atkārtotas rūtiņas";
            foreach (var c in path) if (!cells.Contains(c)) return "ceļš iziet ārpus virsmas";
            for (int i = 1; i < path.Count; i++)
                if (Math.Abs(path[i].R - path[i - 1].R) + Math.Abs(path[i].C - path[i - 1].C) != 1) return $"solis {i} nav uz blakus rūtiņu";
            if (closed && Math.Abs(path[0].R - path[path.Count - 1].R) + Math.Abs(path[0].C - path[path.Count - 1].C) != 1) return "pēdējā rūtiņa nav blakus pirmajai";
            return null;
        }

        static HashSet<Cell> RemainingReachableFrom(HashSet<Cell> cellSet, HashSet<Cell> visited, Cell source)
        {
            var seen = new HashSet<Cell> { source };
            var stack = new Stack<Cell>();
            stack.Push(source);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var nb in Neighbors(cur, cellSet))
                    if (!visited.Contains(nb) && !seen.Contains(nb))
                    {
                        seen.Add(nb);
                        stack.Push(nb);
                    }
            }
            return seen;
        }

        /// <summary>TĀDA PATI metode, TIKAI ar savienojamības apgriešanu
        /// (connectivity pruning) - lēnāka, bet droša REZERVES metode
        /// grūtiem (šauriem) gadījumiem.</summary>
        static List<Cell> FindHamiltonianPathPruned(HashSet<Cell> cellSet, Cell start, bool requireClosed, long timeBudgetSteps = 3_000_000)
        {
            int total = cellSet.Count;
            var visited = new HashSet<Cell> { start };
            var path = new List<Cell> { start };
            var candStack = new List<List<Cell>> { null };
            var idxStack = new List<int> { 0 };
            long steps = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportMs = 0;
            int bestDepth = 1;
            LastSearchTimedOut = false;
            LastSearchBestDepth = 1;

            while (true)
            {
                steps++;
                if (steps > timeBudgetSteps) { LastSearchBestDepth = bestDepth; return null; }
                if (path.Count > bestDepth) bestDepth = path.Count;
                if ((steps & 0xFF) == 0 && SearchTimeCheck(sw, ref lastReportMs, steps, path.Count, bestDepth, total, requireClosed))
                { LastSearchTimedOut = true; LastSearchBestDepth = bestDepth; return null; }

                if (path.Count == total)
                {
                    var current = path[path.Count - 1];
                    bool found = requireClosed ? Neighbors(current, cellSet).Contains(start) : true;
                    if (found) return path;
                }
                else
                {
                    int depth = path.Count - 1;
                    if (candStack[depth] == null)
                    {
                        var current = path[path.Count - 1];
                        var rawCandidates = Neighbors(current, cellSet).Where(nb => !visited.Contains(nb)).ToList();
                        var candidates = new List<Cell>();
                        foreach (var nb in rawCandidates)
                        {
                            int remainingNeeded = total - path.Count - 1;
                            if (remainingNeeded == 0) { candidates.Add(nb); continue; }
                            visited.Add(nb);
                            var reachable = RemainingReachableFrom(cellSet, visited, nb);
                            reachable.Remove(nb);
                            var stillNeeded = new HashSet<Cell>(cellSet.Where(c => !visited.Contains(c)));
                            visited.Remove(nb);
                            if (reachable.SetEquals(stillNeeded)) candidates.Add(nb);
                        }
                        candidates.Sort((a, b) => CountFreeNeighbors(a, cellSet, visited).CompareTo(CountFreeNeighbors(b, cellSet, visited)));
                        candStack[depth] = candidates;
                        idxStack[depth] = 0;
                    }

                    var cands = candStack[depth];
                    if (idxStack[depth] < cands.Count)
                    {
                        var nb = cands[idxStack[depth]];
                        idxStack[depth]++;
                        visited.Add(nb);
                        path.Add(nb);
                        candStack.Add(null);
                        idxStack.Add(0);
                        continue;
                    }
                }

                if (path.Count <= 1) { LastSearchBestDepth = bestDepth; return null; }
                candStack.RemoveAt(candStack.Count - 1);
                idxStack.RemoveAt(idxStack.Count - 1);
                var removed = path[path.Count - 1];
                path.RemoveAt(path.Count - 1);
                visited.Remove(removed);
            }
        }

        static string RenderFFseciba(HashSet<Cell> cellSet, List<Cell> path)
        {
            var order = new Dictionary<Cell, int>();
            for (int i = 0; i < path.Count; i++) order[path[i]] = i;
            int maxRow = cellSet.Max(c => c.R);
            int maxCol = cellSet.Max(c => c.C);
            var lines = new List<string>();
            for (int r = 0; r <= maxRow; r++)
            {
                var fields = new List<string>();
                for (int c = 0; c <= maxCol; c++)
                {
                    var cell = new Cell(r, c);
                    fields.Add(order.ContainsKey(cell) ? order[cell].ToString() : " ");
                }
                lines.Add(string.Join(",", fields) + ",");
            }
            return string.Join("\n", lines);
        }

        // ================== STARPDATI / DIAGNOSTIKA ==================

        /// <summary>No kurienes nāk virsmas rūtiņas (blīvais taisnstūris vai
        /// tievā līnija) - vajadzīgs, lai kartē varētu pārbaudīt, vai forma
        /// noteikta pareizi.</summary>
        class DetectionInfo
        {
            public bool HaveRect;
            public int Rx0, Rx1, Ry0, Ry1;          // atrastā taisnstūra robežas pikseļos
            public int FullX0, FullY0, Step;
            public HashSet<Cell> RectSurface = new HashSet<Cell>();
            public HashSet<Cell> LineSurface = new HashSet<Cell>();
            public HashSet<Cell> ExtraSurface = new HashSet<Cell>();   // biezās daļas ārpus taisnstūra
            public HashSet<Cell> PadSurface = new HashSet<Cell>();     // pievienotās rindas/kolonnas pāra izmēra dēļ
            public HashSet<Cell> RemovedSurface = new HashSet<Cell>(); // šaha krāsojuma izlīdzināšanai noņemtās rūtiņas
        }

        static DetectionInfo LastDetection;
        static string RectRejectNote;   // paziņojums, ja atrastais "taisnstūris" atmests kā kontūra

        // true = vecā uzvedība (pārpalikušie pikseļi pie malas tiek ignorēti, rindu/kolonnu skaits netiek izlīdzināts uz pāra)
        static bool LegacyGrid = false;

        /// <summary>Rūtiņu skaits dotajam pikseļu skaitam: pārpalikušie (1..step-1) pikseļi pie malas
        /// tiek uzskatīti par PILNU rūtiņu (noapaļo uz augšu); vecajā režīmā - uz leju.</summary>
        static int CellCount(int pixels, int step)
        {
            return LegacyGrid ? pixels / step : (pixels + step - 1) / step;
        }

        // meklēšanas laika ierobežojums sekundēs katram meklēšanas posmam (0 = bez ierobežojuma)
        static double SearchTimeLimitSec = 120;
        static bool LastSearchTimedOut;
        static int LastSearchBestDepth;

        /// <summary>Atgriež true, ja laika limits pārsniegts; ik pēc 10 s izdrukā progresu.</summary>
        static bool SearchTimeCheck(System.Diagnostics.Stopwatch sw, ref long lastReportMs, long steps,
                                    int curLen, int best, int total, bool closed)
        {
            long ms = sw.ElapsedMilliseconds;
            if (ms - lastReportMs >= 10000)
            {
                lastReportMs = ms;
                Console.WriteLine($"[..] meklēju {(closed ? "slēgtu bloku secību" : "atvērtu bloku secību")}: {ms / 1000} s, soļi {steps:N0}, ceļš {curLen}/{total}, dziļākais sasniegtais {best}/{total}");
            }
            return SearchTimeLimitSec > 0 && ms >= SearchTimeLimitSec * 1000.0;
        }

        class SurfaceAnalysis
        {
            public int Total, Even, Odd, Components;
            public List<int> CompSizes = new List<int>();
            public List<Cell> Degree0 = new List<Cell>();
            public List<Cell> Degree1 = new List<Cell>();
            public SortedDictionary<int, int> DegHist = new SortedDictionary<int, int>();
            public int RectCount, LineCount, BothCount, ExtraCount, PadCount;
            public string ClosedImpossibleReason;   // null = nav pierādījuma, ka neiespējams
            public string OpenImpossibleReason;
        }

        /// <summary>Aprēķina starpdatus par virsmas rūtiņu grafu (4-virzienu kaimiņi)
        /// un pārbauda NEPIECIEŠAMOS nosacījumus Hamiltona ciklam/ceļam. Ja kāds nosacījums
        /// nav izpildīts, cikls/ceļš pierādāmi neiespējams un meklēšana ir bezjēdzīga.</summary>
        static SurfaceAnalysis AnalyzeSurface(HashSet<Cell> surf, DetectionInfo info)
        {
            var a = new SurfaceAnalysis { Total = surf.Count };
            foreach (var c in surf)
            {
                if (((c.R + c.C) & 1) == 0) a.Even++; else a.Odd++;
                int d = Neighbors(c, surf).Count;
                a.DegHist[d] = a.DegHist.ContainsKey(d) ? a.DegHist[d] + 1 : 1;
                if (d == 0) a.Degree0.Add(c);
                if (d == 1) a.Degree1.Add(c);
                if (info != null)
                {
                    bool r = info.RectSurface.Contains(c), l = info.LineSurface.Contains(c), ex = info.ExtraSurface.Contains(c);
                    if (info.PadSurface.Contains(c)) a.PadCount++; else if (ex) a.ExtraCount++; else if (r && l) a.BothCount++; else if (r) a.RectCount++; else if (l) a.LineCount++;
                }
            }

            var seen = new HashSet<Cell>();
            foreach (var c in surf)
            {
                if (seen.Contains(c)) continue;
                int size = 0;
                var stack = new Stack<Cell>(); stack.Push(c); seen.Add(c);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop(); size++;
                    foreach (var nb in Neighbors(cur, surf))
                        if (!seen.Contains(nb)) { seen.Add(nb); stack.Push(nb); }
                }
                a.CompSizes.Add(size);
            }
            a.Components = a.CompSizes.Count;
            a.CompSizes.Sort((x, y) => y.CompareTo(x));

            int diff = Math.Abs(a.Even - a.Odd);
            var closed = new List<string>();
            if (a.Components > 1) closed.Add($"virsma sadalīta {a.Components} atsevišķos gabalos");
            if (a.Total % 2 == 1) closed.Add($"rūtiņu skaits ({a.Total}) ir NEPĀRA, bet slēgtai bloku secībai tam jābūt pāra");
            else if (diff != 0) closed.Add($"šaha krāsojuma klases nav vienādas (pāra {a.Even}, nepāra {a.Odd}), slēgtai bloku secībai tām jābūt vienādām");
            int low = a.Degree0.Count + a.Degree1.Count;
            if (low > 0) closed.Add($"{low} rūtiņām ir mazāk nekā 2 kaimiņu (slēgtā secībā katrai jābūt vismaz 2)");
            a.ClosedImpossibleReason = closed.Count > 0 ? string.Join("; ", closed) : null;

            var open = new List<string>();
            if (a.Components > 1) open.Add($"virsma sadalīta {a.Components} atsevišķos gabalos (ceļš nevar pāriet no viena uz otru)");
            if (a.Degree1.Count > 2) open.Add($"{a.Degree1.Count} rūtiņām ir tikai 1 kaimiņš, bet ceļam ir tikai 2 gali");
            if (diff > 1) open.Add($"šaha krāsojuma starpība ir {diff} (pāra {a.Even}, nepāra {a.Odd}), ceļam tā var būt ne vairāk kā 1");
            else if (a.Degree1.Count > 0)
            {
                // galapunktu krāsas: pāra skaitam abi gali ir dažādu krāsu, nepāra skaitam - abi vairākuma krāsā
                int majority = a.Even >= a.Odd ? 0 : 1;
                if (a.Total % 2 == 1)
                {
                    int bad = a.Degree1.Count(c => ((c.R + c.C) & 1) != majority);
                    if (bad > 0) open.Add($"nepāra rūtiņu skaitam ceļa galiem jābūt vairākuma krāsā, bet {bad} no piespiedu galiem (1 kaimiņš) ir pretējā krāsā");
                }
                else if (a.Degree1.Count == 2 && (((a.Degree1[0].R + a.Degree1[0].C) & 1) == ((a.Degree1[1].R + a.Degree1[1].C) & 1)))
                    open.Add("pāra rūtiņu skaitam ceļa galiem jābūt dažādu krāsu, bet abi piespiedu gali ir vienas krāsas");
            }
            a.OpenImpossibleReason = open.Count > 0 ? string.Join("; ", open) : null;
            return a;
        }

        // ================== ŠAHA KRĀSOJUMA IZLĪDZINĀŠANA ==================
        const int MAX_BALANCE_REMOVALS = 12;   // lielāku starpību automātiski neizlīdzina (būtu jānoņem par daudz rūtiņu)

        static bool IsConnected(HashSet<Cell> surf)
        {
            if (surf.Count == 0) return true;
            var seen = new HashSet<Cell>();
            var stack = new Stack<Cell>();
            var first = surf.First();
            stack.Push(first); seen.Add(first);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var nb in Neighbors(cur, surf))
                    if (seen.Add(nb)) stack.Push(nb);
            }
            return seen.Count == surf.Count;
        }

        /// <summary>Vai rūtiņu c var droši noņemt: katram tās kaimiņam jāpaliek vismaz 2 kaimiņiem
        /// un virsmai jāpaliek VIENAM savienotam gabalam.</summary>
        static bool IsSafeToRemove(HashSet<Cell> surf, Cell c)
        {
            foreach (var nb in Neighbors(c, surf))
                if (Neighbors(nb, surf).Count - 1 < 2) return false;
            surf.Remove(c);
            bool ok = IsConnected(surf);
            surf.Add(c);
            return ok;
        }

        /// <summary>Aizpildītas virsmas "izvirzījumi": rūtiņas ar VIENU kaimiņu, kas pieder BIEZAJAI virsmai (nevis tievai līnijai). Tādi rodas
        /// no rasterizācijas (piem. šaurs "kakliņš" starp zvaigznes stariem) un NAV īsti līnijas gali. Ar tādu šūnu forma tiek uzskatīta par
        /// "līniju ar galiem" un krāsu izlīdzināšana tiek izlaista. Tāpēc tādas šūnas atstājam TUKŠAS (tās vēlāk var aizpildīt kā pārējās).
        /// Sākuma šūna netiek aiztikta. Atgriež noņemto šūnu sarakstu.</summary>
        static List<Cell> RemoveDenseSpurs(HashSet<Cell> surf, DetectionInfo info, int maxRemovals)
        {
            var removed = new List<Cell>();
            if (info == null || surf.Count == 0) return removed;
            var startCell = surf.OrderBy(c => c.R).ThenBy(c => c.C).First();
            for (int i = 0; i < maxRemovals; i++)
            {
                Cell? spur = null;
                foreach (var c in surf.OrderBy(q => q.R).ThenBy(q => q.C))
                {
                    if (c.Equals(startCell) || Neighbors(c, surf).Count != 1 || info.LineSurface.Contains(c)) continue;
                    if (info.RectSurface.Contains(c) || info.ExtraSurface.Contains(c) || info.PadSurface.Contains(c)) { spur = c; break; }
                }
                if (spur == null) break;
                surf.Remove(spur.Value);
                removed.Add(spur.Value);
            }
            return removed;
        }

        /// <summary>Attālums (rūtiņās, 4-virzienu) no katras virsmas rūtiņas līdz tuvākajai NE-virsmas rūtiņai
        /// (sensors, tukšums vai attēla mala). 1 = rūtiņa pieskaras malai.</summary>
        static Dictionary<Cell, int> DistanceToOutside(HashSet<Cell> surf)
        {
            var dist = new Dictionary<Cell, int>();
            var q = new Queue<Cell>();
            foreach (var c in surf)
                if (Neighbors(c, surf).Count < 4) { dist[c] = 1; q.Enqueue(c); }
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var nb in Neighbors(cur, surf))
                    if (!dist.ContainsKey(nb)) { dist[nb] = dist[cur] + 1; q.Enqueue(nb); }
            }
            return dist;
        }

        /// <summary>Meklē labāko TAISNU DIAGONĀLU ķēdi no 'len' vienas krāsas (majority) rūtiņām, ko var droši atstāt tukšas:
        /// pēc noņemšanas katrai rūtiņai jāpaliek ≥2 kaimiņiem un virsmai jāpaliek vienam gabalam. Vienas krāsas rūtiņas
        /// nekad nav blakus ar malu (blakus rūtiņām krāsas vienmēr ir pretējas), tāpēc diagonāle - kur katra pieskaras
        /// nākamajai ar stūri - ir visciešākais izkārtojums, kas vēl ir drošs. Priekšroka: tuvāk virsmas malai, pēc
        /// pirmās ķēdes - tuvāk jau izņemtajām rūtiņām.</summary>
        static List<Cell> FindBestDiagonalChain(HashSet<Cell> surf, Dictionary<Cell, int> degree, Dictionary<Cell, int> dist,
                                                int majority, int len, Cell startCell, List<Cell> alreadyRemoved,
                                                int minDist, Func<bool> verify)
        {
            var cands = new List<KeyValuePair<double, List<Cell>>>();
            int[][] dirs = { new[] { 1, 1 }, new[] { 1, -1 } };
            foreach (var p in surf)
            {
                if (((p.R + p.C) & 1) != majority) continue;
                foreach (var d in dirs)
                {
                    var chain = new List<Cell>(len);
                    bool ok = true;
                    for (int i = 0; i < len; i++)
                    {
                        var c = new Cell(p.R + i * d[0], p.C + i * d[1]);
                        if (!surf.Contains(c) || c.Equals(startCell)) { ok = false; break; }
                        chain.Add(c);
                    }
                    if (!ok) continue;
                    var set = new HashSet<Cell>(chain);
                    var lost = new Dictionary<Cell, int>();
                    foreach (var c in chain)
                        foreach (var nb in Neighbors(c, surf))
                            if (!set.Contains(nb)) lost[nb] = (lost.ContainsKey(nb) ? lost[nb] : 0) + 1;
                    bool safe = true;
                    foreach (var kv in lost) if (degree[kv.Key] - kv.Value < 2) { safe = false; break; }
                    if (!safe) continue;
                    if (chain.Min(c => dist[c]) < minDist) continue;   // pārāk tuvu malai -> var rasties "kabata" ar nesakritīgu paritāti
                    double score = chain.Sum(c => (double)dist[c]);
                    if (alreadyRemoved.Count > 0)
                    {
                        int dMin = int.MaxValue;
                        foreach (var c in chain) foreach (var q in alreadyRemoved)
                            dMin = Math.Min(dMin, Math.Max(Math.Abs(c.R - q.R), Math.Abs(c.C - q.C)));
                        score += 1000.0 * dMin;
                    }
                    else score -= 0.01 * (Math.Abs(chain[0].R - startCell.R) + Math.Abs(chain[0].C - startCell.C));
                    score += chain[0].R * 1e-4 + chain[0].C * 1e-6;
                    cands.Add(new KeyValuePair<double, List<Cell>>(score, chain));
                }
            }
            int verifyBudget = 40;   // cik kandidātus maksimāli pārbaudīt ar īstu cikla meklēšanu
            foreach (var kv in cands.OrderBy(k => k.Key).Take(300))
            {
                foreach (var c in kv.Value) surf.Remove(c);
                bool good = IsConnected(surf);
                if (good && verify != null)
                {
                    if (verifyBudget-- <= 0) good = false;
                    else good = verify();          // pārbaude ar jau izņemtu ķēdi: vai pa atlikušo virsmu cikls tiešām atrodams
                }
                foreach (var c in kv.Value) surf.Add(c);
                if (good) return kv.Value;
                if (verify != null && verifyBudget <= 0) break;
            }
            return null;
        }

        /// <summary>Ja šaha krāsojuma klases nav vienādas, atstāj TUKŠAS tik vairākuma krāsas rūtiņu, cik ir starpība,
        /// un cenšas tās izvietot KOPĀ - kā vienu taisnu diagonālu ķēdi (katra pieskaras nākamajai ar stūri). Ja garu
        /// ķēdi droši izvietot nevar, ņem īsākas, bet cieši blakus jau izņemtajām. Sākuma rūtiņa (kreisā augšējā) netiek
        /// noņemta. Katra tukšā šūna ir atsevišķs slēgts bloks, ko vēlāk var aizpildīt ar mazu HC bloku.</summary>
        static List<Cell> BalanceSurface(HashSet<Cell> surf, SurfaceAnalysis a, out string message, bool preferClosed = false)
        {
            message = null;
            int diff = Math.Abs(a.Even - a.Odd);
            if (diff == 0) return null;
            // ATVĒRTAI bloku secībai pietiek ar krāsu starpību 1 (gala HC tāpat ir vesels), tāpēc tukšas jāatstāj par vienu šūnu mazāk;
            // ar --slegta-secibu tiek meklēta SLĒGTA secība (starpība 0, vairāk tukšo šūnu)
            int target = preferClosed ? diff : diff - 1;
            if (target <= 0) return null;
            if (a.Degree0.Count + a.Degree1.Count > 0) return null;     // forma ar līnijas galiem: slēgts cikls tāpat nav iespējams, izlīdzināšana nav vajadzīga
            if (a.Components != 1)
            {
                message = "[INFO] Šaha krāsojuma izlīdzināšana izlaista: virsma sastāv no vairākiem gabaliem.";
                return null;
            }
            if (diff > MAX_BALANCE_REMOVALS)
            {
                message = $"[INFO] Šaha krāsojuma izlīdzināšana izlaista: starpība ({diff}) ir pārāk liela, lai to automātiski labotu (max {MAX_BALANCE_REMOVALS}).";
                return null;
            }
            int before = surf.Count;
            int majority = a.Even > a.Odd ? 0 : 1;
            var startCell = surf.OrderBy(c => c.R).ThenBy(c => c.C).First();
            var removed = new List<Cell>();
            int need = target;
            var chains = new List<List<Cell>>();
            while (need > 0)
            {
                var degree = surf.ToDictionary(c => c, c => Neighbors(c, surf).Count);
                var dist = DistanceToOutside(surf);
                List<Cell> best = null;
                for (int len = need; len >= 1 && best == null; len--)
                    for (int minDist = 3; minDist >= 1 && best == null; minDist--)
                    {
                        // ja šī ķēde izlīdzina visu starpību, pārbaudām, ka pa atlikušo virsmu cikls tiešām atrodams
                        Func<bool> verify = (len == need)
                            ? (preferClosed ? (Func<bool>)(() => FindHamiltonianCycleByMerging(surf, startCell, 1.5) != null)
                                            : (Func<bool>)(() => FindHamiltonianPathByMerging(surf, 1.5, startCell) != null))
                            : null;
                        best = FindBestDiagonalChain(surf, degree, dist, majority, len, startCell, removed, minDist, verify);
                    }
                if (best == null) break;
                foreach (var c in best) surf.Remove(c);
                removed.AddRange(best);
                chains.Add(best);
                need -= best.Count;
            }
            if (removed.Count == 0)
            {
                message = $"[INFO] Šaha krāsojuma starpība {diff} (pāra {a.Even} / nepāra {a.Odd}), bet drošu rūtiņu, ko atstāt tukšu, nav.";
                return null;
            }
            message = $"[INFO] Šaha krāsojuma izlīdzināšana: bija pāra {a.Even} / nepāra {a.Odd}" + (preferClosed ? "" : " (atvērtai bloku secībai pietiek ar starpību 1)") + $"; tukšas atstātas {removed.Count} rūtiņas, sabīdītas kopā " +
                      $"({chains.Count} {(chains.Count == 1 ? "ķēde" : "ķēdes")}): " + string.Join(" | ", chains.Select(ch => string.Join(" ", ch))) +
                      $" (virsma {before} -> {surf.Count}); sākuma rūtiņa {startCell} paliek virsmā." +
                      (need > 0 ? $" Starpība joprojām {need}." : "");
            return removed;
        }

        // ================== ATVĒRTA CEĻA STARPREZULTĀTS ==================

        static Color HsvToColor(double h, double s, double v)
        {
            h = h % 360; if (h < 0) h += 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        /// <summary>Atvērts ceļš netiek pieņemts kā gala rezultāts - tā vietā saglabā STARPREZULTĀTU:
        /// numurētu ceļu (_starprezultats.txt, tāds pats formāts kā FFseciba) un krāsainu attēlu
        /// (_starprezultats.png: ceļa secība krāsu gradientā, sākums un gals atzīmēti), lai var redzēt,
        /// kur ceļš beidzas, un izdarīt korekcijas.</summary>
        static void WriteOpenPathIntermediate(string outPath, HashSet<Cell> surf, HashSet<Cell> sens, List<Cell> path,
                                              DetectionInfo info, SurfaceAnalysis a)
        {
            string dir = Path.GetDirectoryName(outPath);
            string baseName = Path.GetFileNameWithoutExtension(outPath) + "_starprezultats";
            string basePath = string.IsNullOrEmpty(dir) ? baseName : Path.Combine(dir, baseName);
            string txtOut = SafeWrite(basePath + ".txt", fp => File.WriteAllText(fp, RenderFFseciba(surf, path), Utf8NoBom));
            string pngOut = basePath + ".png";

            var all = new List<Cell>(surf); all.AddRange(sens); all.AddRange(info != null ? info.RemovedSurface : new HashSet<Cell>());
            int maxR = all.Max(c => c.R), maxC = all.Max(c => c.C);
            const int cs = 12;
            int gridW = (maxC + 1) * cs, gridH = (maxR + 1) * cs;
            int imgW = Math.Max(gridW, 760);
            string openVerdict = "ATVĒRTA bloku secība (S = sākums, G = gals; gala HC no blokiem tāpat būs vesels). " + VerdictText(a, info != null ? info.RemovedSurface.Count : 0);
            int bannerH = BannerHeight(imgW, openVerdict);
            using (var bmp = new Bitmap(imgW, gridH + bannerH))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 30, 30));
                DrawBanner(g, imgW, bannerH, new List<KeyValuePair<Color, string>>
                {
                    new KeyValuePair<Color, string>(HsvToColor(260, 0.85, 0.95), "ceļa sākums (zils)"),
                    new KeyValuePair<Color, string>(HsvToColor(185, 0.85, 0.95), "ceļa beigas (ciāns)"),
                    new KeyValuePair<Color, string>(Color.White, "aizliegts (sensori)"),
                    new KeyValuePair<Color, string>(Color.FromArgb(220, 40, 40), "izņemtās (tukšās) šūnas"),
                    new KeyValuePair<Color, string>(Color.Yellow, "sākums / gals (gredzeni)")
                }, openVerdict);
                for (int i = 0; i < path.Count; i++)
                    using (var br = new SolidBrush(HsvToColor(260.0 - 75.0 * (i / (double)Math.Max(1, path.Count - 1)), 0.85, 0.95)))
                        g.FillRectangle(br, path[i].C * cs, path[i].R * cs, cs - 1, cs - 1);
                foreach (var c in sens)
                    g.FillRectangle(Brushes.White, c.C * cs, c.R * cs, cs - 1, cs - 1);
                if (info != null)
                    foreach (var c in info.RemovedSurface)
                        using (var br = new SolidBrush(Color.FromArgb(220, 40, 40)))
                            g.FillRectangle(br, c.C * cs, c.R * cs, cs - 1, cs - 1);
                var st = path[0]; var en = path[path.Count - 1];
                using (var pen = new Pen(Color.Yellow, 3))
                {
                    g.DrawEllipse(pen, st.C * cs - 3, st.R * cs - 3, cs + 5, cs + 5);
                    g.DrawEllipse(pen, en.C * cs - 3, en.R * cs - 3, cs + 5, cs + 5);
                }
                g.DrawString("S", new Font("Arial", 8, FontStyle.Bold), Brushes.Yellow, st.C * cs + cs + 4, st.R * cs - 4);
                g.DrawString("G", new Font("Arial", 8, FontStyle.Bold), Brushes.Yellow, en.C * cs + cs + 4, en.R * cs - 4);
                pngOut = SafeWrite(basePath + ".png", fp => bmp.Save(fp, System.Drawing.Imaging.ImageFormat.Png));
            }
            if (info != null && info.RemovedSurface.Count > 0)
                WriteRedLine("[UZMANĪBU] TUKŠĀS ŠŪNAS, ko vēlāk aizpildīt ar maziem HC blokiem: " + string.Join(" ", info.RemovedSurface));
            Console.WriteLine($"[STARPREZULTĀTS] saglabāts: {txtOut} (numurēta atvērtā bloku secība) un {pngOut} (krāsu gradients zils -> ciāns = ceļa secība; dzeltens gredzens S = sākums {path[0]}, G = gals {path[path.Count - 1]}; balts = aizliegts/sensori" + (info != null && info.RemovedSurface.Count > 0 ? "; sarkans = izņemtās (tukšās) šūnas" : "") + ")");
        }

        /// <summary>Īss slēdziens (kartes virsrakstam): vai slēgts cikls / ceļš pa šūnām ir iespējams.</summary>
        static string VerdictText(SurfaceAnalysis a, int removedCount)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Šaha krāsojums: pāra {a.Even} / nepāra {a.Odd}. ");
            if (a.ClosedImpossibleReason == null) sb.Append("Slēgta bloku secība IESPĒJAMA.");
            else if (a.OpenImpossibleReason == null) sb.Append("Slēgta bloku secība NEIESPĒJAMA (" + a.ClosedImpossibleReason + "), atvērta secība iespējama - gala HC no blokiem tāpat būs vesels.");
            else sb.Append("NEVIENA bloku secība nav iespējama: " + a.OpenImpossibleReason + ".");
            if (removedCount > 0) sb.Append($" Izņemtas (tukšas) {removedCount} šūnas.");
            return sb.ToString();
        }

        /// <summary>Joslas augstums, lai ietilptu viss slēdziena teksts (ar vārdu pārnešanu).</summary>
        static int BannerHeight(int width, string verdict)
        {
            using (var tmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmp))
            using (var f = new Font("Arial", 9, FontStyle.Bold))
            {
                var sz = g.MeasureString(verdict, f, width - 16);
                return 28 + (int)Math.Ceiling(sz.Height) + 6;
            }
        }

        /// <summary>Uzzīmē augšējo joslu ar krāsu skaidrojumu un slēdzienu; pēc tam zīmēšanas sākums tiek nobīdīts zem tās.</summary>
        static void DrawBanner(Graphics g, int width, int bannerH, IList<KeyValuePair<Color, string>> legend, string verdict)
        {
            g.FillRectangle(Brushes.Black, 0, 0, width, bannerH);
            using (var f = new Font("Arial", 9, FontStyle.Bold))
            using (var f2 = new Font("Arial", 9))
            {
                float x = 8;
                foreach (var item in legend)
                {
                    using (var br = new SolidBrush(item.Key)) g.FillRectangle(br, x, 6, 12, 12);
                    g.DrawRectangle(Pens.Gray, x, 6, 12, 12);
                    g.DrawString(item.Value, f2, Brushes.White, x + 16, 4);
                    x += 16 + g.MeasureString(item.Value, f2).Width + 12;
                }
                g.DrawString(verdict, f, Brushes.Yellow, new RectangleF(8, 24, width - 16, bannerH - 26));
            }
            g.TranslateTransform(0, bannerH);
        }

        /// <summary>Saglabā starpdatus: teksta karti (_karte.txt) un krāsainu attēlu (_karte.png),
        /// kā arī izdrukā diagnostiku konsolē.</summary>
        static void WriteIntermediateData(string outPath, HashSet<Cell> surf, HashSet<Cell> sens,
                                          DetectionInfo info, SurfaceAnalysis a)
        {
            string dir = Path.GetDirectoryName(outPath);
            string baseName = Path.GetFileNameWithoutExtension(outPath) + "_karte";
            string basePath = string.IsNullOrEmpty(dir) ? baseName : Path.Combine(dir, baseName);

            var all = new List<Cell>(surf); all.AddRange(sens); all.AddRange(info != null ? info.RemovedSurface : new HashSet<Cell>());
            int maxR = all.Max(c => c.R), maxC = all.Max(c => c.C);
            var deg1 = new HashSet<Cell>(a.Degree1);

            // ---- konsoles diagnostika ----
            Console.WriteLine("[DIAG] ---- starpdati ----");
            if (info != null && info.HaveRect)
                Console.WriteLine($"[DIAG] atrastais blīvais taisnstūris (px): x {info.Rx0}..{info.Rx1}, y {info.Ry0}..{info.Ry1}  = rūtiņās kolonnas {(info.Rx0 - info.FullX0) / info.Step}..{(info.Rx1 - info.FullX0) / info.Step}, rindas {(info.Ry0 - info.FullY0) / info.Step}..{(info.Ry1 - info.FullY0) / info.Step}");
            else
            {
                if (RectRejectNote != null) Console.WriteLine(RectRejectNote);
                Console.WriteLine("[DIAG] blīvs taisnstūris NAV atrasts (viss uzskatīts par tievu līniju)");
            }
            Console.WriteLine($"[DIAG] virsmas rūtiņas pēc izcelsmes: taisnstūris {a.RectCount}, biezās daļas ārpus taisnstūra {a.ExtraCount}, tikai tievā līnija {a.LineCount}, abas {a.BothCount}, pievienotās pāra izmēra dēļ {a.PadCount}");
            Console.WriteLine($"[DIAG] šaha krāsojums: pāra (R+C) {a.Even}, nepāra {a.Odd}, starpība {Math.Abs(a.Even - a.Odd)}");
            Console.WriteLine("[DIAG] kaimiņu skaita sadalījums: " + string.Join(", ", a.DegHist.Select(kv => kv.Key + "=>" + kv.Value)));
            Console.WriteLine($"[DIAG] savienotie gabali: {a.Components}" + (a.Components > 1 ? " (izmēri: " + string.Join(", ", a.CompSizes) + ")" : ""));
            Console.WriteLine($"[DIAG] rūtiņas ar 1 kaimiņu (piespiedu gali): {a.Degree1.Count}" +
                              (a.Degree1.Count > 0 ? " - " + string.Join(" ", a.Degree1.Take(20)) : ""));
            Console.WriteLine($"[DIAG] izolētas rūtiņas (0 kaimiņu): {a.Degree0.Count}" +
                              (a.Degree0.Count > 0 ? " - " + string.Join(" ", a.Degree0.Take(20)) : ""));
            Console.WriteLine("[DIAG] slēgta bloku secība: " + (a.ClosedImpossibleReason == null ? "pierādījuma, ka neiespējama, nav" : "PIERĀDĀMI NEIESPĒJAMA - " + a.ClosedImpossibleReason));
            Console.WriteLine("[DIAG] atvērta bloku secība: " + (a.OpenImpossibleReason == null ? "pierādījuma, ka neiespējama, nav" : "PIERĀDĀMI NEIESPĒJAMA - " + a.OpenImpossibleReason));

            // ---- teksta karte ----
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("KARTE: R/E/P/L/B = virsma, kas jāapmeklē (R taisnstūris, E biezā daļa ārpus taisnstūra, P pievienota pāra izmēra dēļ, L tieva līnija, B abi), S = sensors (aizliegta zona), X = izņemta (tukša) šūna, @ = piespiedu gals (1 kaimiņš), . = nav šūnas");
            sb.AppendLine($"Rindas 0..{maxR}, kolonnas 0..{maxC}. Virsma {a.Total}, sensori {sens.Count}, izņemtās (tukšās) šūnas {(info != null ? info.RemovedSurface.Count : 0)}.");
            sb.AppendLine("SLĒDZIENS: " + VerdictText(a, info != null ? info.RemovedSurface.Count : 0));
            sb.Append("    ");
            for (int c = 0; c <= maxC; c++) sb.Append(c % 10 == 0 ? (c / 10 % 10).ToString() : " ");
            sb.AppendLine();
            sb.Append("    ");
            for (int c = 0; c <= maxC; c++) sb.Append((c % 10).ToString());
            sb.AppendLine();
            for (int r = 0; r <= maxR; r++)
            {
                sb.Append(r.ToString().PadLeft(3)).Append(' ');
                for (int c = 0; c <= maxC; c++)
                {
                    var cell = new Cell(r, c);
                    char ch = '.';
                    if (sens.Contains(cell)) ch = 'S';
                    if (info != null && info.RemovedSurface.Contains(cell)) ch = 'X';
                    if (surf.Contains(cell))
                    {
                        bool rr = info != null && info.RectSurface.Contains(cell), ll = info != null && info.LineSurface.Contains(cell);
                        bool ee = info != null && info.ExtraSurface.Contains(cell);
                        bool pp = info != null && info.PadSurface.Contains(cell);
                        ch = pp ? 'P' : ee ? 'E' : (rr && ll) ? 'B' : rr ? 'R' : ll ? 'L' : '#';
                        if (deg1.Contains(cell)) ch = '@';
                    }
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            string mapTxtOut = SafeWrite(basePath + ".txt", fp => File.WriteAllText(fp, sb.ToString(), System.Text.Encoding.UTF8));
            string mapPngOut = basePath + ".png";

            // ---- krāsains attēls: zaļš = viss OK, balts = aizliegts (sensori), sarkans = izņemtās (tukšās) šūnas ----
            const int cs = 12;
            int gridW = (maxC + 1) * cs, gridH = (maxR + 1) * cs;
            int imgW = Math.Max(gridW, 700);
            string mapVerdict = VerdictText(a, info != null ? info.RemovedSurface.Count : 0);
            int bannerH = BannerHeight(imgW, mapVerdict);
            using (var bmp = new Bitmap(imgW, gridH + bannerH))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 30, 30));
                DrawBanner(g, imgW, bannerH, new List<KeyValuePair<Color, string>>
                {
                    new KeyValuePair<Color, string>(Color.FromArgb(0, 160, 0), "viss OK (jāapmeklē)"),
                    new KeyValuePair<Color, string>(Color.White, "aizliegts (sensori)"),
                    new KeyValuePair<Color, string>(Color.FromArgb(220, 40, 40), "izņemtās (tukšās) šūnas"),
                    new KeyValuePair<Color, string>(Color.Yellow, "piespiedu gals")
                }, mapVerdict);
                foreach (var cell in surf)
                    using (var br = new SolidBrush(Color.FromArgb(0, 160, 0)))
                        g.FillRectangle(br, cell.C * cs, cell.R * cs, cs - 1, cs - 1);
                foreach (var cell in sens)
                    g.FillRectangle(Brushes.White, cell.C * cs, cell.R * cs, cs - 1, cs - 1);
                if (info != null)
                    foreach (var cell in info.RemovedSurface)
                        using (var br = new SolidBrush(Color.FromArgb(220, 40, 40)))
                            g.FillRectangle(br, cell.C * cs, cell.R * cs, cs - 1, cs - 1);
                foreach (var cell in a.Degree1)
                    g.FillEllipse(Brushes.Yellow, cell.C * cs + 2, cell.R * cs + 2, cs - 5, cs - 5);
                using (var f = new Font("Arial", 7))
                using (var pen = new Pen(Color.FromArgb(90, 90, 90)))
                    for (int k = 0; k <= Math.Max(maxR, maxC); k += 10)
                    {
                        if (k <= maxC) { g.DrawLine(pen, k * cs, 0, k * cs, gridH); g.DrawString(k.ToString(), f, Brushes.Black, k * cs + 1, 0); }
                        if (k <= maxR) { g.DrawLine(pen, 0, k * cs, gridW, k * cs); g.DrawString(k.ToString(), f, Brushes.Black, 0, k * cs + 1); }
                    }
                mapPngOut = SafeWrite(basePath + ".png", fp => bmp.Save(fp, System.Drawing.Imaging.ImageFormat.Png));
            }
            Console.WriteLine($"[DIAG] saglabāts: {mapTxtOut} un {mapPngOut} (zaļš = viss OK, balts = aizliegts/sensori" + (info != null && info.RemovedSurface.Count > 0 ? ", sarkans = izņemtās (tukšās) šūnas" : "") + ", dzeltens = piespiedu gals)");
        }

        // ================== SARKANS PAZIŅOJUMS ==================
        /// <summary>Izdrukā rindu SARKANĀ krāsā (konsolē) un atjauno iepriekšējo krāsu.</summary>
        static void WriteRedLine(string text)
        {
            ConsoleColor old = Console.ForegroundColor;
            try { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(text); }
            finally { Console.ForegroundColor = old; }
        }

        // FFseciba TXT jāraksta UTF-8 BEZ BOM: ar BOM (EF BB BF) pirmais lauks ir "\ufeff6059", ko parasta utf-8 nolasīšana (piem. Python
        // open(...,encoding='utf-8')) neatpazīst kā skaitli - tad pirmā šūna (0,0) "pazūd" un bloku secībā rodas sprauga.
        static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

        // ================== DROŠA FAILU RAKSTĪŠANA ==================
        /// <summary>Ieraksta failu; ja tas ir aizņemts (parasti atvērts Excel vai teksta redaktorā, vai to izmanto cits process),
        /// rezultāts netiek zaudēts - tas tiek saglabāts ar nosaukumu _1, _2... Atgriež faktisko ceļu.</summary>
        static string SafeWrite(string path, Action<string> writer)
        {
            Exception first;
            try { writer(path); return path; }
            catch (Exception ex)    // IOException, UnauthorizedAccessException, GDI+ ExternalException (attēla saglabāšana) u.c.
            { first = ex; }
            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; i <= 20; i++)
            {
                string alt = Path.Combine(dir, name + "_" + i + ext);
                try
                {
                    writer(alt);
                    Console.WriteLine("[BRĪDINĀJUMS] Nevar ierakstīt " + path + " (" + first.Message.Split('\n')[0].Trim() + ").");
                    Console.WriteLine("[BRĪDINĀJUMS] Fails, visticamāk, ir atvērts citā programmā (Excel, teksta redaktors) vai to izmanto cits Png2Seciba process.");
                    Console.WriteLine("[BRĪDINĀJUMS] Rezultāts nav zaudēts - saglabāts kā " + alt + ". Aizver programmu, kas izmanto " + path + ", lai nākamreiz to varētu pārrakstīt.");
                    return alt;
                }
                catch (Exception) { }
            }
            Console.WriteLine("[KĻŪDA] Nevar ierakstīt " + path + " (nedz ar nosaukumiem _1.._20): " + first.Message);
            throw first;
        }

        // ================== TUKŠO ŠŪNU AIZPILDĪŠANA (zirdziņa cikla bloku saplūdināšana) ==================
        // Ja virsmai kāda šūna (6x6 bloks) palikusi tukša (piem. šaha krāsojuma izlīdzināšanas dēļ), šī šūna tiek
        // aizpildīta ar ATSEVIŠĶU slēgtu 6x6 zirdziņa ciklu, un tas tiek savienots ar jau esošo lielo ciklu ar divu
        // šķautņu apmaiņu: lielā cikla šķautne a-b un jaunā bloka cikla šķautne c-d tiek aizstātas ar a-c un b-d
        // (abām jābūt derīgiem zirdziņa gājieniem). Rezultāts ir VIENS slēgts cikls, kas apmeklē arī iepriekš tukšās
        // šūnas. Šūnu secībai (FFseciba) šeit vairs nav nozīmes - katra šūna ir slēgts bloks.

        struct Pt : IEquatable<Pt>
        {
            public readonly int X, Y;
            public Pt(int x, int y) { X = x; Y = y; }
            public bool Equals(Pt o) { return X == o.X && Y == o.Y; }
            public override bool Equals(object o) { return o is Pt && Equals((Pt)o); }
            public override int GetHashCode() { return unchecked(X * 73856093 ^ Y * 19349663); }
            public override string ToString() { return "(" + X + "," + Y + ")"; }
        }

        // zirdziņa gājieni (1-bāzēts pēc cipara): 1:(1,-2) 2:(2,-1) 3:(2,1) 4:(1,2) 5:(-1,2) 6:(-2,1) 7:(-2,-1) 8:(-1,-2)
        static readonly int[] MvDx = { 0, 1, 2, 2, 1, -1, -2, -2, -1 };
        static readonly int[] MvDy = { 0, -2, -1, 1, 2, 2, 1, -1, -2 };

        static bool IsKnight(Pt a, Pt b)
        {
            int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
            return (dx == 1 && dy == 2) || (dx == 2 && dy == 1);
        }

        static bool TourDfs(int n, int[] order, bool[] vis, int cur, int depth, ref long budget, Random rng)
        {
            order[depth] = cur; vis[cur] = true;
            int total = n * n;
            if (depth == total - 1)
            {
                int ax = cur % n, ay = cur / n, bx = order[0] % n, by = order[0] / n;
                int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
                if ((dx == 1 && dy == 2) || (dx == 2 && dy == 1)) return true;    // slēgts: gals ir zirdziņa gājienā no sākuma
                vis[cur] = false; return false;
            }
            if (--budget < 0) { vis[cur] = false; return false; }
            int cx = cur % n, cy = cur / n;
            var cand = new List<int[]>();
            for (int m = 1; m <= 8; m++)
            {
                int nx = cx + MvDx[m], ny = cy + MvDy[m];
                if (nx < 0 || ny < 0 || nx >= n || ny >= n || vis[ny * n + nx]) continue;
                int deg = 0;
                for (int k = 1; k <= 8; k++)
                {
                    int px = nx + MvDx[k], py = ny + MvDy[k];
                    if (px >= 0 && py >= 0 && px < n && py < n && !vis[py * n + px]) deg++;
                }
                cand.Add(new[] { ny * n + nx, deg, rng.Next() });
            }
            cand.Sort((p, q) => p[1] != q[1] ? p[1].CompareTo(q[1]) : p[2].CompareTo(q[2]));    // Warnsdorff + nejaušs
            foreach (var c in cand)
            {
                if (TourDfs(n, order, vis, c[0], depth + 1, ref budget, rng)) return true;
                if (budget < 0) break;
            }
            vis[cur] = false; return false;
        }

        class TourVariant
        {
            public int[] Order;            // šūnu indeksi (y*n+x) cikla secībā
            public HashSet<int> Edges;     // šķautnes kā c*n*n+d (abos virzienos)
        }

        /// <summary>Ģenerē slēgtus n x n zirdziņa ciklus un katram pievieno 8 simetrijas (rotācijas/spoguļojumi).</summary>
        static List<TourVariant> BuildTourVariants(int n, int baseCount, double timeLimitSec, List<int[]> extraBases = null)
        {
            var rng = new Random(2024);
            int total = n * n;
            var bases = new List<int[]>();
            if (extraBases != null) bases.AddRange(extraBases.Take(60));      // lietotāja cikli - PIRMIE (tiek izmantoti pirmie, ja der)
            int userBases = bases.Count;
            baseCount += userBases;
            var sw = Stopwatch.StartNew();
            while (bases.Count < baseCount && sw.Elapsed.TotalSeconds < timeLimitSec)
            {
                var order = new int[total]; var vis = new bool[total];
                long budget = 100000;
                if (TourDfs(n, order, vis, rng.Next(total), 0, ref budget, rng)) bases.Add((int[])order.Clone());
            }
            var variants = new List<TourVariant>();
            var seen = new HashSet<string>();
            foreach (var b in bases)
            {
                for (int t = 0; t < 8; t++)
                {
                    var ord = new int[total];
                    for (int i = 0; i < total; i++)
                    {
                        int x = b[i] % n, y = b[i] / n, nx, ny;
                        switch (t)
                        {
                            case 0: nx = x; ny = y; break;
                            case 1: nx = n - 1 - x; ny = y; break;
                            case 2: nx = x; ny = n - 1 - y; break;
                            case 3: nx = n - 1 - x; ny = n - 1 - y; break;
                            case 4: nx = y; ny = x; break;
                            case 5: nx = n - 1 - y; ny = x; break;
                            case 6: nx = y; ny = n - 1 - x; break;
                            default: nx = n - 1 - y; ny = n - 1 - x; break;
                        }
                        ord[i] = ny * n + nx;
                    }
                    var edges = new HashSet<int>();
                    var keyList = new List<long>();
                    for (int i = 0; i < total; i++)
                    {
                        int c = ord[i], d = ord[(i + 1) % total];
                        edges.Add(c * total + d); edges.Add(d * total + c);
                        keyList.Add(Math.Min(c, d) * (long)total + Math.Max(c, d));
                    }
                    keyList.Sort();
                    if (seen.Add(string.Join(",", keyList))) variants.Add(new TourVariant { Order = ord, Edges = edges });
                }
            }
            return variants;
        }

        static bool HasEdge(Dictionary<Pt, Pt[]> nb, Pt a, Pt b)
        {
            Pt[] arr;
            return nb.TryGetValue(a, out arr) && (arr[0].Equals(b) || arr[1].Equals(b));
        }

        static void ReplaceNb(Dictionary<Pt, Pt[]> nb, Pt p, Pt oldN, Pt newN)
        {
            var arr = nb[p];
            if (arr[0].Equals(oldN)) arr[0] = newN; else arr[1] = newN;
        }

        /// <summary>Vai nb veido VIENU slēgtu ciklu, kas apmeklē visus punktus.</summary>
        static bool IsSingleCycle(Dictionary<Pt, Pt[]> nb, Pt start)
        {
            int count = 0; Pt prev = default(Pt), cur = start;
            bool first = true;
            do
            {
                count++;
                var arr = nb[cur];
                Pt next = (first || !arr[0].Equals(prev)) ? arr[0] : arr[1];
                if (!first && arr[0].Equals(prev) && arr[1].Equals(prev)) return false;
                prev = cur; cur = next; first = false;
                if (count > nb.Count) return false;
            } while (!cur.Equals(start));
            return count == nb.Count;
        }

        /// <summary>Mēģina savienot jaunu slēgtu bloku (bloka stūris bx0,by0; izmērs n) ar esošo ciklu nb.</summary>
        static bool MergeBlock(Dictionary<Pt, Pt[]> nb, int n, int bx0, int by0, List<TourVariant> variants, Pt startPt, out string info)
        {
            info = null;
            int total = n * n;
            Func<int, Pt> G = idx => new Pt(bx0 + idx % n, by0 + idx / n);
            // kandidātšķautnes (a,b) lielajā ciklā, kuru abi gali ir pietiekami tuvu blokam (zirdziņa gājiens ≤ 2)
            Func<Pt, bool> near = p => p.X >= bx0 - 2 && p.X <= bx0 + n + 1 && p.Y >= by0 - 2 && p.Y <= by0 + n + 1;
            var edgesNear = new List<KeyValuePair<Pt, Pt>>();
            foreach (var kv in nb)
            {
                var a = kv.Key;
                if (!near(a)) continue;
                foreach (var b in kv.Value)
                {
                    if (!near(b)) continue;
                    if (a.X < b.X || (a.X == b.X && a.Y < b.Y)) edgesNear.Add(new KeyValuePair<Pt, Pt>(a, b));
                }
            }
            // šķautnes pie sākuma punkta pēdējās (lai saglabātu cikla sākumu un virzienu, ja iespējams)
            edgesNear = edgesNear.OrderBy(e => (e.Key.Equals(startPt) || e.Value.Equals(startPt)) ? 1 : 0)
                                 .ThenBy(e => e.Key.X).ThenBy(e => e.Key.Y).ToList();
            foreach (var e in edgesNear)
            {
                Pt a = e.Key, b = e.Value;
                var ra = new List<int>(); var rb = new List<int>();
                for (int idx = 0; idx < total; idx++)
                {
                    var g = G(idx);
                    if (IsKnight(a, g)) ra.Add(idx);
                    if (IsKnight(b, g)) rb.Add(idx);
                }
                if (ra.Count == 0 || rb.Count == 0) continue;
                for (int vi = 0; vi < variants.Count; vi++)
                {
                    var v = variants[vi];
                    foreach (int u in ra)
                    {
                        foreach (int w in rb)
                        {
                            if (u == w || !v.Edges.Contains(u * total + w)) continue;
                            // atrasta apmaiņa: noņemam a-b un u-w, pievienojam a-u un b-w
                            var savedA = (Pt[])nb[a].Clone(); var savedB = (Pt[])nb[b].Clone();
                            var added = new List<Pt>();
                            for (int i = 0; i < total; i++)
                            {
                                var p = G(v.Order[i]);
                                nb[p] = new[] { G(v.Order[(i + total - 1) % total]), G(v.Order[(i + 1) % total]) };
                                added.Add(p);
                            }
                            Pt gu = G(u), gw = G(w);
                            ReplaceNb(nb, gu, gw, a); ReplaceNb(nb, gw, gu, b);
                            ReplaceNb(nb, a, b, gu); ReplaceNb(nb, b, a, gw);
                            if (IsSingleCycle(nb, startPt))
                            {
                                info = "cikls #" + (vi / 8 + 1) + ", simetrija " + (vi % 8) + "; apmainītas šķautnes " + a + "-" + b + " un " + gu + "-" + gw;
                                return true;
                            }
                            foreach (var p in added) nb.Remove(p);      // atcelšana
                            nb[a] = savedA; nb[b] = savedB;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Nolasa lietotāja n x n slēgto bloku ciklu katalogu: katra rinda = viena cikla gājienu cipari (n*n cipari).
        /// Derīgi ir tikai slēgti cikli, kas apmeklē visas n x n šūnas tieši vienreiz.</summary>
        static List<int[]> LoadBlockCycles(string path, int n)
        {
            var res = new List<int[]>();
            int bad = 0, total = n * n;
            foreach (var line in File.ReadAllLines(path))
            {
                var ds = new List<int>();
                foreach (char ch in line) if (ch >= '1' && ch <= '8') ds.Add(ch - '0');
                if (ds.Count == 0) continue;
                if (ds.Count != total) { bad++; continue; }
                int x = 0, y = 0;
                var p = new List<Pt> { new Pt(0, 0) };
                foreach (int d in ds) { x += MvDx[d]; y += MvDy[d]; p.Add(new Pt(x, y)); }
                if (!p[p.Count - 1].Equals(p[0])) { bad++; continue; }
                p.RemoveAt(p.Count - 1);
                int mx = p.Min(q => q.X), my = p.Min(q => q.Y);
                var order = new int[total]; var seen = new bool[total]; bool ok = true;
                for (int i = 0; i < total && ok; i++)
                {
                    int lx = p[i].X - mx, ly = p[i].Y - my;
                    if (lx < 0 || ly < 0 || lx >= n || ly >= n || seen[ly * n + lx]) ok = false;
                    else { order[i] = ly * n + lx; seen[ly * n + lx] = true; }
                }
                if (ok) res.Add(order); else bad++;
            }
            Console.WriteLine($"[INFO] Bloku ciklu katalogs {path}: derīgi {n}x{n} slēgti cikli: {res.Count}" + (bad > 0 ? $", izlaisti {bad} nederīgi" : ""));
            return res;
        }

        /// <summary>Nolasa FFseciba TXT: šūnas ar skaitli (apmeklēšanas kārtas numuru) = bloki secībā.</summary>
        static HashSet<Cell> ReadFFsecibaCells(string path)
        {
            var cells = new HashSet<Cell>();
            string[] lines = File.ReadAllText(path).Split('\n');
            for (int r = 0; r < lines.Length; r++)
            {
                string[] f = lines[r].TrimEnd('\r').Split(',');
                for (int c = 0; c < f.Length; c++)
                {
                    int v;
                    if (f[c].Trim().Length > 0 && int.TryParse(f[c].Trim(), out v)) cells.Add(new Cell(r, c));
                }
            }
            return cells;
        }

        /// <summary>Aizpildīšana no FFseciba TXT (bez PNG): virsma = TXT šūnas + tukšo šūnu saraksts (_tuksas.txt);
        /// bloka izmērs tiek noteikts no .hrz. Tā bloku SECĪBA (TXT) nav atkarīga no bloka izmēra.</summary>
        static int FillHolesFromTxt(string ffsecibaPath, string tuksasPath, string hrzPath, string outHrz, int n, bool force, string cycPath)
        {
            if (!File.Exists(ffsecibaPath)) { Console.WriteLine("[KĻŪDA] Nav faila: " + ffsecibaPath); return 1; }
            var surface = ReadFFsecibaCells(ffsecibaPath);
            int numbered = surface.Count;
            if (tuksasPath == null)
            {
                string cand = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ffsecibaPath)),
                                           Path.GetFileNameWithoutExtension(ffsecibaPath) + "_tuksas.txt");
                if (File.Exists(cand)) tuksasPath = cand;
            }
            int extra = 0;
            if (tuksasPath != null && File.Exists(tuksasPath))
            {
                foreach (var line in File.ReadAllLines(tuksasPath))
                {
                    var f = line.Split(',');
                    int r, c;
                    if (f.Length >= 2 && int.TryParse(f[0].Trim(), out r) && int.TryParse(f[1].Trim(), out c) && surface.Add(new Cell(r, c))) extra++;
                }
                if (extra > 0) Console.WriteLine($"[INFO] Tukšo šūnu saraksts {tuksasPath}: {extra} šūnas.");
            }
            else
            {
                Console.WriteLine("[INFO] BRĪDINĀJUMS: tukšo šūnu saraksta (" + Path.GetFileNameWithoutExtension(ffsecibaPath) + "_tuksas.txt) nav.");
                Console.WriteLine("[INFO] Tad tiks aizpildītas tikai TXT numurētās šūnas, kas nav .hrz ciklā. Šūnas, ko Png2Seciba izņēma šaha krāsojuma");
                Console.WriteLine("[INFO] izlīdzināšanai, TXT nav numurētas, un tās netiks aizpildītas. Lai tās aizpildītu: palaid Png2Seciba attēls.png izvade.txt");
                Console.WriteLine("[INFO] vēlreiz (tas izveido _tuksas.txt) vai lieto PNG režīmu: Png2Seciba attēls.png --aizpildit=cikls.hrz");
            }
            if (surface.Count == 0) { Console.WriteLine("[KĻŪDA] TXT nesatur nevienu šūnu."); return 1; }
            int nRows = surface.Max(c => c.R) + 1, nCols = surface.Max(c => c.C) + 1;
            Console.WriteLine($"[INFO] FFseciba TXT: {numbered} numurētas šūnas" + (extra > 0 ? $" + {extra} tukšās" : "") + $" = virsma {surface.Count}, tīkls {nRows} x {nCols}.");
            int rc = FillHolesInHrz(surface, nRows, nCols, hrzPath, outHrz, n, force, cycPath);
            if (rc == 0 && (tuksasPath == null || !File.Exists(tuksasPath)))
                Console.WriteLine("[INFO] PIEZĪME: tukšo šūnu saraksta (_tuksas.txt) nebija, tāpēc šūnas, kas nav TXT, netika aizpildītas. Pārbaudi, vai virsma ir pilna.");
            return rc;
        }

        /// <summary>Aizpilda tukšās šūnas: PNG nosaka, kuras šūnas ir virsma; .hrz cikls rāda, kuras no tām ir apmeklētas.</summary>
        static int FillHolesInHrz(HashSet<Cell> surface, int nRows, int nCols, string hrzPath, string outHrz, int n, bool force, string cycPath)
        {
            if (!File.Exists(hrzPath))
            {
                Console.WriteLine("[KĻŪDA] Nav faila: " + hrzPath);
                Console.WriteLine("[KĻŪDA] --aizpildit= jānorāda ESOŠS, jau salikts .hrz cikls, kuru vajag aizpildīt (piem. --aizpildit=aplisUzFona.hrz).");
                Console.WriteLine("[KĻŪDA] Rezultāta faila nosaukumu norāda ar --hrz-izvade=NOSAUKUMS.hrz; ja nenorāda, tas būs <ieejas nosaukums>_aizpildits.hrz.");
                return 1;
            }
            var digits = new List<int>();
            foreach (char ch in File.ReadAllText(hrzPath)) if (ch >= '1' && ch <= '8') digits.Add(ch - '0');
            if (digits.Count < 4) { Console.WriteLine("[KĻŪDA] .hrz fails nesatur gājienu virkni."); return 1; }

            var pts = new List<Pt>(digits.Count + 1);
            int x = 0, y = 0; pts.Add(new Pt(0, 0));
            foreach (int d in digits) { x += MvDx[d]; y += MvDy[d]; pts.Add(new Pt(x, y)); }
            if (!pts[pts.Count - 1].Equals(pts[0])) { Console.WriteLine("[KĻŪDA] .hrz nav SLĒGTS cikls (gals nav sākuma punktā): " + pts[pts.Count - 1]); return 1; }
            pts.RemoveAt(pts.Count - 1);
            if (new HashSet<Pt>(pts).Count != pts.Count) { Console.WriteLine("[KĻŪDA] .hrz ciklā ir atkārtoti punkti."); return 1; }

            int minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            int wPx = maxX - minX + 1, hPx = maxY - minY + 1;
            Console.WriteLine($"[INFO] .hrz: {pts.Count} punkti, laukums {wPx} x {hPx}, sākuma punkts {pts[0]}.");
            if (n <= 0)
            {
                // bloka izmērs netiek prasīts: .hrz laukums = tīkls x bloks
                if (wPx % nCols == 0 && hPx % nRows == 0 && wPx / nCols == hPx / nRows) n = wPx / nCols;
                else
                {
                    Console.WriteLine($"[KĻŪDA] Nevar automātiski noteikt bloka izmēru: .hrz laukums {wPx} x {hPx}, šūnu tīkls {nRows} x {nCols}. Norādi --bloks=N.");
                    return 1;
                }
                if (n < 6 || n % 2 != 0) { Console.WriteLine($"[KĻŪDA] Noteiktais bloka izmērs {n} nav derīgs (jābūt pāra skaitlim >= 6). Norādi --bloks=N."); return 1; }
                Console.WriteLine($"[INFO] Bloka izmērs noteikts automātiski: {n}x{n} (.hrz laukums {wPx} x {hPx} / šūnu tīkls {nRows} x {nCols}).");
            }
            if (wPx % n != 0 || hPx % n != 0)
            {
                Console.WriteLine($"[KĻŪDA] .hrz laukums ({wPx} x {hPx}) nedalās ar bloka izmēru {n} - tas nav no {n}x{n} blokiem salikts cikls.");
                return 1;
            }
            int colsH = wPx / n, rowsH = hPx / n;

            // DROŠINĀTĀJS: .hrz bloku tīklam jābūt TIEŠI tikpat lielam kā PNG šūnu tīklam. Ja .hrz ir mazāks, tas, visticamāk,
            // nav no šī attēla (citādi "tukšās" šūnas būtu visa pārējā virsma). Ar --forse var atļaut (ja trūkst visas malas rindas/kolonnas).
            if ((rowsH != nRows || colsH != nCols) && !force)
            {
                Console.WriteLine($"[KĻŪDA] .hrz bloku tīkls ({rowsH} x {colsH}) nesakrīt ar PNG šūnu tīklu ({nRows} x {nCols}): šis .hrz, visticamāk, nav salikts no šī attēla.");
                Console.WriteLine("[KĻŪDA] Ja tiešām trūkst visa malas rinda/kolonna, palaid ar --forse (tad nobīde tiks meklēta automātiski).");
                return 1;
            }
            if (rowsH > nRows || colsH > nCols) { Console.WriteLine($"[KĻŪDA] .hrz bloku tīkls ({rowsH} x {colsH}) ir LIELĀKS par PNG šūnu tīklu ({nRows} x {nCols})."); return 1; }
            // izlīdzinājums un orientācija: kur .hrz bloku tīkls atrodas PNG šūnu tīklā. FFseciba rinda 0 ir attēla AUGŠĀ,
            // bet .hrz y ass bieži ir apgriezta (rinda 0 = lielākais y), tāpēc pārbaudām abas orientācijas (apgrieztā - pirmā).
            var good = new List<int[]>();   // {flip(1/0), nobīde rindās, nobīde kolonnās}
            foreach (int flip in new[] { 1, 0 })
                for (int sr = 0; sr <= nRows - rowsH; sr++)
                    for (int sc = 0; sc <= nCols - colsH; sc++)
                    {
                        bool ok = true;
                        foreach (var p in pts)
                        {
                            int r0 = (p.Y - minY) / n;
                            int rr = flip == 1 ? rowsH - 1 - r0 : r0;
                            if (!surface.Contains(new Cell(sr + rr, sc + (p.X - minX) / n))) { ok = false; break; }
                        }
                        if (ok) good.Add(new[] { flip, sr, sc });
                    }
            if (good.Count == 0)
            {
                Console.WriteLine($"[KĻŪDA] .hrz ({rowsH} x {colsH} bloki) neatbilst virsmai ({nRows} x {nCols} šūnas): daži .hrz bloki atrodas ārpus virsmas (pārbaudītas abas y ass orientācijas).");
                Console.WriteLine("[KĻŪDA] Pārbaudi, vai .hrz ir salikts no tā paša attēla (un ar tiem pašiem iestatījumiem, piem. ne --vecais-tikls).");
                return 1;
            }
            int flipY = good[0][0], offR = good[0][1], offC = good[0][2];
            if (good.Count > 1) Console.WriteLine($"[INFO] Brīdinājums: {good.Count} iespējami izlīdzinājumi/orientācijas, izmantoju pirmo.");
            Console.WriteLine($"[INFO] .hrz bloku tīkls {rowsH} x {colsH}; šūnu tīkls {nRows} x {nCols}; nobīde rindās {offR}, kolonnās {offC}; " +
                              (flipY == 1 ? "y ass apgriezta (FFseciba rinda 0 = .hrz lielākais y)." : "y ass tieša."));
            Func<Pt, Cell> cellOf = p =>
            {
                int r0 = (p.Y - minY) / n;
                return new Cell(offR + (flipY == 1 ? rowsH - 1 - r0 : r0), offC + (p.X - minX) / n);
            };
            // šūnas (R,C) bloka stūris .hrz koordinātēs
            Func<Cell, Pt> cornerOf = h =>
            {
                int r0 = flipY == 1 ? rowsH - 1 - (h.R - offR) : h.R - offR;
                return new Pt(minX + (h.C - offC) * n, minY + r0 * n);
            };

            // kuras šūnas ir apmeklētas
            var counts = new Dictionary<Cell, int>();
            foreach (var p in pts)
            {
                var c = cellOf(p);
                counts[c] = counts.ContainsKey(c) ? counts[c] + 1 : 1;
            }
            int partial = counts.Count(kv => kv.Value != n * n);
            if (partial > 0) { Console.WriteLine($"[KĻŪDA] {partial} šūnas .hrz nav pilnībā apmeklētas ({n * n} punkti) - tas nav pilnu {n}x{n} bloku cikls."); return 1; }
            var holes = surface.Where(c => !counts.ContainsKey(c)).OrderBy(c => c.R).ThenBy(c => c.C).ToList();
            Console.WriteLine($"[INFO] Virsmas šūnas: {surface.Count}; .hrz apmeklētas: {counts.Count}");
            if (holes.Count > 0)
                WriteRedLine($"[UZMANĪBU] TUKŠĀS ŠŪNAS (jāaizpilda): {holes.Count} - " + string.Join(" ", holes));
            if (holes.Count == 0) { Console.WriteLine("[INFO] Nav ko aizpildīt - visas virsmas šūnas jau ir ciklā. Fails netiek mainīts."); return 0; }

            Console.WriteLine($"[INFO] Gatavoju {n}x{n} slēgtos zirdziņa ciklus...");
            List<int[]> userCycles = (cycPath != null && File.Exists(cycPath)) ? LoadBlockCycles(cycPath, n) : null;
            if (cycPath != null && !File.Exists(cycPath)) Console.WriteLine("[INFO] Brīdinājums: bloku ciklu katalogs nav atrasts: " + cycPath);
            var variants = BuildTourVariants(n, n == 6 ? 40 : 12, n == 6 ? 8 : 30, userCycles);
            if (variants.Count == 0) { Console.WriteLine($"[KĻŪDA] Neizdevās uzģenerēt {n}x{n} slēgtus zirdziņa ciklus."); return 1; }
            Console.WriteLine($"[INFO] Pieejami {variants.Count} cikla varianti (ar rotācijām/spoguļojumiem).");

            // cikla kaimiņu saraksts
            var nb = new Dictionary<Pt, Pt[]>();
            for (int i = 0; i < pts.Count; i++)
                nb[pts[i]] = new[] { pts[(i + pts.Count - 1) % pts.Count], pts[(i + 1) % pts.Count] };
            Pt start = pts[0], firstNext = pts[1];

            var remaining = new List<Cell>(holes);
            int filled = 0;
            bool progress = true;
            while (remaining.Count > 0 && progress)
            {
                progress = false;
                foreach (var h in remaining.ToList())
                {
                    string info;
                    var corner = cornerOf(h);
                    if (MergeBlock(nb, n, corner.X, corner.Y, variants, start, out info))
                    {
                        Console.WriteLine($"[OK] šūna {h} aizpildīta ({info}).");
                        remaining.Remove(h); filled++; progress = true;
                    }
                }
            }
            if (remaining.Count > 0)
            {
                WriteRedLine("[KĻŪDA] Neizdevās aizpildīt TUKŠĀS šūnas: " + string.Join(" ", remaining) + " (nav piemērotas šķautnes, ko apmainīt, vai apkārt nav ciklā ietvertu bloku).");
                return 1;
            }

            // gala cikls: sākam no tā paša punkta un, ja iespējams, tajā pašā virzienā
            var arr0 = nb[start];
            Pt cur = arr0[0].Equals(firstNext) ? arr0[0] : (arr0[1].Equals(firstNext) ? arr0[1] : arr0[0]);
            var result = new List<Pt> { start };
            Pt prev = start;
            while (!cur.Equals(start))
            {
                result.Add(cur);
                var arr = nb[cur];
                Pt next = arr[0].Equals(prev) ? arr[1] : arr[0];
                prev = cur; cur = next;
                if (result.Count > nb.Count) { Console.WriteLine("[KĻŪDA] Iekšēja kļūda: cikls nesaslēdzas."); return 1; }
            }
            var sb = new System.Text.StringBuilder();
            result.Add(start);
            for (int i = 0; i + 1 < result.Count; i++)
            {
                int dx = result[i + 1].X - result[i].X, dy = result[i + 1].Y - result[i].Y, dg = 0;
                for (int d = 1; d <= 8; d++) if (MvDx[d] == dx && MvDy[d] == dy) dg = d;
                if (dg == 0) { Console.WriteLine($"[KĻŪDA] Iekšēja kļūda: solis {i} nav zirdziņa gājiens."); return 1; }
                sb.Append(dg);
            }
            int expected = pts.Count + filled * n * n;
            if (sb.Length != expected || new HashSet<Pt>(result.Take(result.Count - 1)).Count != expected)
            { Console.WriteLine($"[KĻŪDA] Gala kontrole neizdevās: garums {sb.Length}, sagaidāms {expected}."); return 1; }

            outHrz = SafeWrite(outHrz, fp => File.WriteAllText(fp, sb.ToString()));
            Console.WriteLine($"[OK] Aizpildītas {filled} šūnas (+{filled * n * n} punkti). Slēgts cikls: {sb.Length} gājieni, visi punkti unikāli, kontrole veiksmīga.");
            Console.WriteLine($"[OK] Saglabāts {outHrz}");
            return 0;
        }

        // ================== MAIN ==================

        static void Main(string[] args)
        {
            // Lietošana: Png2Seciba attēls.png [izvade.txt] [--tikai-karte] [--laiks=SEK] [--forse]
            //            Png2Seciba attēls.png --aizpildit=cikls.hrz   (tukšo šūnu aizpildīšana)
            //   --tikai-karte : saglabā starpdatus (karti un diagnostiku) un beidz pēc slēgtā cikla
            //                   mēģinājuma, nesākot atvērtā ceļa meklēšanu
            //   --laiks=SEK   : laika ierobežojums katram meklēšanas posmam (noklusējums 120, 0 = bez ierobežojuma)
            //   --forse       : meklēt arī tad, ja pierādīts, ka ciklam/ceļam jābūt neiespējamam (aizpildīšanas režīmā: atļaut mazāku .hrz tīklu)
            //   --neizlidzinat: NEizlīdzināt šaha krāsojumu (pēc noklusējuma starpība tiek novērsta, noņemot malas rūtiņas)
            //   --atteikt-atvertu: atteikties no ATVĒRTAS bloku secības (pēc noklusējuma tā tiek pieņemta - gala HC no blokiem tāpat ir vesels)
            //   --slegta-secibu: meklēt SLĒGTU bloku secību arī tad, ja tam jāatstāj vairāk tukšo šūnu (pēc noklusējuma pietiek ar atvērtu)
            //   --atlaut-atvertu: (vecs karodziņš, vairs nav vajadzīgs - atvērta secība tiek pieņemta pati)
            //   --aizpildit=CIKLS.hrz : AIZPILDA TUKŠĀS šūnas jau saliktā slēgtā .hrz ciklā (no attēla uzzina, kuras šūnas ir virsma,
            //                   no .hrz - kuras no tām nav apmeklētas; tukšās aizpilda ar atsevišķiem slēgtiem blokiem un saplūdina ar ciklu)
            //   --hrz-izvade=FAILS.hrz : kur saglabāt aizpildīto ciklu (noklusējums: <cikls>_aizpildits.hrz)
            //   --bloks=N     : bloka izmērs (pāra skaitlis >= 6). Parasti NEVAJAG - tiek noteikts automātiski no .hrz laukuma
            //   --ffseciba=TXT: aizpildīšana BEZ attēla: virsma = FFseciba TXT šūnas + <TXT>_tuksas.txt (ja ir); piem.
            //                   Png2Seciba --ffseciba=izvade.txt --aizpildit=cikls.hrz
            //   --tuksas=FAILS: tukšo šūnu saraksts (rinda,kolonna) - ja nav noklusējuma <TXT>_tuksas.txt
            //   --cikli=FAILS : savs n x n slēgto bloku ciklu katalogs (rinda = viens cikls, n*n cipari); tie tiek izmantoti pirmie
            //   --vecais-tikls: vecā uzvedība - pārpalikušie pikseļi pie malas netiek skaitīti un rindu/kolonnu skaits netiek izlīdzināts uz pāra
            var positional = new List<string>();
            bool onlyMap = false, force = false, noBalance = false, allowOpen = false, rejectOpen = false, preferClosedOrder = false;
            string fillHrz = null, fillOut = null, fillTxt = null, fillEmpty = null, fillCyc = null; int blockSize = 0;   // 0 = noteikt automātiski
            foreach (var a in args)
            {
                if (a == "--tikai-karte") onlyMap = true;
                else if (a == "--forse") force = true;
                else if (a == "--vecais-tikls") LegacyGrid = true;
                else if (a == "--neizlidzinat") noBalance = true;
                else if (a == "--atlaut-atvertu") allowOpen = true;
                else if (a == "--atteikt-atvertu") rejectOpen = true;
                else if (a == "--slegta-secibu") preferClosedOrder = true;
                else if (a.StartsWith("--aizpildit=")) fillHrz = a.Substring(12);
                else if (a.StartsWith("--hrz-izvade=")) fillOut = a.Substring(13);
                else if (a.StartsWith("--bloks=")) { int bs; if (int.TryParse(a.Substring(8), out bs) && bs >= 6 && bs % 2 == 0) blockSize = bs; }
                else if (a.StartsWith("--ffseciba=")) fillTxt = a.Substring(11);
                else if (a.StartsWith("--tuksas=")) fillEmpty = a.Substring(9);
                else if (a.StartsWith("--cikli=")) fillCyc = a.Substring(8);
                else if (a.StartsWith("--laiks="))
                {
                    double sec;
                    if (double.TryParse(a.Substring(8), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out sec))
                        SearchTimeLimitSec = sec;
                }
                else positional.Add(a);
            }
            if (fillHrz != null && fillTxt != null)
            {
                // AIZPILDĪŠANA NO TXT (bez PNG): bloku secība (FFseciba) nav atkarīga no bloka izmēra
                string outH = fillOut ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fillHrz)),
                                                      Path.GetFileNameWithoutExtension(fillHrz) + "_aizpildits.hrz");
                Environment.ExitCode = FillHolesFromTxt(fillTxt, fillEmpty, fillHrz, outH, blockSize, force, fillCyc);
                return;
            }
            if (allowOpen) rejectOpen = false;      // --atlaut-atvertu pārraksta --atteikt-atvertu
            string imagePath = positional.Count > 0 ? positional[0] : IMAGE_DEFAULT;
            string outPath = positional.Count > 1 ? positional[1] : OUTPUT_DEFAULT;

            bool[,] blackMask = LoadBlackMask(imagePath, out int width, out int height);

            var (surfaceCells, sensorCells, nRows, nCols) = DetectSurfaceAndSensors(blackMask, width, height, PIXELS_PER_BLOCK);
            Console.WriteLine($"[INFO] Rūtiņu tīkls: {nRows} rindas x {nCols} kolonnas ({PIXELS_PER_BLOCK}x{PIXELS_PER_BLOCK}px/bloks" +
                              (LegacyGrid ? ", vecais režīms)" : "; pārpalikušie pikseļi pieskaitīti, izmērs izlīdzināts uz pāra)"));
            Console.WriteLine($"[INFO] Virsmas rūtiņas (jāapmeklē): {surfaceCells.Count}");
            Console.WriteLine($"[INFO] Sensoru rūtiņas (izslēgtas): {sensorCells.Count}");

            if (surfaceCells.Count == 0)
            {
                Console.WriteLine("[KĻŪDA] Nav atrasta neviena virsmas rūtiņa!");
                return;
            }

            if (fillHrz != null)
            {
                // AIZPILDĪŠANAS REŽĪMS: šeit surfaceCells ir PILNA virsma (pirms šaha krāsojuma izlīdzināšanas)
                string outHrz = fillOut ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fillHrz)),
                                                        Path.GetFileNameWithoutExtension(fillHrz) + "_aizpildits.hrz");
                Environment.ExitCode = FillHolesInHrz(surfaceCells, nRows, nCols, fillHrz, outHrz, blockSize, force, fillCyc);
                return;
            }

            // ŠAHA KRĀSOJUMA IZLĪDZINĀŠANA (virsma tiek atstāta bez dažām malas rūtiņām, ja tas vajadzīgs slēgtam ciklam)
            var diag = AnalyzeSurface(surfaceCells, LastDetection);
            var emptyCells = new List<Cell>();
            if (!noBalance)
            {
                var spurCells = RemoveDenseSpurs(surfaceCells, LastDetection, 12);
                if (spurCells.Count > 0)
                {
                    foreach (var sc in spurCells) { LastDetection.RemovedSurface.Add(sc); emptyCells.Add(sc); }
                    diag = AnalyzeSurface(surfaceCells, LastDetection);
                    Console.WriteLine($"[INFO] Aizpildītās virsmas izvirzījumi (šūnas ar 1 kaimiņu, kas nav līnijas gali) atstāti tukši: {string.Join(" ", spurCells)} (virsma -> {surfaceCells.Count}).");
                }
                string balMsg;
                var removedCells = BalanceSurface(surfaceCells, diag, out balMsg, preferClosedOrder);
                if (balMsg != null) Console.WriteLine(balMsg);
                if (removedCells != null && removedCells.Count > 0)
                {
                    foreach (var rc in removedCells) { LastDetection.RemovedSurface.Add(rc); emptyCells.Add(rc); }
                    diag = AnalyzeSurface(surfaceCells, LastDetection);
                    Console.WriteLine($"[INFO] Virsmas rūtiņas pēc izlīdzināšanas: {surfaceCells.Count}");
                }
            }
            // tukšo šūnu saraksts (rinda,kolonna) - VIENMĒR (tukšs, ja tukšo šūnu nav); to lasa aizpildīšana (--ffseciba=... --aizpildit=...)
            string emptyDir = Path.GetDirectoryName(outPath);
            string emptyBase = Path.GetFileNameWithoutExtension(outPath) + "_tuksas.txt";
            string emptySaved = SafeWrite(string.IsNullOrEmpty(emptyDir) ? emptyBase : Path.Combine(emptyDir, emptyBase),
                                          fp => File.WriteAllLines(fp, emptyCells.Select(rc => rc.R + "," + rc.C)));
            if (emptyCells.Count > 0)
            {
                // SARKANS paziņojums - tikai ja tukšās šūnas ir
                string imgShown = imagePath;
                WriteRedLine($"[UZMANĪBU] TUKŠĀS ŠŪNAS: {emptyCells.Count} virsmas šūnas (rinda,kolonna) ceļš NEAPMEKLĒS: " + string.Join(" ", emptyCells));
                WriteRedLine("[UZMANĪBU] Pēc bloku salikšanas tās jāaizpilda ar atsevišķiem slēgtiem blokiem, piem.: Png2Seciba " + imgShown + " --aizpildit=cikls.hrz");
                string emptyShown = emptySaved;
                AppDomain.CurrentDomain.ProcessExit += (sd, ev) =>
                    WriteRedLine($"[UZMANĪBU] Atceries: {emptyCells.Count} tukšās šūnas jāaizpilda pēc bloku salikšanas (saraksts: {emptyShown}).");
            }

            // STARPDATI: karte + diagnostika - saglabājam PIRMS jebkādas meklēšanas
            WriteIntermediateData(outPath, surfaceCells, sensorCells, LastDetection, diag);

            // sākuma rūtiņa: ja eksistē rūtiņa ar TIEŠI 1 kaimiņu (piem. spirāles
            // gals) - tā ir OBLIGĀTA jebkura atvērta Hamiltona ceļa galapunkts.
            var degree1 = surfaceCells.Where(c => Neighbors(c, surfaceCells).Count == 1).ToList();
            Cell start = degree1.Count > 0 ? degree1[0] : surfaceCells.OrderBy(c => c.R).ThenBy(c => c.C).First();
            if (degree1.Count > 0)
                Console.WriteLine($"[INFO] Atrasta piespiedu galapunkta rūtiņa (1 kaimiņš): {start} - sāku no tās.");

            // ---- slēgta cikla mēģinājums ----
            List<Cell> path = null;
            bool closed = true;
            if (diag.ClosedImpossibleReason != null && !force)
                Console.WriteLine("[INFO] Slēgta bloku secība pierādāmi neiespējama (meklēšana izlaista): " + diag.ClosedImpossibleReason);
            else
            {
                // 1) konstruktīvā metode (2-faktors + ciklu saplūdināšana) - strādā uz aizpildītām formām
                path = FindHamiltonianCycleByMerging(surfaceCells, start, SearchTimeLimitSec > 0 ? Math.Min(SearchTimeLimitSec, 30) : 30);
                if (path != null)
                    Console.WriteLine("[INFO] Slēgta bloku secība atrasta ar 2-faktora + ciklu saplūdināšanas metodi.");
                else
                    path = FindHamiltonianPath(surfaceCells, start, true);
                if (path == null && LastSearchTimedOut)
                    Console.WriteLine($"[INFO] Slēgtās bloku secības meklēšana pārtraukta (laika ierobežojums {SearchTimeLimitSec} s); dziļākais sasniegtais {LastSearchBestDepth}/{surfaceCells.Count}");
            }

            if (path == null)
            {
                Console.WriteLine("[INFO] Slēgta bloku secība nav atrasta - mēģinu ATVĒRTU (no tās salikts gala HC tāpat būs vesels).");
                if (onlyMap)
                {
                    Console.WriteLine("[INFO] Starpdati saglabāti (--tikai-karte), meklēšanu neturpinu.");
                    return;
                }
                if (diag.OpenImpossibleReason != null && !force)
                {
                    Console.WriteLine("[KĻŪDA] Neviena bloku secība šai virsmai nav iespējama (pierādīts): " + diag.OpenImpossibleReason);
                    Console.WriteLine("[KĻŪDA] Meklēšanu neturpinu. Skat. karti (_karte.txt/.png); ja tomēr gribi meklēt, pievieno --forse.");
                    return;
                }

                closed = false;
                // 1) konstruktīvā metode nepāra rūtiņu skaitam: cikls uz virsmas bez vienas rūtiņas + šī rūtiņa kā gals
                path = FindHamiltonianPathByMerging(surfaceCells, SearchTimeLimitSec > 0 ? Math.Min(SearchTimeLimitSec, 30) : 30, start);
                if (path != null)
                    Console.WriteLine("[INFO] Atvērta bloku secība atrasta ar 2-faktora + ciklu saplūdināšanas metodi (cikls bez vienas rūtiņas + gals).");
                else
                    path = FindHamiltonianPath(surfaceCells, start, false);
                if (path == null && LastSearchTimedOut)
                    Console.WriteLine($"[INFO] Ātrās meklēšanas laiks beidzies ({SearchTimeLimitSec} s); dziļākais sasniegtais {LastSearchBestDepth}/{surfaceCells.Count}");
            }

            if (path == null)
            {
                Console.WriteLine("[INFO] Ātrā meklēšana neizdevās - mēģinu LĒNĀKU, bet DROŠU (savienojamības apgriešanu izmantojošu) meklēšanu (laika ierobežojums " + SearchTimeLimitSec + " s katram posmam)...");
                closed = true;
                if (diag.ClosedImpossibleReason == null || force)
                    path = FindHamiltonianPathPruned(surfaceCells, start, true);
                if (path == null)
                {
                    path = FindHamiltonianPathPruned(surfaceCells, start, false);
                    closed = false;
                    if (path == null)
                        Console.WriteLine($"[INFO] Drošā meklēšana: {(LastSearchTimedOut ? "laika ierobežojums" : "visas iespējas izsmeltas")}; dziļākais sasniegtais {LastSearchBestDepth}/{surfaceCells.Count}");
                }
            }

            if (path == null)
            {
                Console.WriteLine("[KĻŪDA] Neizdevās atrast derīgu bloku secību šai virsmai (iespējams, sensori/forma sadala virsmu tā, ka tas ir patiešām neiespējami).");
                return;
            }

            string bad = VerifyPath(surfaceCells, path, closed);
            if (bad != null) { Console.WriteLine("[KĻŪDA] Atrastā ceļa kontrole neizdevās: " + bad); return; }
            Console.WriteLine($"[OK] Atrasta {(closed ? "SLĒGTA" : "ATVĒRTA")} bloku secība ({path.Count}/{surfaceCells.Count} rūtiņas), kontrole veiksmīga.");

            // ATVĒRTA bloku secība (sākums + gals, piem. kā burtam S) ir pilnvērtīgs rezultāts: katrs bloks ir slēgts HC, un no savienotiem blokiem
            // gala HC tāpat ir vesels. Tāpēc atvērtu secību pieņemam jebkurai formai. (Stingrajā režīmā --atteikt-atvertu to noraida.)
            bool forcedEnds = diag.Degree1.Count > 0;
            if (!closed)
            {
                if (forcedEnds)
                    Console.WriteLine($"[INFO] Bloku secība ir ATVĒRTA: formai ir {diag.Degree1.Count} līnijas gali ({string.Join(" ", diag.Degree1.Take(6))}{(diag.Degree1.Count > 6 ? " ..." : "")}); gala HC no blokiem tāpat būs vesels slēgts cikls.");
                else
                    Console.WriteLine($"[INFO] Bloku secība ir ATVĒRTA (sākums {path[0]}, gals {path[path.Count - 1]}): šaha krāsojuma starpība neļauj slēgtu secību; gala HC no blokiem tāpat būs vesels slēgts cikls.");
            }
            if (!closed && rejectOpen)
            {
                Console.WriteLine("[INFO] Atvērta bloku secība netiek pieņemta (--atteikt-atvertu), tāpēc " + outPath + " NETIEK saglabāts.");
                Console.WriteLine("[INFO] Kāpēc slēgta bloku secība nav atrasta: " + (diag.ClosedImpossibleReason ?? "slēgtās secības meklēšana neizdevās (pierādījuma, ka tā neiespējama, nav)"));
                WriteOpenPathIntermediate(outPath, surfaceCells, sensorCells, path, LastDetection, diag);
                return;
            }
            // aizpildītai virsmai (bez līnijas galiem) papildus saglabājam atvērtās secības attēlu - lai redz, kur secība sākas un beidzas
            if (!closed && !forcedEnds)
                WriteOpenPathIntermediate(outPath, surfaceCells, sensorCells, path, LastDetection, diag);

            string result = RenderFFseciba(surfaceCells, path);
            string savedPath = SafeWrite(outPath, fp => File.WriteAllText(fp, result, Utf8NoBom));
            Console.WriteLine($"[OK] Saglabāts {savedPath}");
        }
    }
}
