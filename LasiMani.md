# Serpentīna Hamiltona Cikls (VCSM)

🇬🇧 English: [README.md](README.md)

**VCSM** (Vector Cycling Stitching Method) ir rīku kopa šaha zirdziņa gājienu
virknēm un no tām veidotiem **Hamiltona cikliem/ceļiem** — ar pielietojumu
prepress, tekstila un CNC/lāzera darbos, kur "serpentīna" tipa nepārtraukts
ceļš, kas apmeklē katru virsmas šūnu tieši vienu reizi, ir noderīgs reālai
fiziskai apstrādei (izšūšana, gravēšana, griešana u.tml.).

Ceļi tiek kodēti kā **ciparu virknes** (katrs cipars 1-8 = viens no 8
iespējamiem zirdziņa gājienu virzieniem), ļaujot kompakti glabāt un
pārveidot ļoti garus ceļus (simtiem tūkstošu gājienu).

## Satura rādītājs

- [Saites](#saites)
- [Darbplūsma](#darbplūsma)
- [Rīki](#rīki)
  - [Python](#python-rīki)
  - [C#](#c-rīki)
  - [Tīmekļa skatītājs](#tīmekļa-skatītājs)
- [Png2Seciba sīkāk](#png2seciba-sīkāk)
- [Tukšās šūnas un to aizpildīšana](#tukšās-šūnas-un-to-aizpildīšana)
- [Failu formāti](#failu-formāti)
- [Prasības](#prasības)

## Saites

- 🌐 Projekta lapa (GitHub Pages): <https://sverners.github.io/serpentine-hamiltonian-cycles/>
- 🏠 Autora mājaslapa: <https://verneraweb.wuaze.com/index.html>

## Darbplūsma

Tipiska pilna ceļa: no attēla/formas līdz gatavam apvienotam Hamiltona ciklam.

```
1. serpentinaHC3_6x6.exe 6 6
   → ģenerē 6×6 zirdziņa cikla blokus (cikls4abt6x6.hrz)

2. python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6
   → pārveido par gatavu "plēsto" .abt katalogu (hub-satelīts griešana)

3. Png2Seciba.exe attēls.png FFseciba.txt
   (veidota no PngSensorsToFFseciba.cs; vecāka Python versija ir
    python png_sensors_to_ffseciba.py attēls.png FFseciba.txt)
   → attēls (aizpildīta forma ar sensoru izgriezumiem UN/VAI tieva
     līnija/spirāle/kontūra) → rūtiņu tīkls → bloku secība (FFseciba.txt)
   → raksta arī FFseciba_karte.png/.txt (diagnostikas karte) un
     FFseciba_tuksas.txt (tukšās šūnas; fails ir tukšs, ja tādu nav)

4. python serpentina_hc_v12.py gatavie_abt_6x6 FFseciba.txt
   → saliek blokus (6×6, 16×16, ...) pēc FFseciba.txt secības
     → viens garš .hrz ceļš

5. Png2Seciba.exe --ffseciba=FFseciba.txt --aizpildit=cikls.hrz
   → tikai, ja 3. solī parādījās sarkans paziņojums par tukšajām šūnām:
     aizpilda tās ar atsevišķiem slēgtiem blokiem un saplūdina ar ciklu
     (skat. zemāk)

6. HCeluSavienoshanaWinForms.exe
   → apvieno DIVUS atsevišķus ceļus vienā (lūzuma punkti, rotācija,
     transformācijas, automātiska savienojumu meklēšana)

7. mikroScope.html
   → gatavā ceļa apskate pārlūkā, divu ceļu apvienošana arī tur,
     SVG eksports koplietošanai
```

Bloku secība `FFseciba.txt` failā nav atkarīga no bloka izmēra, tāpēc to pašu
failu var izmantot gala cikla veidošanai no 6×6, 16×16 vai citiem blokiem.

## Rīki

### Python rīki

| Fails | Apraksts |
|---|---|
| `cycles_to_gatavie_abt_6x6.py` | No savāktiem 6×6 zirdziņa cikliem izveido gatavu "plēsto" `.abt` katalogu vienā solī (hub-satelīts virziena griešana + rotācijas/spoguļojuma varianti). |
| `serpentina_hc_v12.py` | Saliek 6×6 (vai 16×16) blokus lielākā "palagā" pēc FFseciba shēmas. Atbalsta atvērtus un slēgtus ceļus, progresa ziņojumus meklēšanas laikā. |
| `diagnose_ffseciba.py` | Diagnostikas rīks — izsmeļoši pārbauda katru bloku pāreju FFseciba shēmā. |
| `png_sensors_to_ffseciba.py` | PNG attēls → FFseciba.txt (oriģinālā Python versija). Atpazīst gan blīvu taisnstūri ar sensoru izgriezumiem, gan tievu līniju/spirāli/kontūru (ar iztieknošanu, spuru tīrīšanu, savienoto komponenšu meklēšanu). Jaunākās iespējas, kas uzskaitītas sadaļā [Png2Seciba sīkāk](#png2seciba-sīkāk), ir C# versijā. |

**Nepieciešamās bibliotēkas:** `numpy`, `Pillow` (PIL), `scikit-image`, `scipy`.

### C# rīki

| Fails | Apraksts |
|---|---|
| `HCeluSavienoshanaWinForms.cs` | Windows Forms rīks DIVU HC ceļu savienošanai: lūzuma punkti (ar peles klikšķi vai koordinātēm), 2. ceļa rotācija, F1-F9 transformācijas (F5/F9 apgriež ceļu un saglabā lūzuma punktu uz tās pašas šķautnes), automātiska savienojumu meklēšana, +/- soļa pogas ātrai nobīžu/centra regulēšanai, redzamā attēla SVG eksports. |
| `PngSensorsToFFseciba.cs` | PNG → FFseciba.txt (kompilēts kā `Png2Seciba.exe`). Pašrakstīts Guo-Hall iztieknošanas algoritms un savienoto komponenšu marķēšana (aizstāj Python `scikit-image`/`scipy.ndimage`), jebkuras aprises aizpildītu formu atpazīšana, šaha krāsojuma izlīdzināšana ar kopā sabīdītām tukšajām šūnām, slēgta cikla būve, diagnostikas karte un **tukšo šūnu aizpildīšana gatavā `.hrz` ciklā**. Skat. [Png2Seciba sīkāk](#png2seciba-sīkāk). |

**Nepieciešams:** .NET Framework (Visual Studio) ar `System.Drawing` atsauci
(konsoles lietotnei) vai Windows Forms (grafiskajam rīkam).

### Tīmekļa skatītājs

| Fails | Apraksts |
|---|---|
| `mikroScope.html` | Patstāvīgs HTML/JS rīks liela HC ceļa ("palaga") apskatei un apvienošanai. Apskate: tālummaiņa, krāsu maiņa pēc gājiena numura, lūzuma punkta meklēšana pēc koordinātēm, palaga izmērs šūnās. Apvienošana (kā WinForms rīkā): 2. ceļš (dzeltens) ar nobīdēm un +/- soļa pogām, lūzuma punkti abiem ceļiem (arī ar klikšķi uz zīmējuma), saišu derīguma statuss, automātiska atvērumu piesaiste, F1-F9 transformācijas katram ceļam, apvienotā cikla eksports kā `.hrz`/`.bin` un redzamā attēla eksports kā SVG (ar iegultu peles ritenīša tuvināšanu/vilkšanu un dubultklikšķa detalizētu tuvinājumu). |

Atver tieši pārlūkā (dubultklikšķis) — nav vajadzīgs serveris.

## Png2Seciba sīkāk

```
Png2Seciba attēls.png [FFseciba.txt] [opcijas]
Png2Seciba attēls.png --aizpildit=cikls.hrz
Png2Seciba --ffseciba=FFseciba.txt --aizpildit=cikls.hrz
```

**Attēla noteikumi.** Melns = virsma, kas jāapmeklē, balts = sensori / aizliegtās
zonas. Viena šūna = 4×4 pikseļi. Melni apgabali, kas plānāki par 12 px, tiek
iztieknoti līdz vienas šūnas līnijai; biezāki apgabali tiek uzskatīti par jebkuras
aprises aizpildītu virsmu. Sīki melni plankumi (mazāki par 60 px) tiek ignorēti kā
troksnis. Pārpalikušie pikseļi labajā/apakšējā malā skaitās kā pilna šūna, un
nepāra rindu/kolonnu skaits tiek izlīdzināts uz pāra, nokopējot pēdējo aizpildīto
rindu/kolonnu.

**Izvades faili**

| Fails | Saturs |
|---|---|
| `FFseciba.txt` | Bloku secība (apmeklēšanas kārtas numurs katrai šūnai). Tiek rakstīts tikai slēgtam ciklam (skat. `--atlaut-atvertu`). |
| `FFseciba_karte.png` / `.txt` | Diagnostikas karte: **zaļš** = virsma (OK), **balts** = aizliegts (sensori), **sarkans** = tukšās šūnas, **dzeltens** = piespiedu gals. Josla augšā paziņo, vai cikls ir iespējams un kāpēc nav. |
| `FFseciba_tuksas.txt` | Tukšās šūnas (`rinda,kolonna`); fails ir tukšs, ja tādu nav. |
| `FFseciba_starprezultats.txt` / `.png` | Starprezultāts, ja iespējams tikai **atvērts** ceļš: numurēts ceļš un attēls ar atzīmētu sākumu/beigām, lai attēlu varētu izlabot. |

Ja izvades failu izmanto cita programma (piem., Excel), rezultāts netiek zaudēts:
tas tiek saglabāts kā `nosaukums_1.txt`, `nosaukums_2.txt`, ... un tiek izdrukāts
brīdinājums.

**Opcijas**

| Opcija | Nozīme |
|---|---|
| `--atlaut-atvertu` | Pieņemt atvērtu ceļu kā gala rezultātu. Tievo līniju formām vienmēr ir līnijas gali, tāpēc tām šī opcija ir vajadzīga. |
| `--tikai-karte` | Saglabāt karti un diagnostiku un apstāties pirms atvērtā ceļa meklēšanas. |
| `--laiks=SEK` | Laika ierobežojums katram meklēšanas posmam (noklusējums 120, 0 = bez ierobežojuma). |
| `--forse` | Meklēt arī tad, ja pierādīts, ka cikls/ceļš neiespējams (aizpildīšanas režīmā: atļaut mazāku `.hrz` tīklu). |
| `--neizlidzinat` | Neizlīdzināt šaha krāsojumu (neviena šūna netiek atstāta tukša). |
| `--vecais-tikls` | Vecā tīkla uzvedība (pārpalikušie pikseļi ignorēti, bez pāra izmēra papildinājuma). |
| `--aizpildit=FAILS.hrz` | Aizpildīšanas režīms: esošais slēgtais cikls, ko aizpildīt. |
| `--ffseciba=FAILS.txt` | Aizpildīšanas režīms bez attēla: virsma no FFseciba faila (+ `*_tuksas.txt`). |
| `--hrz-izvade=FAILS.hrz` | Aizpildītā cikla nosaukums (noklusējums `<ieejas nosaukums>_aizpildits.hrz`). |
| `--tuksas=FAILS` | Tukšo šūnu saraksts, ja tas nav `<FFseciba>_tuksas.txt`. |
| `--bloks=N` | Bloka izmērs (parasti tiek noteikts automātiski). |
| `--cikli=FAILS` | Savs slēgto N×N bloku ciklu katalogs (rinda = viens cikls, cipari); tie tiek izmēģināti pirmie. |

## Tukšās šūnas un to aizpildīšana

**Kāpēc šūnas var palikt tukšas.** Nokrāso šūnas kā šaha galdiņu. Katrā solī
mainās krāsa, tāpēc slēgtam ciklam pa visām šūnām abu krāsu šūnu skaitam jābūt
vienādam. Formām, piemēram, ar apaļu izgriezumu, bieži vienas krāsas šūnu ir par
dažām vairāk. Tad Png2Seciba atstāj minimālo skaitu šūnu **tukšas**, izvieto tās
kopā kā vienu diagonālu ķēdi (sākuma šūna netiek noņemta), pārbauda, ka pārējai
virsmai cikls eksistē, un uzskaita tukšās šūnas: `FFseciba_tuksas.txt` failā un
**sarkanā paziņojumā** konsolē (ja tukšu šūnu nav, nekas netiek izdrukāts).

**Kā tās aizpilda.** Katra šūna pati par sevi ir slēgts bloks (zirdziņa cikls).
Kad gala `.hrz` ir salikts, katra tukšā šūna tiek aizpildīta ar savu slēgtu N×N
ciklu un saplūdināta ar lielo ciklu, apmainot divas šķautnes: lielā cikla
šķautne a–b un jaunā bloka šķautne c–d tiek aizstātas ar a–c un b–d (abām jābūt
zirdziņa gājieniem). Rezultāts ir **viens** slēgts cikls, kas apmeklē arī
iepriekš tukšās šūnas.

- Bloka izmērs N tiek noteikts automātiski: `.hrz` laukums ir tīkla izmērs × N
  (piem., 640 × 576 pie 40 × 36 tīkla → N = 16). Der jebkurš pāra N ≥ 6.
- `.hrz` y ass orientācija tiek noteikta automātiski.
- Rezultāts tiek pārbaudīts: viens slēgts cikls, visi punkti unikāli, visi bloki pilni.
- Vajadzīgs slēgts `.hrz` no pilniem N×N blokiem un tas pats attēls vai FFseciba
  fails. Šūnu, kurai nav neviena apmeklēta kaimiņa, aizpildīt nevar.

## Failu formāti

- **`.hrz` / `.vrz` / `.txt`** — ceļa gājienu virkne kā ciparu teksts (1-8 katrs cipars).
- **`.bin`** — tas pats, bināri kompaktākā formā.
- **`FFseciba.txt`** — CSV tipa rūtiņu tīkls, kur katra šūna satur tās apmeklējuma kārtas numuru (vai tukša, ja šūna netiek apmeklēta: sensors, ārpus formas vai tukšā šūna).
- **`FFseciba_tuksas.txt`** — tukšās šūnas, katrā rindā `rinda,kolonna`.
- **`FFseciba_karte.png` / `.txt`** — atpazītās virsmas diagnostikas karte.
- **`.abt`** — 6×6 (vai 16×16) "plēsto" bloku katalogs ar manifestu, ko izmanto `serpentina_hc_v12.py` bloku saskaņošanai.

## Prasības

- **Python 3.x** ar `numpy`, `Pillow`, `scikit-image`, `scipy`
- **.NET Framework** (Visual Studio) C# rīkiem
- Jebkurš mūsdienu pārlūks `mikroScope.html` skatīšanai

---

*Projekts attīstīts pakāpeniski, risinot reālas problēmas ar lielu (6×6, 16×16, un lielāku) Hamiltona ciklu salikšanu, attēlu-uz-ceļu konversiju un ceļu vizuālu apskati/apvienošanu.*
