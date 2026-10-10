#!/usr/bin/env python3
"""
VCSM sēklu rīks: zirdziņa gājienu tūres ar 90° rotācijas simetriju.

Virzieni (dx pa labi, dy uz augšu; a1 = apakšā pa kreisi):
  1=(+1,+2) 2=(+2,+1) 3=(+2,-1) 4=(+1,-2) 5=(-1,-2) 6=(-2,-1) 7=(-2,+1) 8=(-1,+2)

Režīmi:
  python vcsm_seklas.py grupa 238521867
      parāda sēklas transformāciju grupu (pagriezieni, spoguļi, atpakaļgaitas)
  python vcsm_seklas.py kombinacijas 238521867 [--visas]
      izmēģina visas gabalu kombinācijas no grupas un visus sākuma lauciņus
      (9 ciparu sēkla -> 4 gabali, 18 ciparu -> 2 gabali, 6x6 dēlī);
      --visas pievieno arī vienkārši apgrieztās rindas (bez +4)
  python vcsm_seklas.py parbaudit 182347254768125832546783618324561476
      pārbauda pilnu rindu no visiem sākuma lauciņiem
  python vcsm_seklas.py katalogs [-n 6]
      atrod VISAS 90° simetriskās slēgtās tūres un to sēklas
  python vcsm_seklas.py katalogs180 [-n 6] [--visas]
      atrod VISAS 180° simetriskās slēgtās tūres; sēkla = pusē tūres (n*n/2 cipari),
      otrā puse = sēkla ar +4; --visas parāda visas klases sēklas no a1

  python vcsm_seklas.py piemers -n 10 [--skaits 3] [--nejausiba 42]
      ģenerē nejaušas simetriskas slēgtas tūres lielākiem dēļiem
      (n ≡ 2 mod 4 -> 90° simetrija; n ≡ 0 mod 4 -> 180° simetrija)

Faili (saderīgi ar mikroScope):
  parbaudit / kombinacijas / grupa pieņem arī faila nosaukumu: tura.hrz vai tura.bin
  katalogs / katalogs180 / piemers: --saglabat PREFIKSS [--formats hrz|bin|abi]
      katra tūre tiek saglabāta atsevišķā failā, piem. tures_001_10x10.hrz

Zīmēšana (vajag:  pip install pillow):
  python vcsm_seklas.py zimet 271236167413458381635672523857814745
      -> 271236167413458381635672523857814745.png  (tura.hrz -> tura.png;
         vairākas tūres -> galerija_N_tures.png; --izeja maina nosaukumu)
  python vcsm_seklas.py zimet tura.hrz cita.bin 2712361674... --izeja tures.png
      [--paraksti "A|B|"] [--kolonnas 3] [--izmers 520]
      dēļa izmērs un sākuma lauciņš tiek noteikti automātiski;
      paraksts pēc noklusējuma, piem. "10×10 · 90°" vai "8×8 · open"

Dēļa izmērs pēc noklusējuma 6 (-n). 90° simetriskas slēgtas tūres eksistē
tikai, ja n ≡ 2 (mod 4): 6, 10, 14, ...  Sēklas garums = n*n/4.
"""
import argparse
import itertools
import sys

K = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]
BURTI = 'abcdefghijklmnopqrstuvwxyz'


# ---------- pamatfunkcijas ----------

def lauc(p):
    return BURTI[p[0]] + str(p[1] + 1)


def afins(s, a, b):
    """Katram ciparam d (1..8): d -> a*(d-1)+b (mod 8) + 1.
    a=+1: pagrieziens par b*45° (b pāra skaitlis -> 90° soļi)
    a=-1, b nepāra: spoguļattēls"""
    return ''.join(str((a * (int(c) - 1) + b) % 8 + 1) for c in s)


def atpakalgaita(s):
    """Īstā atpakaļgaita: rinda otrādi, katram ciparam +4."""
    return afins(s[::-1], 1, 4)


SPOGULI = {1: 'spogulis /', 3: 'spogulis —', 5: 'spogulis \\', 7: 'spogulis |'}


def grupa(seed, ar_vienkarso=False):
    """Atgriež {nosaukums: rinda}."""
    g = {}
    for k in range(4):
        g['R%d' % (90 * k) if k else 'oriģināls'] = afins(seed, 1, 2 * k)
    for b, nm in SPOGULI.items():
        g[nm] = afins(seed, -1, b)
    for nm, s in list(g.items()):
        g['A·' + nm] = atpakalgaita(s)          # A = atpakaļgaita
    if ar_vienkarso:
        for nm, s in list(g.items()):
            if not nm.startswith('A·'):
                g['V·' + nm] = s[::-1]          # V = vienkārši apgriezta
    return g


def cels(s, start, n):
    """Ceļš no start pēc rindas s; None, ja iziet no dēļa vai atkārtojas."""
    p = [start]
    redz = {start}
    for i, c in enumerate(s):
        dx, dy = K[int(c) - 1]
        q = (p[-1][0] + dx, p[-1][1] + dy)
        if not (0 <= q[0] < n and 0 <= q[1] < n):
            return None
        if q in redz:
            if q == start and i == len(s) - 1 and len(redz) == n * n:
                p.append(q)
                return p
            return None
        redz.add(q)
        p.append(q)
    return p


def slegts_no(s, n):
    """Visi sākuma lauciņi, no kuriem s ir slēgts Hamiltona cikls."""
    if len(s) != n * n:
        return []
    return [(c, r) for c in range(n) for r in range(n)
            if (p := cels(s, (c, r), n)) and len(p) == n * n + 1]


def malas(p):
    return frozenset(frozenset(e) for e in zip(p, p[1:]))


# ---------- VCSM failu formāti (.hrz / .bin), saderīgi ar mikroScope ----------
# .hrz: teksta fails, cipari 1-8; visas citas zīmes (rindu pārnesumi u.c.) tiek ignorētas.
# .bin: 8 gājieni = 3 baiti (24 biti), katram gājienam 3 biti, augstākie biti pirmie;
#       cipars 8 glabājas kā 000. Ja gājienu skaits nedalās ar 8, beigās pieliek
#       EOF marķieri = pretējo virzienu pēdējam gājienam (d±4), atlikumu aizpilda ar 8.
#       Nolasot rinda tiek nogriezta pie pirmā gājiena, kas ir pretējs iepriekšējam
#       (derīgā tūrē tūlītēja atgriešanās nekad nenotiek).

def pretejais(d):
    return d - 4 if d > 4 else d + 4


def lasit_fails(cels_uz_failu):
    if cels_uz_failu.lower().endswith('.bin'):
        b = open(cels_uz_failu, 'rb').read()
        dati = []
        for i in range(0, len(b) - 2, 3):
            v = (b[i] << 16) | (b[i + 1] << 8) | b[i + 2]
            for j in range(7, -1, -1):
                x = (v >> (j * 3)) & 0b111
                dati.append(8 if x == 0 else x)
        for i in range(1, len(dati)):
            if dati[i] == pretejais(dati[i - 1]):
                dati = dati[:i]
                break
        return ''.join(map(str, dati))
    teksts = open(cels_uz_failu, encoding='utf-8', errors='ignore').read()
    return ''.join(c for c in teksts if c in '12345678')


def saglabat_hrz(rinda, fails):
    with open(fails, 'w', encoding='ascii') as f:
        f.write(rinda)


def saglabat_bin(rinda, fails):
    dati = [int(c) for c in rinda]
    if len(dati) % 8:
        dati.append(pretejais(dati[-1]))
    while len(dati) % 8:
        dati.append(8)
    buf = bytearray()
    for i in range(0, len(dati), 8):
        v = 0
        for k, d in enumerate(dati[i:i + 8]):
            v |= (0 if d == 8 else d) << ((7 - k) * 3)
        buf += bytes([(v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF])
    with open(fails, 'wb') as f:
        f.write(buf)


SAGLABAT = {'prefikss': None, 'formats': 'hrz', 'skaits': 0}


def saglabat(rinda, apraksts=''):
    """Saglabā vienu tūri atsevišķā failā (mikroScope vienu failu lasa kā vienu ceļu)."""
    if not SAGLABAT['prefikss']:
        return
    SAGLABAT['skaits'] += 1
    vards = f"{SAGLABAT['prefikss']}_{SAGLABAT['skaits']:03d}"
    if apraksts:
        vards += '_' + apraksts
    if SAGLABAT['formats'] in ('hrz', 'abi'):
        saglabat_hrz(rinda, vards + '.hrz')
    if SAGLABAT['formats'] in ('bin', 'abi'):
        saglabat_bin(rinda, vards + '.bin')
    print(f'  -> saglabāts: {vards}.{"hrz/.bin" if SAGLABAT["formats"] == "abi" else SAGLABAT["formats"]}')


# ---------- režīmi ----------

def rezims_grupa(seed, visas):
    for nm, s in grupa(seed, visas).items():
        print(f'{nm:20s} {s}')


def rezims_kombinacijas(seed, n, visas):
    if (n * n) % len(seed):
        print(f'Sēklas garums {len(seed)} nedala {n*n} (lauciņu skaits {n}×{n} dēlī). '
              f'Der garumi: {", ".join(str(n*n//k) for k in (4, 2, 1) if (n*n) % k == 0)}.')
        return
    gab = n * n // len(seed)          # 9 cipari -> 4 gabali, 18 -> 2, 36 -> 1
    g = grupa(seed, visas)
    vardi = list(g)
    print(f'Sēkla {len(seed)} cipari -> {gab} gabali. Grupā {len(vardi)} rindas; '
          f'pārbaudu {len(vardi)**gab} kombinācijas × {n*n} sākuma lauciņi...',
          file=sys.stderr)
    tures = {}   # malu kopa -> saraksts ar (kombinācija, sākums)
    for kombo in itertools.product(vardi, repeat=gab):
        s = ''.join(g[k] for k in kombo)
        for st in slegts_no(s, n):
            p = cels(s, st, n)
            tures.setdefault(malas(p), []).append((kombo, st, s))
    if not tures:
        print('Neviena derīga slēgta cikla.')
        return
    print(f'Atrastas {len(tures)} dažādas tūres (pēc malu kopas):\n')
    for i, (_, saraksts) in enumerate(tures.items(), 1):
        print(f'--- Tūre {i}: {len(saraksts)} pieraksta veidi ---')
        for kombo, st, s in saraksts:
            print(f'  no {lauc(st):3s}  {" + ".join(kombo)}')
            print(f'           {s}')
        print()


def rezims_parbaudit(s, n):
    starti = slegts_no(s, n)
    if starti:
        print('Slēgts cikls no:', ', '.join(lauc(x) for x in starti))
        return
    atv = [(c, r) for c in range(n) for r in range(n)
           if (p := cels(s, (c, r), n)) and len(set(p)) == n * n]
    if atv:
        print('Atvērts Hamiltona ceļš no:', ', '.join(lauc(x) for x in atv))
    else:
        print('Nav derīgs ne no viena lauciņa.')


def rezims_katalogs(n):
    if n % 4 != 2:
        print(f'n={n}: 90° simetriskas slēgtas tūres nav iespējamas (vajag n ≡ 2 mod 4).')
        return
    q = n * n // 4
    rot = lambda p: (p[1], n - 1 - p[0])        # pulksteņrādītāja virzienā
    rezultati = {}

    def dfs(p, aiznemts, sekla):
        if len(sekla) == q:
            # pēc q gājieniem jānonāk rot(sākums) -> cikls noslēdzas
            if p[-1] == rot(p[0]):
                s = ''.join(afins(sekla, 1, 2 * k) for k in range(4))
                pilns = cels(s, p[0], n)
                if pilns and len(pilns) == n * n + 1:
                    rezultati.setdefault(malas(pilns), []).append((p[0], sekla))
            return
        for d, (dx, dy) in enumerate(K):
            x = (p[-1][0] + dx, p[-1][1] + dy)
            if not (0 <= x[0] < n and 0 <= x[1] < n):
                continue
            kopijas = [x, rot(x), rot(rot(x)), rot(rot(rot(x)))]
            beigas = len(sekla) == q - 1
            if beigas:
                # pēdējais solis drīkst nonākt tikai rot(sākums)
                if x != rot(p[0]):
                    continue
                dfs(p + [x], aiznemts, sekla + str(d + 1))
                continue
            if any(k in aiznemts for k in kopijas):
                continue
            dfs(p + [x], aiznemts | set(kopijas), sekla + str(d + 1))

    for c in range(n):
        for r in range(n):
            s0 = (c, r)
            k0 = {s0, rot(s0), rot(rot(s0)), rot(rot(rot(s0)))}
            if len(k0) == 4:
                dfs([s0], k0, '')

    # sagrupē pēc ģeometriskās ekvivalences (8 dēļa simetrijas)
    def transf(E, f):
        return frozenset(frozenset(f(v) for v in e) for e in E)
    m = n - 1
    simetr = [lambda v: v, lambda v: (v[1], m - v[0]), lambda v: (m - v[0], m - v[1]),
              lambda v: (m - v[1], v[0]), lambda v: (m - v[0], v[1]), lambda v: (v[0], m - v[1]),
              lambda v: (v[1], v[0]), lambda v: (m - v[1], m - v[0])]
    klases = []
    for E in rezultati:
        if not any(any(transf(E, f) == K2 for f in simetr) for K2 in klases):
            klases.append(E)
    print(f'{n}×{n}: {len(rezultati)} slēgtas 90° simetriskas tūres '
          f'({len(klases)} ģeometriski atšķirīgas).\n')
    for i, E in enumerate(klases, 1):
        print(f'--- Klase {i} ---')
        varianti = []
        for f in simetr:
            E2 = transf(E, f)
            if E2 in rezultati and E2 not in varianti:
                varianti.append(E2)
        for j, E2 in enumerate(varianti):
            nos = 'tūre' if j == 0 else 'spoguļkopija'
            ieraksti = sorted(rezultati[E2], key=lambda t: (t[0][1], t[0][0]))
            a1 = [s for st, s in ieraksti if st == (0, 0)]
            print(f'  {nos}: {len(ieraksti)} sēklas (dažādi sākuma lauciņi)')
            for s in (a1 or [ieraksti[0][1]]):
                print(f'    sēkla {s}  no a1' if a1 else f'    sēkla {s}  no {lauc(ieraksti[0][0])}')
                if a1:
                    saglabat(''.join(afins(s, 1, 2 * k) for k in range(4)), f'90_klase{i}_{"ture" if j == 0 else "spogulis"}')
        print()


def rezims_katalogs180(n, visas=False):
    """Visas slēgtās tūres ar 180° rotācijas simetriju.
    Sēkla = pirmā puse (n*n/2 cipari); otrā puse = sēkla ar +4 katram ciparam."""
    if n % 2:
        print('Vajag pāra n.')
        return
    if n > 6:
        print(f'Uzmanību: n={n} šai meklēšanai ir ļoti smags (var ilgt dienām).',
              file=sys.stderr)
    q = n * n // 2
    m = n - 1
    r2 = lambda p: (m - p[0], m - p[1])
    tures = set()

    # Katrs slēgts cikls iet caur a1, tāpēc pietiek sākt no a1.
    # 180° simetrijā a1 un tā pretpunkts vienmēr ir tieši pusceļā viens no otra.
    def dfs(p, aiznemts):
        if len(p) == q:
            for dx, dy in K:
                if (p[-1][0] + dx, p[-1][1] + dy) == r2(p[0]):
                    pilns = p + [r2(v) for v in p]
                    tures.add(malas(pilns + [pilns[0]]))
            return
        for dx, dy in K:
            x = (p[-1][0] + dx, p[-1][1] + dy)
            if not (0 <= x[0] < n and 0 <= x[1] < n):
                continue
            if x in aiznemts or r2(x) in aiznemts:
                continue
            dfs(p + [x], aiznemts | {x, r2(x)})

    dfs([(0, 0)], {(0, 0), r2((0, 0))})

    simetr = [lambda v: v, lambda v: (v[1], m - v[0]), lambda v: (m - v[0], m - v[1]),
              lambda v: (m - v[1], v[0]), lambda v: (m - v[0], v[1]), lambda v: (v[0], m - v[1]),
              lambda v: (v[1], v[0]), lambda v: (m - v[1], m - v[0])]
    transf = lambda E, f: frozenset(frozenset(f(v) for v in e) for e in E)

    def seklas_no_a1(E):
        """Abi apgaitas virzieni no a1 -> pirmās puses virzienu rindas."""
        kaim = {}
        for e in E:
            a, b = tuple(e)
            kaim.setdefault(a, []).append(b)
            kaim.setdefault(b, []).append(a)
        rez = []
        for pirmais in kaim[(0, 0)]:
            p = [(0, 0), pirmais]
            while len(p) < n * n:
                p.append([x for x in kaim[p[-1]] if x != p[-2]][0])
            p.append(p[0])
            s = ''.join(str(K.index((b[0] - a[0], b[1] - a[1])) + 1)
                        for a, b in zip(p, p[1:]))
            assert s[q:] == afins(s[:q], 1, 4)      # otrā puse = pirmā +4
            rez.append(s[:q])
        return rez

    klases = []
    for E in tures:
        if not any(E in kl for kl in klases):
            klases.append({transf(E, f) for f in simetr} & tures)

    R90 = simetr[1]
    info = []
    for kl in klases:
        E0 = next(iter(kl))
        ir90 = transf(E0, R90) == E0
        visas_s = sorted(s for E in kl for s in seklas_no_a1(E))
        info.append((ir90, visas_s))
    info.sort(key=lambda t: (not t[0], t[1][0]))

    print(f'{n}×{n}: {len(tures)} slēgtas 180° simetriskas tūres, '
          f'{len(klases)} ģeometriski atšķirīgas klases.')
    print(f'Sēkla = pirmie {q} cipari no a1; pilnā rinda = sēkla + (sēkla ar +4).\n')
    for i, (ir90, ss) in enumerate(info, 1):
        atz = '  [arī 90° simetriska]' if ir90 else ''
        print(f'Klase {i:2d}: sēkla {ss[0]}{atz}')
        saglabat(ss[0] + afins(ss[0], 1, 4), f'180_klase{i}')
        if visas:
            for s in ss[1:]:
                print(f'           {s}')


def generet_simetrisku(n, kartiba, rnd, mezglu_limits=200000, laika_limits=60):
    """Atrod vienu slēgtu tūri ar 90° (kartiba=4) vai 180° (kartiba=2) simetriju.
    Meklēšana dziļumā ar Varnsdorfa kārtošanu (vispirms lauciņi ar vismazāk brīvu
    kaimiņu) un nejaušu sajaukšanu; ja zars par ilgu, sāk no jauna."""
    import time
    m = n - 1
    R = (lambda p: (p[1], m - p[0])) if kartiba == 4 else (lambda p: (m - p[0], m - p[1]))
    def orbita(p):
        o = [p]
        for _ in range(kartiba - 1):
            o.append(R(o[-1]))
        return o
    q = n * n // kartiba
    kaimini = lambda p: [(p[0] + dx, p[1] + dy) for dx, dy in K
                         if 0 <= p[0] + dx < n and 0 <= p[1] + dy < n]
    t0 = time.time()
    while time.time() - t0 < laika_limits:
        sakums = (rnd.randrange(n), rnd.randrange(n))
        if len(set(orbita(sakums))) < kartiba:
            continue
        aizn = set(orbita(sakums)); cels_ = [sakums]; merkis = R(sakums); mezgli = [0]
        def dfs():
            mezgli[0] += 1
            if mezgli[0] > mezglu_limits:
                return False
            if len(cels_) == q:
                return merkis in kaimini(cels_[-1])
            kand = [x for x in kaimini(cels_[-1]) if x not in aizn]
            rnd.shuffle(kand)
            kand.sort(key=lambda x: sum(1 for y in kaimini(x) if y not in aizn))
            for x in kand:
                o = orbita(x)
                if len(set(o)) < kartiba or any(y in aizn for y in o):
                    continue
                aizn.update(o); cels_.append(x)
                if dfs():
                    return True
                cels_.pop(); aizn.difference_update(o)
            return False
        if dfs():
            soli = 2 if kartiba == 4 else 4
            sekla = ''.join(str(K.index((b[0] - a[0], b[1] - a[1])) + 1)
                            for a, b in zip(cels_, cels_[1:] + [merkis]))
            pilna = ''.join(afins(sekla, 1, soli * k) for k in range(kartiba))
            # pārbīda tā, lai rinda sāktos no a1 (simetrija saglabājas jebkurai nobīdei)
            p = cels(pilna, sakums, n)
            i = p.index((0, 0))
            pilna = pilna[i:] + pilna[:i]
            assert slegts_no(pilna, n) and (0, 0) in slegts_no(pilna, n)
            return pilna[:q], pilna
    return None


def rezims_piemers(n, skaits, sakl):
    import random
    if n % 2:
        print('Slēgtas tūres eksistē tikai pāra n.')
        return
    kartiba = 4 if n % 4 == 2 else 2
    print(f'{n}×{n}: {"90°" if kartiba == 4 else "180°"} simetriskas tūres '
          f'(sēkla {n*n//kartiba} cipari, sākums a1)\n')
    if sakl is None:
        sakl = random.SystemRandom().randrange(1_000_000)
    print(f'nejaušība: {sakl}   (lai atkārtotu tieši šos rezultātus: --nejausiba {sakl})\n')
    rnd = random.Random(sakl)
    atrastas = set()
    for _ in range(skaits * 10):
        r = generet_simetrisku(n, kartiba, rnd)
        if r and r[1] not in atrastas:
            atrastas.add(r[1])
            print(f'sēkla: {r[0]}')
            print(f'pilna: {r[1]}', flush=True)
            saglabat(r[1], f'{n}x{n}')
            print()
            if len(atrastas) >= skaits:
                break


# ---------- zīmēšana ----------

KRASAS = ['#ffe600', '#e45fc6', '#ff9a3c', '#7ea8ff', '#8fd36a', '#ff7a5c',
          '#f5c99b', '#5ef0f0', '#e98fd8', '#d39a4a', '#6fd0b8', '#ffe08a']


def atrast_dela_izmeru(rinda):
    """Slēgtam ciklam gājienu skaits = n*n, atvērtam ceļam = n*n - 1."""
    import math
    for garums in (len(rinda), len(rinda) + 1):
        n = math.isqrt(garums)
        if n * n == garums:
            return n
    return None


def atrast_sakumu(rinda, n):
    """Vispirms mēģina a1, tad visus lauciņus. Atgriež (sākums, slēgts?) vai None."""
    starti = [(0, 0)] + [(c, r) for c in range(n) for r in range(n) if (c, r) != (0, 0)]
    for st in starti:
        p = cels(rinda, st, n)
        if p is None:
            continue
        if len(p) == n * n + 1 and p[-1] == p[0]:
            return st, True
        if len(p) == n * n and len(set(p)) == n * n:
            return st, False
    return None


def simetrija_apraksts(p, slegts, n):
    m = n - 1
    E = malas(p if slegts else p)
    tr = lambda f: frozenset(frozenset(f(v) for v in e) for e in E)
    if tr(lambda v: (v[1], m - v[0])) == E:
        sim = '90°'
    elif tr(lambda v: (m - v[0], m - v[1])) == E:
        sim = '180°'
    else:
        sim = ''
    dalas = [f'{n}×{n}'] + ([sim] if sim else []) + ([] if slegts else ['open'])
    return ' · '.join(dalas)


def png_vards(rinda, max_garums=200):
    """Faila nosaukums = virzienu rinda + .png.
    Windows faila nosaukums drīkst būt līdz 255 zīmēm (un viss ceļš parasti līdz 260),
    tāpēc ļoti garām rindām (piem. 16×16 = 256 cipari) paturam sākumu + īsu kontrolsummu,
    lai nosaukums joprojām būtu unikāls."""
    import hashlib
    if len(rinda) <= max_garums:
        return rinda + '.png'
    h = hashlib.sha1(rinda.encode()).hexdigest()[:8]
    return f'{rinda[:max_garums - 9]}_{h}.png'


def rezims_zimet(rindas, izeja, paraksti, kolonnas, izmers, n_dots):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print('Zīmēšanai vajag Pillow bibliotēku:  pip install pillow')
        return
    tures = []
    for i, rinda in enumerate(rindas):
        n = n_dots or atrast_dela_izmeru(rinda)
        if not n:
            print(f'{i+1}. rinda: {len(rinda)} gājieni neatbilst neviena kvadrātiska dēļa izmēram.')
            return
        r = atrast_sakumu(rinda, n)
        if not r:
            print(f'{i+1}. rinda nav derīga zirdziņa tūre {n}×{n} dēlī ne no viena lauciņa.')
            return
        st, slegts = r
        p = cels(rinda, st, n)
        if paraksti and i < len(paraksti) and paraksti[i]:
            nos = paraksti[i]
        else:
            nos = simetrija_apraksts(p, slegts, n)
        tures.append((n, p, nos))

    S = 3                                     # zīmē 3x lielāku un samazina -> gludas līnijas
    P = izmers; M = max(20, P // 13); TXT = max(30, P // 11)
    kol = min(kolonnas, len(tures)); rind = (len(tures) + kol - 1) // kol
    W, H = kol * P, rind * (P + TXT)
    img = Image.new('RGB', (W * S, H * S), 'black')
    d = ImageDraw.Draw(img)
    fonts = None
    for f in ('DejaVuSans.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',
              'arial.ttf', 'C:/Windows/Fonts/arial.ttf', 'Arial.ttf'):
        try:
            fonts = ImageFont.truetype(f, int(TXT * 0.55) * S)
            break
        except OSError:
            pass
    if fonts is None:
        fonts = ImageFont.load_default()
    for i, (n, p, nos) in enumerate(tures):
        ox = (i % kol) * P; oy = (i // kol) * (P + TXT)
        solis = (P - 2 * M) / (n - 1)
        xy = [((ox + M + c * solis) * S, (oy + M + (n - 1 - r) * solis) * S) for c, r in p]
        biezums = max(2, int(S * P / 110 * (6 / max(n, 6)) ** 0.5))
        d.line(xy, fill=KRASAS[i % len(KRASAS)], width=biezums, joint='curve')
        tw = d.textlength(nos, font=fonts)
        d.text(((ox + P / 2) * S - tw / 2, (oy + P - M / 2) * S), nos, fill='white', font=fonts)
    img = img.resize((W, H), Image.LANCZOS)
    img.save(izeja)
    print(f'Saglabāts: {izeja}  ({len(tures)} tūres, {W}×{H} px)')


def main():
    ap = argparse.ArgumentParser(description='VCSM sēklu rīks (zirdziņa tūres)')
    ap.add_argument('rezims', choices=['grupa', 'kombinacijas', 'parbaudit', 'katalogs',
                                       'katalogs180', 'piemers', 'zimet'])
    ap.add_argument('rindas', nargs='*', metavar='rinda',
                    help='sēkla, pilna virzienu rinda vai fails (.hrz/.bin); zimet: vairākas')
    ap.add_argument('-n', type=int, default=None, help='dēļa izmērs (noklusējums 6; zimet: automātiski)')
    ap.add_argument('--saglabat', metavar='PREFIKSS',
                    help='katalogs/katalogs180/piemers: saglabāt katru tūri failā PREFIKSS_NNN_*.hrz')
    ap.add_argument('--formats', choices=['hrz', 'bin', 'abi'], default='hrz',
                    help='saglabāšanas formāts (noklusējums hrz)')
    ap.add_argument('--skaits', type=int, default=3, help='piemers: cik tūres ģenerēt')
    ap.add_argument('--nejausiba', type=int, default=None,
                    help='piemers: nejaušības sēkla (vienāds skaitlis -> vienādi rezultāti)')
    ap.add_argument('--visas', action='store_true',
                    help='grupa/kombinacijas: arī vienkārši apgrieztās rindas; katalogs180: visas sēklas')
    ap.add_argument('--izeja', default=None,
                    help='zimet: PNG nosaukums (noklusējums: virzienu rinda.png vai faila_vards.png)')
    ap.add_argument('--paraksti', default=None,
                    help='zimet: paraksti, atdalīti ar |  (tukšs = automātisks paraksts)')
    ap.add_argument('--kolonnas', type=int, default=3, help='zimet: kolonnu skaits')
    ap.add_argument('--izmers', type=int, default=520, help='zimet: viena paneļa izmērs px')
    a = ap.parse_args()

    import os
    rindas = []
    vardi = []                      # zimet: faila nosaukumam
    for x in a.rindas:
        vardi.append(os.path.splitext(os.path.basename(x))[0]
                     if x.lower().endswith(('.hrz', '.bin', '.txt', '.vrz')) else None)
        if x.lower().endswith(('.hrz', '.bin', '.txt', '.vrz')):
            if not os.path.isfile(x):
                ap.error(f'fails "{x}" nav atrasts mapē {os.getcwd()}')
            r = lasit_fails(x)
            if not r:
                ap.error(f'failā "{x}" nav neviena gājiena (cipari 1-8)')
            print(f'Nolasīti {len(r)} gājieni no {x}.', file=sys.stderr)
            x = r
        if not set(x) <= set('12345678'):
            ap.error('rindā drīkst būt tikai cipari 1-8')
        rindas.append(x)
    if a.rezims in ('grupa', 'kombinacijas', 'parbaudit', 'zimet') and not rindas:
        ap.error('šim režīmam vajag rindu vai failu')
    rinda = rindas[0] if rindas else None
    n = a.n or 6
    SAGLABAT['prefikss'] = a.saglabat
    SAGLABAT['formats'] = a.formats
    if a.rezims == 'grupa':
        rezims_grupa(rinda, a.visas)
    elif a.rezims == 'kombinacijas':
        rezims_kombinacijas(rinda, n, a.visas)
    elif a.rezims == 'parbaudit':
        rezims_parbaudit(rinda, n)
    elif a.rezims == 'piemers':
        rezims_piemers(n, a.skaits, a.nejausiba)
    elif a.rezims == 'katalogs':
        rezims_katalogs(n)
    elif a.rezims == 'katalogs180':
        rezims_katalogs180(n, a.visas)
    else:
        paraksti = a.paraksti.split('|') if a.paraksti else None
        izeja = a.izeja
        if not izeja:
            if len(rindas) > 1:
                izeja = f'galerija_{len(rindas)}_tures.png'
            elif vardi[0]:
                izeja = vardi[0] + '.png'
            else:
                izeja = png_vards(rindas[0])
        rezims_zimet(rindas, izeja, paraksti, a.kolonnas, a.izmers, a.n)


if __name__ == '__main__':
    main()
