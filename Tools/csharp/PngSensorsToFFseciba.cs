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

namespace PngSensorsToFFseciba
{
    class Program
    {
        const int PIXELS_PER_BLOCK = 4;      // 4x4 px attēlā = 1 FF bloks
        const int BLACK_THRESHOLD = 128;     // pikselis tumšāks par šo = "melns"
        const int RUN_LENGTH_THRESHOLD = 100; // min. nepārtraukta melna posma garums
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
        static Dictionary<Cell, bool> BuildGridMajority(bool[,] mask, int x0, int x1, int y0, int y1, int step)
        {
            int nCols = (x1 - x0 + 1) / step;
            int nRows = (y1 - y0 + 1) / step;
            var grid = new Dictionary<Cell, bool>();
            for (int r = 0; r < nRows; r++)
            {
                for (int c = 0; c < nCols; c++)
                {
                    int by0 = y0 + r * step, bx0 = x0 + c * step;
                    int blackCount = 0, total = step * step;
                    for (int dy = 0; dy < step; dy++)
                        for (int dx = 0; dx < step; dx++)
                            if (mask[by0 + dy, bx0 + dx]) blackCount++;
                    grid[new Cell(r, c)] = (blackCount / (double)total) >= FILL_MAJORITY;
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
                    if (branch.Count <= minBranchLength)
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

        static (HashSet<Cell> surface, HashSet<Cell> sensors, int nRows, int nCols) DetectSurfaceAndSensors(
            bool[,] blackMask, int width, int height, int step)
        {
            var rectBounds = FindRectangleBounds(blackMask, width, height);

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

            int nCols = (fullX1 - fullX0 + 1) / step;
            int nRows = (fullY1 - fullY0 + 1) / step;

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

            if (haveRect)
            {
                var rectGridLocal = BuildGridMajority(blackMask, rx0, rx1, ry0, ry1, step);
                int rowOffset = (ry0 - fullY0) / step;
                int colOffset = (rx0 - fullX0) / step;
                foreach (var kv in rectGridLocal)
                {
                    var gr = kv.Key.R + rowOffset;
                    var gc = kv.Key.C + colOffset;
                    var gCell = new Cell(gr, gc);
                    if (kv.Value) surfaceCells.Add(gCell);
                    else sensorCells.Add(gCell);
                }
            }

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
                    if (branch.Count <= maxSpurLen)
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

            while (true)
            {
                steps++;
                if (steps > timeBudgetSteps) return null;

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

                if (path.Count <= 1) return null;
                candStack.RemoveAt(candStack.Count - 1);
                idxStack.RemoveAt(idxStack.Count - 1);
                var removed = path[path.Count - 1];
                path.RemoveAt(path.Count - 1);
                visited.Remove(removed);
            }
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

            while (true)
            {
                steps++;
                if (steps > timeBudgetSteps) return null;

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

                if (path.Count <= 1) return null;
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

        // ================== MAIN ==================

        static void Main(string[] args)
        {
            string imagePath = args.Length > 0 ? args[0] : IMAGE_DEFAULT;
            string outPath = args.Length > 1 ? args[1] : OUTPUT_DEFAULT;

            bool[,] blackMask = LoadBlackMask(imagePath, out int width, out int height);

            var (surfaceCells, sensorCells, nRows, nCols) = DetectSurfaceAndSensors(blackMask, width, height, PIXELS_PER_BLOCK);
            Console.WriteLine($"[INFO] Rūtiņu tīkls: {nRows} rindas x {nCols} kolonnas ({PIXELS_PER_BLOCK}x{PIXELS_PER_BLOCK}px/bloks)");
            Console.WriteLine($"[INFO] Virsmas rūtiņas (jāapmeklē): {surfaceCells.Count}");
            Console.WriteLine($"[INFO] Sensoru rūtiņas (izslēgtas): {sensorCells.Count}");

            if (surfaceCells.Count == 0)
            {
                Console.WriteLine("[KĻŪDA] Nav atrasta neviena virsmas rūtiņa!");
                return;
            }

            // sākuma rūtiņa: ja eksistē rūtiņa ar TIEŠI 1 kaimiņu (piem. spirāles
            // gals) - tā ir OBLIGĀTA jebkura atvērta Hamiltona ceļa galapunkts.
            var degree1 = surfaceCells.Where(c => Neighbors(c, surfaceCells).Count == 1).ToList();
            Cell start = degree1.Count > 0 ? degree1[0] : surfaceCells.OrderBy(c => c.R).ThenBy(c => c.C).First();
            if (degree1.Count > 0)
                Console.WriteLine($"[INFO] Atrasta piespiedu galapunkta rūtiņa (1 kaimiņš): {start} - sāku no tās.");

            var path = FindHamiltonianPath(surfaceCells, start, true);
            bool closed = true;
            if (path == null)
            {
                Console.WriteLine("[INFO] Neizdevās atrast SLĒGTU ciklu - mēģinu ATVĒRTU ceļu.");
                path = FindHamiltonianPath(surfaceCells, start, false);
                closed = false;
            }

            if (path == null)
            {
                Console.WriteLine("[INFO] Ātrā meklēšana neizdevās - mēģinu LĒNĀKU, bet DROŠU (savienojamības apgriešanu izmantojošu) meklēšanu (var aizņemt kādu brīdi)...");
                path = FindHamiltonianPathPruned(surfaceCells, start, true);
                closed = true;
                if (path == null)
                {
                    path = FindHamiltonianPathPruned(surfaceCells, start, false);
                    closed = false;
                }
            }

            if (path == null)
            {
                Console.WriteLine("[KĻŪDA] Neizdevās atrast derīgu Hamiltona ceļu/ciklu šai virsmai (iespējams, sensori/forma sadala virsmu tā, ka tas ir patiešām neiespējami).");
                return;
            }

            Console.WriteLine($"[OK] Atrasts {(closed ? "SLĒGTS cikls" : "ATVĒRTS ceļš")} ({path.Count}/{surfaceCells.Count} rūtiņas).");

            string result = RenderFFseciba(surfaceCells, path);
            File.WriteAllText(outPath, result, System.Text.Encoding.UTF8);
            Console.WriteLine($"[OK] Saglabāts {outPath}");
        }
    }
}
